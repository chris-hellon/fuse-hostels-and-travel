using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Fuse.Application.Common.Jobs;
using Fuse.Application.Common.Mail;
using Fuse.Application.Common.Payments;

namespace Fuse.Web.Tests;

/// <summary>Stands in for Stripe: checkouts are remembered, and "paid" when a test says so.</summary>
public sealed class FakePayments : IPaymentGateway
{
    public ConcurrentDictionary<string, (CheckoutRequest Request, bool Paid)> Sessions { get; } = new();
    public ConcurrentBag<(string Session, decimal Amount)> Refunds { get; } = [];

    public bool IsConfigured => true;

    /// <summary>Who paid a checkout, when it wasn't the booking's guest (Stripe's customer details).</summary>
    public ConcurrentDictionary<string, string> PaidBy { get; } = new();

    public void MarkPaid(string sessionId, string? paidByEmail = null)
    {
        Sessions[sessionId] = Sessions[sessionId] with { Paid = true };
        if (paidByEmail is not null)
            PaidBy[sessionId] = paidByEmail;
    }

    public Task RefundAsync(string checkoutSessionId, decimal amount, CancellationToken cancellationToken)
    {
        Refunds.Add((checkoutSessionId, amount));
        return Task.CompletedTask;
    }

    public Task<decimal> SessionTotalAsync(string checkoutSessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.TryGetValue(checkoutSessionId, out var s) && s.Paid ? Total(s.Request) : 0m);

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var id = $"cs_test_{Guid.NewGuid():N}";
        Sessions[id] = (request, false);
        return Task.FromResult(new CheckoutSession(id, $"https://checkout.stripe.test/{id}"));
    }

    public Task<CheckoutResult?> GetCheckoutAsync(string checkoutSessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.TryGetValue(checkoutSessionId, out var s)
            ? new CheckoutResult(checkoutSessionId, s.Paid, s.Request.BookingId.ToString(), Total(s.Request),
                PaidBy.GetValueOrDefault(checkoutSessionId) ?? s.Request.CustomerEmail, "Card Holder")
            {
                // As Stripe: told apart by where it returns (the website's checkout returns to legacy's website page).
                WebsiteCheckout = s.Request.WebsiteCheckout,
            }
            : null);

    public CheckoutEvent? ReadCheckoutEvent(string payload, string signature) => throw new InvalidOperationException("Not in tests.");

    private static decimal Total(CheckoutRequest request) => request.Lines.Sum(l => l.UnitAmount * l.Quantity);
}

/// <summary>Keeps sent emails instead of sending them.</summary>
public sealed class CapturedEmails : IEmailSender
{
    public ConcurrentBag<EmailMessage> Sent { get; } = [];

    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Add(message);
        return Task.FromResult(true);
    }
}

/// <summary>Runs background jobs straight away, so tests can check their effects.</summary>
public sealed class InlineJobs(IServiceScopeFactory scopes) : IBackgroundJobs
{
    public void Enqueue<TService>(System.Linq.Expressions.Expression<Func<TService, Task>> job) where TService : notnull
    {
        using var scope = scopes.CreateScope();
        job.Compile()(scope.ServiceProvider.GetRequiredService<TService>()).GetAwaiter().GetResult();
    }

    /// <summary>Recorded, not run: a test runs what it needs itself.</summary>
    public ConcurrentQueue<(Hangfire.Common.Job Job, TimeSpan Delay)> Scheduled { get; } = [];

    public void Schedule<TService>(System.Linq.Expressions.Expression<Func<TService, Task>> job, TimeSpan delay) where TService : notnull =>
        Scheduled.Enqueue((Hangfire.Common.Job.FromExpression(job), delay));
}

public sealed class CapturedWebhooks : IPaidBookingWebhook
{
    public ConcurrentBag<(DefaultIdType Booking, IReadOnlyList<string> CloudbedsIds)> Sent { get; } = [];

    public bool IsEnabled => true;

    public Task NotifyAsync(DefaultIdType bookingId, IReadOnlyList<string> cloudbedsGuestIds, CancellationToken cancellationToken)
    {
        Sent.Add((bookingId, cloudbedsGuestIds));
        return Task.CompletedTask;
    }
}

/// <summary>reCAPTCHA without Google: only <see cref="Pass"/> passes.</summary>
public sealed class FakeCaptcha : Application.Common.Security.ICaptcha
{
    public const string Pass = "captcha-ok";

    public Task<bool> VerifyAsync(string response, CancellationToken cancellationToken) => Task.FromResult(response == Pass);
}

/// <summary>An inbox for tests: emails put in <see cref="Threads"/> are what "Gmail" holds.</summary>
public sealed class FakeMailbox : Application.Common.Mail.IMailbox
{
    public bool IsEnabled { get; set; } = true;
    public string? Address { get; set; } = "team@tests.invalid";

    /// <summary>Thread id → its emails.</summary>
    public ConcurrentDictionary<string, List<Application.Common.Mail.MailboxMessage>> Threads { get; } = new();

    public Task<string?> FindThreadAsync(string messageId, CancellationToken cancellationToken) =>
        Task.FromResult(Threads.FirstOrDefault(t => t.Value.Any(m => m.MessageId == messageId)).Key);

    public Task<IReadOnlyList<Application.Common.Mail.MailboxMessage>> ReadThreadAsync(string threadId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Application.Common.Mail.MailboxMessage>>(Threads.TryGetValue(threadId, out var list) ? [.. list.OrderBy(m => m.SentOnUtc)] : []);
}
