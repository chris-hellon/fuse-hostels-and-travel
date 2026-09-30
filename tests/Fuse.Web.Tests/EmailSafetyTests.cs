using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Fuse.Application.Common.Mail;
using Fuse.Infrastructure.Mail;

namespace Fuse.Web.Tests;

/// <summary>What the email sender lets out outside production.</summary>
public sealed class EmailSafetyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("travaloud-mail-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>SMTP on, in development, with the recipient allowed: as far as settings can go towards sending.</summary>
    private EmailSender Sender(string environment) => new(
        Options.Create(new MailOptions { Mode = "Smtp", Host = "smtp.invalid", Port = 465, PickupDirectory = "mail", AllowedRecipients = ["hostel@tests.invalid"] }),
        new Environment(environment, _root), NullLogger<EmailSender>.Instance);

    private static EmailMessage Manifest() => new(["hostel@tests.invalid"], "Today's Tour Manifests for Bar Hop - 18:00", "<p>Manifest</p>") { Automated = true };

    [Fact]
    public async Task Automated_emails_are_never_sent_outside_production_even_with_smtp_on()
    {
        await Sender("Development").SendAsync(Manifest(), TestContext.Current.CancellationToken);

        // Written locally instead (an SMTP attempt would have failed on smtp.invalid).
        Directory.GetFiles(Path.Combine(_root, "mail"), "*.eml").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Emails_someone_triggers_still_go_by_smtp_to_allowed_recipients()
    {
        var reminder = new EmailMessage(["hostel@tests.invalid"], "Payment reminder for booking 1", "<p>Pay</p>");

        // It tries the SMTP server (which doesn't exist here), rather than keeping it back.
        await Should.ThrowAsync<Exception>(() => Sender("Development").SendAsync(reminder, TestContext.Current.CancellationToken));
        Directory.Exists(Path.Combine(_root, "mail")).ShouldBeFalse();
    }

    private sealed class Environment(string name, string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Fuse.Web";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
