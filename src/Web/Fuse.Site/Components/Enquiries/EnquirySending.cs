using Fuse.Contracts.Common;
using Fuse.Contracts.Site;
using Fuse.Site.Components.Forms;

namespace Fuse.Site.Components.Enquiries;

/// <summary>
/// What the enquiry forms share: sending with the captcha's answer, the field messages that come back, and the
/// thank-you. The hidden <see cref="Website"/> field is for bots (people never see it).
/// </summary>
public sealed class EnquirySending
{
    private Dictionary<string, string[]> _errors = [];

    public bool Busy { get; private set; }
    public bool Sent { get; private set; }
    /// <summary>A problem with the whole form (not one field).</summary>
    public string? Problem { get; private set; }
    public string? Website { get; set; }

    public string? this[string field] => _errors.TryGetValue(field, out var messages) ? messages.FirstOrDefault() : null;

    public async Task SendAsync(Captcha captcha, Func<string, ValueTask> send)
    {
        if (Busy)
            return;
        (Busy, Problem, _errors) = (true, null, []);
        try
        {
            await send(await captcha.ResponseAsync());
            Sent = true;
        }
        catch (RequestValidationException invalid)
        {
            _errors = invalid.Errors.ToDictionary(e => e.Key, e => e.Value);
            Problem = "Please check the details above.";
        }
        catch (AppException failure)
        {
            Problem = failure.StatusCode == 429
                ? "You've sent us a few messages already. Please try again in a few minutes."
                : failure.Message;
        }
        finally
        {
            Busy = false;
        }
        // Each answer can only be checked once.
        if (!Sent)
            await captcha.ResetAsync();
    }

    public static string SentMessage => EnquiryMessages.Sent;
}
