using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Fuse.Web.Dev;

/// <summary>
/// Development, the "ngrok" launch profile (Ngrok:ShowUrl): once the app is listening, asks the local ngrok agent
/// (its API on 127.0.0.1:4040) for the public URL forwarding to this app and writes it to the console, since the
/// app itself only knows localhost. With Ngrok:Url set (the reserved domain) and no tunnel to this app yet, it starts
/// "ngrok http {port} --url {Ngrok:Url}" itself, and stops it again with the app. A tunnel already running is reused.
/// </summary>
internal sealed class NgrokUrl(IServer server, IHostApplicationLifetime lifetime, IConfiguration configuration, ILogger<NgrokUrl> logger)
    : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(2), GiveUpAfter = TimeSpan.FromMinutes(5);

    /// <summary>The ngrok this app started (stopped with it); null when ngrok was already running or none was started.</summary>
    private Process? _ngrok;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await StartedAsync(stoppingToken))
            return;
        var port = server.Features.Get<IServerAddressesFeature>()?.Addresses
            .Select(a => Uri.TryCreate(a.Replace("*", "localhost").Replace("+", "localhost"), UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>().OrderBy(u => u.Scheme == "http" ? 0 : 1).FirstOrDefault()?.Port;
        if (port is null)
            return;

        using var agent = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:4040/"), Timeout = TimeSpan.FromSeconds(2) };
        var reserved = configuration["Ngrok:Url"]?.Trim().TrimEnd('/');
        if (await TunnelAsync(agent, port.Value, stoppingToken) is null && !string.IsNullOrEmpty(reserved))
            Start(port.Value, reserved.Contains("://") ? reserved : $"https://{reserved}");

        var hinted = false;
        var until = DateTime.UtcNow + GiveUpAfter;
        while (!stoppingToken.IsCancellationRequested && DateTime.UtcNow < until)
        {
            if (await TunnelAsync(agent, port.Value, stoppingToken) is { } url)
            {
                logger.LogWarning("ngrok: {Url} (admin: {Url}/admin) → http://localhost:{Port}", url, url, port);
                return;
            }
            if (_ngrok is { HasExited: true })
            {
                logger.LogWarning("ngrok stopped (exit code {Code}); see the lines above. Start it yourself with: ngrok http {Port}{Url}",
                    _ngrok.ExitCode, port, string.IsNullOrEmpty(reserved) ? "" : $" --url {reserved}");
                return;
            }
            if (!hinted && _ngrok is null)
            {
                logger.LogWarning("ngrok: no tunnel to this app yet. Start one with: ngrok http {Port}", port);
                hinted = true;
            }
            await Task.Delay(Every, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_ngrok is { HasExited: false })
        {
            _ngrok.Kill(entireProcessTree: true);
            logger.LogInformation("ngrok stopped");
        }
        _ngrok?.Dispose();
    }

    /// <summary>Starts ngrok for this app's port on the reserved URL, its log (errors only) in this app's console.</summary>
    private void Start(int port, string url)
    {
        var executable = Executable();
        try
        {
            _ngrok = Process.Start(new ProcessStartInfo(executable, ["http", port.ToString(), "--url", url, "--log", "stdout", "--log-format", "logfmt", "--log-level", "warn"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning("ngrok couldn't be started ({Executable}): {Message}. Set Ngrok:Path to where it's installed.", executable, ex.Message);
            return;
        }
        if (_ngrok is null)
            return;
        _ngrok.OutputDataReceived += (_, e) => Relay(e.Data);
        _ngrok.ErrorDataReceived += (_, e) => Relay(e.Data);
        _ngrok.BeginOutputReadLine();
        _ngrok.BeginErrorReadLine();
        logger.LogInformation("ngrok: starting {Url} → http://localhost:{Port}", url, port);
    }

    private void Relay(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
            logger.LogWarning("ngrok: {Line}", line);
    }

    /// <summary>
    /// ngrok on the PATH, or where Homebrew puts it: an IDE started from the Dock doesn't always have Homebrew on its PATH.
    /// Ngrok:Path overrides.
    /// </summary>
    private string Executable() =>
        configuration["Ngrok:Path"] is { Length: > 0 } path ? path
        : new[] { "/opt/homebrew/bin/ngrok", "/usr/local/bin/ngrok" }.FirstOrDefault(File.Exists) ?? "ngrok";

    /// <summary>The https URL of the agent's tunnel to this app's port; null when there's none (or no agent yet).</summary>
    private static async Task<string?> TunnelAsync(HttpClient agent, int port, CancellationToken stoppingToken)
    {
        try
        {
            var tunnels = await agent.GetFromJsonAsync<Tunnels>("api/tunnels", stoppingToken);
            return tunnels?.List?
                .Where(t => t.PublicUrl?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true && ForwardsTo(t.Config?.Addr, port))
                .Select(t => t.PublicUrl!.TrimEnd('/'))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !stoppingToken.IsCancellationRequested)
        {
            return null; // the agent isn't running (yet)
        }
    }

    /// <summary>True once the app is listening (its addresses are known).</summary>
    private async Task<bool> StartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource();
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await Task.WhenAny(started.Task, Task.Delay(Timeout.Infinite, stoppingToken));
        return started.Task.IsCompleted;
    }

    /// <summary>The tunnel's target ("http://localhost:5104", "localhost:5104" or just "5104") is this app's port.</summary>
    private static bool ForwardsTo(string? addr, int port) =>
        addr is not null && (addr == port.ToString() || addr.EndsWith($":{port}", StringComparison.Ordinal));

    private sealed record Tunnels([property: JsonPropertyName("tunnels")] IReadOnlyList<Tunnel>? List);

    private sealed record Tunnel([property: JsonPropertyName("public_url")] string? PublicUrl, [property: JsonPropertyName("config")] TunnelConfig? Config);

    private sealed record TunnelConfig([property: JsonPropertyName("addr")] string? Addr);
}
