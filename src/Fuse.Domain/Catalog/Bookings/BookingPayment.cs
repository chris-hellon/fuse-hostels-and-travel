using Fuse.Shared.Enums;

namespace Fuse.Domain.Catalog.Bookings;

public class BookingPayment : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private BookingPayment() { }

    public DefaultIdType BookingId { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(450)] public string? StripeSessionId { get; set; }
    public bool Refunded { get; set; }
    public bool? PartialRefund { get; private set; }
    [MaxLength(450)] public string? CustomerName { get; set; }
    [MaxLength(450)] public string? CustomerEmail { get; set; }
    [MaxLength(10)] public string? Currency { get; set; }
    public DateTime? PaymentDate { get; set; }
    public BookingPaymentMethod? PaymentMethod { get; set; }
    
    public BookingPayment(DefaultIdType bookingId, decimal amount, string? stripeSessionId, bool refunded, string? customerName, string? customerEmail, string? currency, BookingPaymentMethod? paymentMethod)
    {
        BookingId = bookingId;
        Amount = amount;
        StripeSessionId = stripeSessionId;
        Refunded = refunded;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        Currency = currency;
        PaymentMethod = paymentMethod;
        PaymentDate = DateTime.Now;
    }
    
    public BookingPayment Update(
        decimal? amount, string? stripeSessionId, bool? refunded, string? customerName, string? customerEmail, string? currency, DateTime? paymentDate, bool? partialRefund, BookingPaymentMethod? paymentMethod)
    {
        if (stripeSessionId is not null && StripeSessionId != stripeSessionId)
            StripeSessionId = stripeSessionId;

        if (amount is not null && Amount != amount)
            Amount = amount.Value;
        
        if (paymentDate is not null && PaymentDate != paymentDate)
            PaymentDate = paymentDate.Value;

        if (refunded is not null && Refunded != refunded)
            Refunded = refunded.Value;
        
        if (partialRefund is not null && PartialRefund != partialRefund)
            PartialRefund = partialRefund.Value;

        if (customerName is not null && CustomerName != customerName)
            CustomerName = customerName;

        if (customerEmail is not null && CustomerEmail != customerEmail)
            CustomerEmail = customerEmail;
        
        if (currency is not null && Currency != currency)
            Currency = currency;

        if (paymentMethod is not null && PaymentMethod != paymentMethod)
            PaymentMethod = paymentMethod.Value;
        
        return this;
    }

    public BookingPayment FlagAsPartialRefund()
    {
        PartialRefund = true;
        Refunded = false;
        return this;
    }
    
    public BookingPayment FlagAsRefund()
    {
        PartialRefund = false;
        Refunded = true;
        return this;
    }

    public BookingPayment SetPaymentMethod(BookingPaymentMethod paymentMethod)
    {
        PaymentMethod = paymentMethod;
        return this;
    }

    public virtual Booking Booking { get; set; } = null!;
}