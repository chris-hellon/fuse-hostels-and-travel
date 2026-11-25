using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Application.Common.Mail;
using Fuse.Contracts.Common;
using Fuse.Contracts.Enquiries;
using Fuse.Contracts.Site;
using Fuse.Infrastructure.Mail;
using Fuse.Infrastructure.Persistence;

namespace Fuse.Web.Tests;

/// <summary>Replying to enquiries from the admin, and their conversations read back from the team's inbox.</summary>
public sealed class EnquiryConversationTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A contact enquiry from the website, as the admin finds it.</summary>
    private async Task<(Fuse.Contracts.Api.IApi Admin, DefaultIdType Id, string Email)> EnquiryAsync()
    {
        var email = $"visitor-{Guid.NewGuid():N}@tests.invalid";
        await new Fuse.Web.Client.Api.HttpApi(app.Anonymous())
            .Send(new SendEnquiry("Sam Walker", email, "+84 123", "Do you have lockers?", null, null, FakeCaptcha.Pass, null), Ct);
        await using var scope = TestBookings.TenantScope(app);
        var id = await scope.ServiceProvider.GetRequiredService<AppDbContext>().GeneralEnquiries.Where(e => e.Email == email).Select(e => e.Id).SingleAsync(Ct);
        return (await app.AdminApiAsync(), id, email);
    }

    private static MailboxMessage Email(string thread, string from, string body, DateTime on, string? inReplyTo = null) =>
        new($"g-{Guid.NewGuid():N}", thread, $"{Guid.NewGuid():N}@mail.tests.invalid", from, null, "team@tests.invalid", "Re: Your enquiry", body, on);

    private async Task<bool> RepliedAsync(DefaultIdType id)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().GeneralEnquiries.Where(e => e.Id == id).Select(e => e.ResponseSent).SingleAsync(Ct);
    }

    [Fact]
    public async Task A_reply_is_emailed_threaded_and_quoting_the_enquiry_then_shown_in_the_conversation()
    {
        var (admin, id, email) = await EnquiryAsync();
        var notification = app.Emails.Sent.Single(m => m.ReplyTo == email);
        notification.MessageId.ShouldNotBeNull();
        (await admin.Send(new GetEnquiryConversation(EnquiryKind.General, id), Ct)).ShouldSatisfyAllConditions(
            c => c.Messages.ShouldBeEmpty(), // the team's copy of the enquiry isn't part of it
            c => c.ReplySubject.ShouldBe("Re: Your enquiry to Fuse Hostels & Travel"));

        await admin.Send(new ReplyToEnquiry(EnquiryKind.General, id, "Re: Your enquiry to Fuse Hostels & Travel", "<p>Yes, <strong>free</strong> lockers!</p>"), Ct);

        var reply = app.Emails.Sent.Single(m => m.To.Contains(email));
        reply.HtmlBody.ShouldContain("<strong>free</strong> lockers");
        reply.HtmlBody.ShouldContain("Do you have lockers?"); // quoted
        reply.References.ShouldContain(notification.MessageId!);
        reply.ReplyTo.ShouldBe("team@tests.invalid");
        var conversation = await admin.Send(new GetEnquiryConversation(EnquiryKind.General, id), Ct);
        var sent = conversation.Messages.ShouldHaveSingleItem();
        (sent.Kind, sent.To, sent.HtmlBody).ShouldBe((EnquiryMessageKind.Sent, email, "<p>Yes, <strong>free</strong> lockers!</p>"));
        sent.SentByName.ShouldNotBeNullOrEmpty();
        (await RepliedAsync(id)).ShouldBeTrue();

        await Should.ThrowAsync<RequestValidationException>(() => admin.Send(new ReplyToEnquiry(EnquiryKind.General, id, "Re: x", "<p><br></p>"), Ct).AsTask());
    }

    [Fact]
    public async Task The_enquirers_answer_is_read_from_the_inbox_once_and_puts_the_enquiry_back_to_not_replied()
    {
        var (admin, id, email) = await EnquiryAsync();
        await admin.Send(new ReplyToEnquiry(EnquiryKind.General, id, "Re: Your enquiry", "<p>Yes!</p>"), Ct);
        var ours = app.Emails.Sent.Single(m => m.To.Contains(email));

        // Gmail's conversation: our reply, then theirs.
        var thread = $"t-{Guid.NewGuid():N}";
        var now = DateTime.UtcNow;
        app.Mailbox.Threads[thread] =
        [
            Email(thread, "team@tests.invalid", "<p>Yes!</p>", now) with { MessageId = ours.MessageId },
            Email(thread, email, "<p>Great, see you Friday</p>", now.AddMinutes(5)),
        ];

        (await admin.Send(new CheckEnquiryInbox(EnquiryKind.General, id), Ct)).ShouldBe(1);
        (await admin.Send(new CheckEnquiryInbox(EnquiryKind.General, id), Ct)).ShouldBe(0); // not twice

        var messages = (await admin.Send(new GetEnquiryConversation(EnquiryKind.General, id), Ct)).Messages;
        messages.Select(m => m.Kind).ShouldBe([EnquiryMessageKind.Sent, EnquiryMessageKind.Received]);
        messages[1].HtmlBody.ShouldBe("<p>Great, see you Friday</p>");
        (await RepliedAsync(id)).ShouldBeFalse();

        // The team answers straight from the inbox: it shows too, and the enquiry is replied again.
        app.Mailbox.Threads[thread].Add(Email(thread, "team@tests.invalid", "<p>Perfect</p>", now.AddMinutes(10)));
        await using (var scope = TestBookings.TenantScope(app))
            (await scope.ServiceProvider.GetRequiredService<Application.Features.Enquiries.EnquiryInbox>().SyncRecentAsync(Ct)).ShouldBeGreaterThanOrEqualTo(1);
        (await admin.Send(new GetEnquiryConversation(EnquiryKind.General, id), Ct)).Messages.Last().ShouldSatisfyAllConditions(
            m => m.Kind.ShouldBe(EnquiryMessageKind.Sent), m => m.HtmlBody.ShouldBe("<p>Perfect</p>"));
        (await RepliedAsync(id)).ShouldBeTrue();
    }

    private async Task<EnquiryStatus> StatusAsync(Fuse.Contracts.Api.IApi admin, DefaultIdType id) =>
        (await admin.Send(new GetGeneralEnquiry(id), Ct)).Summary.Status;

    [Fact]
    public async Task The_status_follows_the_conversation_and_can_be_closed_and_filtered_on()
    {
        var (admin, id, email) = await EnquiryAsync();
        (await StatusAsync(admin, id)).ShouldBe(EnquiryStatus.New);

        await admin.Send(new ReplyToEnquiry(EnquiryKind.General, id, "Re: Your enquiry", "<p>Yes!</p>"), Ct);
        (await StatusAsync(admin, id)).ShouldBe(EnquiryStatus.Replied);

        await admin.Send(new SetEnquiryStatus(EnquiryKind.General, id, EnquiryStatus.Closed), Ct);
        (await StatusAsync(admin, id)).ShouldBe(EnquiryStatus.Closed);
        (await RepliedAsync(id)).ShouldBeTrue(); // legacy's flag: closed counts as replied
        (await admin.Send(new SearchGeneralEnquiries { Keyword = email, Status = EnquiryStatus.Closed }, Ct)).Items.ShouldHaveSingleItem();
        (await admin.Send(new SearchGeneralEnquiries { Keyword = email, Status = EnquiryStatus.New }, Ct)).Items.ShouldBeEmpty();

        // They write again: reopened, waiting on us.
        var ours = app.Emails.Sent.Single(m => m.To.Contains(email));
        var thread = $"t-{Guid.NewGuid():N}";
        app.Mailbox.Threads[thread] =
        [
            Email(thread, "team@tests.invalid", "<p>Yes!</p>", DateTime.UtcNow) with { MessageId = ours.MessageId },
            Email(thread, email, "<p>One more thing</p>", DateTime.UtcNow.AddMinutes(1)),
        ];
        await admin.Send(new CheckEnquiryInbox(EnquiryKind.General, id), Ct);
        (await StatusAsync(admin, id)).ShouldBe(EnquiryStatus.WaitingOnUs);
        (await admin.Send(new GetEnquiryConversation(EnquiryKind.General, id), Ct)).Status.ShouldBe(EnquiryStatus.WaitingOnUs);
        (await RepliedAsync(id)).ShouldBeFalse();
    }

    [Fact]
    public async Task Inbox_emails_are_cleaned_and_their_quoted_history_cut()
    {
        GmailMailbox.CleanHtml("""
            <div>Sounds good<script>alert(1)</script><img src="x" onerror="alert(2)"></div>
            <div class="gmail_quote"><div class="gmail_attr">On Tue, Fuse wrote:</div><blockquote>Old text</blockquote></div>
            """).ShouldSatisfyAllConditions(
            h => h.ShouldContain("Sounds good"),
            h => h.ShouldNotContain("script"),
            h => h.ShouldNotContain("onerror"),
            h => h.ShouldNotContain("Old text"));

        GmailMailbox.PlainToHtml("Thanks <3\n\nSee you\n\nOn Tue, 24 Nov 2026 at 10:00, Fuse <team@tests.invalid> wrote:\n> Old text")
            .ShouldBe("<p>Thanks &lt;3</p><p>See you</p>");
    }
}
