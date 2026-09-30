namespace Fuse.Contracts.Reports;

// Legacy Reports › Payments: every payment taken (Stripe or cash) in a period, by payment date (Vietnam time), newest
// first. Show Details opens the booking (as on Bookings); a paid booking can be refunded from its payment.

public sealed record PaymentRow
{
    public DefaultIdType Id { get; init; }
    public DefaultIdType BookingId { get; init; }
    /// <summary>The booking's number (legacy "Booking Id").</summary>
    public int Reference { get; init; }
    public DateTime? PaymentDate { get; init; }
    public DateTime BookingDate { get; init; }
    /// <summary>When it was refunded (legacy: the payment's last change once refunded), Vietnam time.</summary>
    public DateTime? RefundDate { get; init; }
    public decimal Amount { get; init; }
    public string? Currency { get; init; }
    public string? Method { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? StripeSessionId { get; init; }
    public bool Refunded { get; init; }
    public bool BookingCancelled { get; init; }
}

/// <summary>Payments taken between <see cref="From"/> and <see cref="To"/> (whole days; by default the last month to
/// today, as legacy). The search matches the customer's name or email, the booking number or the amount.</summary>
[Post("reports/payments/search"), Permission(FuseAction.View, FuseResource.Reports)]
public sealed record SearchPayments : PagedQuery, IQuery<PagedResult<PaymentRow>>
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>The payments (matching the search) as a spreadsheet, as legacy: a row per tour × payment of its booking, newest first.</summary>
[Post("reports/payments/export"), Permission(FuseAction.Export, FuseResource.Reports)]
public sealed record ExportPayments(DateOnly? From, DateOnly? To, string? Keyword) : IQuery<FileDownload>;

public sealed class SearchPaymentsValidator : AbstractValidator<SearchPayments>
{
    public SearchPaymentsValidator() =>
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).When(q => q.From is not null && q.To is not null).WithMessage("The end date must be on or after the start date.");
}

public sealed class ExportPaymentsValidator : AbstractValidator<ExportPayments>
{
    public ExportPaymentsValidator() =>
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).When(q => q.From is not null && q.To is not null).WithMessage("The end date must be on or after the start date.");
}
