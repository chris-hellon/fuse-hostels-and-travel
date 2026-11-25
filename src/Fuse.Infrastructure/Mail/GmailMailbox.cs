using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Mail;

namespace Fuse.Infrastructure.Mail;

/// <summary>
/// The team's Gmail inbox, read with the Gmail API using the same Google access as sending (Mail:GmailOAuth, whose
/// https://mail.google.com/ scope covers reading). Bodies come back cleaned for showing in the admin (no scripts, forms or
/// the like), with the quoted earlier emails that mail apps add under a reply cut off.
/// </summary>
internal sealed partial class GmailMailbox(HttpClient http, GmailAccessTokens tokens, IOptions<MailOptions> options) : IMailbox
{
    private static readonly HtmlSanitizer Sanitizer = new();

    // Where mail apps put the email being replied to (Gmail, Apple Mail and others, Outlook, Yahoo).
    private const string QuotedSelectors =
        ".gmail_quote, .gmail_quote_container, .gmail_attr, blockquote[type=cite], #appendonsend, #divRplyFwdMsg, .yahoo_quoted, #mail-editor-reference-message-container";

    private GmailOAuthOptions Gmail => options.Value.GmailOAuth;

    public bool IsEnabled => Gmail.IsConfigured;

    public string? Address => IsEnabled ? Gmail.FromAddress ?? Gmail.AccountEmail : null;

    public async Task<string?> FindThreadAsync(string messageId, CancellationToken cancellationToken)
    {
        using var json = await GetAsync($"messages?q={Uri.EscapeDataString($"rfc822msgid:{messageId}")}&includeSpamTrash=false", cancellationToken);
        return json.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
            ? messages[0].GetProperty("threadId").GetString()
            : null;
    }

    public async Task<IReadOnlyList<MailboxMessage>> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        using var json = await GetAsync($"threads/{Uri.EscapeDataString(threadId)}?format=full", cancellationToken);
        if (!json.RootElement.TryGetProperty("messages", out var messages))
            return [];
        var read = new List<MailboxMessage>();
        foreach (var message in messages.EnumerateArray())
        {
            // Drafts and trashed emails aren't part of the conversation.
            if (message.TryGetProperty("labelIds", out var labels) && labels.EnumerateArray().Any(l => l.GetString() is "DRAFT" or "TRASH"))
                continue;
            var payload = message.GetProperty("payload");
            var headers = Headers(payload);
            var from = ParseAddress(headers.GetValueOrDefault("from"));
            if (from.Address is null)
                continue;
            var sentOn = long.TryParse(message.GetProperty("internalDate").GetString(), out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                : DateTime.UtcNow;
            read.Add(new MailboxMessage(
                message.GetProperty("id").GetString()!,
                message.GetProperty("threadId").GetString()!,
                headers.GetValueOrDefault("message-id")?.Trim().Trim('<', '>'),
                from.Address,
                from.Name,
                headers.GetValueOrDefault("to") ?? "",
                headers.GetValueOrDefault("subject") ?? "",
                Body(payload),
                sentOn));
        }
        return [.. read.OrderBy(m => m.SentOnUtc)];
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(cancellationToken));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gmail answered {(int)response.StatusCode} for {path}: {await response.Content.ReadAsStringAsync(cancellationToken)}",
                null, response.StatusCode);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private static Dictionary<string, string> Headers(JsonElement payload)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (payload.TryGetProperty("headers", out var list))
            foreach (var header in list.EnumerateArray())
                headers.TryAdd(header.GetProperty("name").GetString() ?? "", header.GetProperty("value").GetString() ?? "");
        return headers;
    }

    private static (string? Address, string? Name) ParseAddress(string? header) =>
        MimeKit.MailboxAddress.TryParse(header ?? "", out var mailbox)
            ? (mailbox.Address, string.IsNullOrWhiteSpace(mailbox.Name) ? null : mailbox.Name)
            : (null, null);

    /// <summary>The email's text as safe HTML: its HTML part (cleaned) or its plain text, without the quoted earlier emails.</summary>
    internal static string Body(JsonElement payload)
    {
        if (Part(payload, "text/html") is { } html)
            return CleanHtml(html);
        return Part(payload, "text/plain") is { } text ? PlainToHtml(text) : "";
    }

    internal static string CleanHtml(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var quoted in document.QuerySelectorAll(QuotedSelectors).ToList())
            quoted.Remove();
        // Outlook: everything from its "From: ... Sent: ..." divider down is the email replied to.
        if (document.QuerySelector("#divRplyFwdMsg, div[style*='border-top:solid #E1E1E1']") is { } divider)
        {
            for (var next = divider.NextSibling; next is not null; next = divider.NextSibling)
                next.Parent!.RemoveChild(next);
            divider.Remove();
        }
        return Sanitizer.Sanitize(document.Body?.InnerHtml ?? "").Trim();
    }

    internal static string PlainToHtml(string text)
    {
        var kept = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            // "On Tue, 24 Nov 2026 at 10:00, Fuse <contact@…> wrote:" and the "> " lines under it are the quoted email.
            if (line.StartsWith('>') || WroteLine().IsMatch(line) || line.StartsWith("-----Original Message-----", StringComparison.Ordinal))
                break;
            kept.Add(line);
        }
        var paragraphs = string.Join('\n', kept).Trim().Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br>")}</p>"));
    }

    private static string? Part(JsonElement part, string mimeType)
    {
        if (part.TryGetProperty("mimeType", out var type) && string.Equals(type.GetString(), mimeType, StringComparison.OrdinalIgnoreCase)
            && part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data) && data.GetString() is { Length: > 0 } encoded)
            return Encoding.UTF8.GetString(FromBase64Url(encoded));
        if (part.TryGetProperty("parts", out var parts))
            foreach (var child in parts.EnumerateArray())
                if (Part(child, mimeType) is { } found)
                    return found;
        return null;
    }

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    [GeneratedRegex(@"^\s*On .{4,200}wrote:\s*$")]
    private static partial Regex WroteLine();
}
