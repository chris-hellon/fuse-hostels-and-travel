using System.ComponentModel.DataAnnotations.Schema;

namespace Fuse.Domain.Catalog.Bookings;

public class Booking : AuditableEntity, IAggregateRoot
{
    [MaxLength(2000)] public string Description { get; private set; } = null!;
    public decimal TotalAmount { get; private set; }
    public decimal? AmountOutstanding { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public int ItemQuantity { get; private set; }
    public bool IsPaid { get; private set; }
    public DateTime BookingDate { get; private set; }
    [MaxLength(450)] public string? GuestId { get; private set; }
    [MaxLength(450)] public string? GuestName { get; private set; }
    [MaxLength(450)] public string? GuestEmail { get; private set; }
    [MaxLength(450)] public string? StripeSessionId { get; private set; }
    [MaxLength(450)] public string? UpdateStripeSessionId { get; private set; }
    public bool? BookingConfirmed { get; private set; }
    public bool? WaiverSigned { get; private set; }
    [MaxLength(2000)] public string? AdditionalNotes { get; private set; }
    [MaxLength(128)] public string? BookingSource { get; private set; }
    public bool? ConfirmationEmailSent { get; private set; }
    public bool? Refunded { get; private set; }
    public bool? PartialRefund { get; private set; }
    public bool? Cancelled { get; private set; }
    
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int InvoiceId { get; private set; }

    /// <summary>
    /// Optimistic concurrency: goes up with every change that the booking editor shows or saves (see <see cref="Touch"/>), so
    /// an editor opened before it can't save over it.
    /// </summary>
    public int ConcurrencyVersion { get; set; }

    /// <summary>Optional "pack" incentive this booking is marked for (the mandatory pack remark); null = no mark.</summary>
    public DefaultIdType? PackIncentiveId { get; set; }

    /// <summary>The pack's sale-price tier the booking is charged at (when marked for a pack).</summary>
    public DefaultIdType? PackIncentiveTierId { get; set; }

    public virtual IList<BookingItem> Items { get; set; } = null!;
    public virtual IList<BookingPayment> Payments { get; set; } = null!;
    public virtual IList<BookingPackage> BookingPackages { get; set; } = new List<BookingPackage>();
    
    public Booking()
    {
    }

    public Booking(
        string description,
        decimal totalAmount,
        string currencyCode,
        int itemQuantity,
        bool isPaid,
        DateTime bookingDate,
        string? guestId,
        string? stripeSessionId,
        bool? waiverSigned,
        string? guestName,
        string? guestEmail,
        string? additionalNotes,
        string? bookingSource,
        bool? refunded, 
        decimal? amountOutstanding)
    {
        Description = description;
        TotalAmount = totalAmount;
        CurrencyCode = currencyCode;
        ItemQuantity = itemQuantity;
        IsPaid = isPaid;
        BookingDate = bookingDate;
        GuestId = guestId;
        StripeSessionId = stripeSessionId;
        WaiverSigned = waiverSigned;
        GuestName = guestName;
        GuestEmail = guestEmail;
        AdditionalNotes = additionalNotes;
        BookingSource = bookingSource;
        Refunded = refunded;
        AmountOutstanding = amountOutstanding;
    }

    /// <summary>Sets the lead guest's email, clearing it when they have none (<see cref="Update"/> ignores nulls).</summary>
    public Booking SetGuestEmail(string? guestEmail)
    {
        GuestEmail = guestEmail;
        return this;
    }

    public Booking Update(
        string? description = null,
        decimal? totalAmount = null,
        string? currencyCode = null,
        int? itemQuantity = null,
        bool? isPaid = null,
        DateTime? bookingDate = null,
        string? guestId = null,
        string? stripeSessionId = null,
        bool? waiverSigned = null,
        string? guestName = null,
        string? guestEmail = null,
        string? additionalNotes = null,
        string? bookingSource = null,
        bool? refunded = null,
        IList<BookingItem>? items = null,
        DefaultIdType? createdBy = null, 
        bool doNotUpdateAmount = false,
        decimal? amountOutstanding = null,
        string? updateStripeSessionId = null,
        bool? cancelled = null)
    {
        if (description is not null && Description != description)
            Description = description;

        if (!doNotUpdateAmount)
        {
            if (totalAmount is not null && TotalAmount != totalAmount)
                TotalAmount = totalAmount.Value;
            
            if (amountOutstanding is not null && AmountOutstanding != amountOutstanding)
                AmountOutstanding = amountOutstanding.Value;
        }

        if (currencyCode is not null && CurrencyCode != currencyCode)
            CurrencyCode = currencyCode;

        if (itemQuantity is not null && ItemQuantity != itemQuantity)
            ItemQuantity = itemQuantity.Value;

        if (isPaid is not null && IsPaid != isPaid)
            IsPaid = isPaid.Value;

        if (bookingDate is not null && BookingDate != bookingDate)
            BookingDate = bookingDate.Value;

        if (guestId is not null && GuestId != guestId)
            GuestId = guestId;

        if (items is not null && Items != items)
            Items = items;

        if (stripeSessionId is not null && StripeSessionId != stripeSessionId)
            StripeSessionId = stripeSessionId;
        
        if (updateStripeSessionId is not null && UpdateStripeSessionId != updateStripeSessionId)
            UpdateStripeSessionId = updateStripeSessionId;

        if (waiverSigned is not null && WaiverSigned != waiverSigned)
            WaiverSigned = waiverSigned.Value;

        if (guestName is not null && GuestName != guestName)
            GuestName = guestName;

        if (guestEmail is not null && GuestEmail != guestEmail)
            GuestEmail = guestEmail;

        AdditionalNotes = additionalNotes;
        BookingSource = bookingSource;
        
        if (refunded is not null && Refunded != refunded)
            Refunded = refunded.Value;
        
        if (cancelled is not null && Cancelled != cancelled)
            Cancelled = cancelled.Value;

        if (!createdBy.HasValue || CreatedBy == createdBy) return this;
        
        OverrideCreatedBy = true;
        CreatedBy = createdBy.Value;

        return this;
    }

    /// <summary>
    /// Marks the booking changed (payments, refunds, cancellations, attendance, edits): once per save, so an editor holding
    /// the previous <see cref="ConcurrencyVersion"/> is refused rather than saving over the change.
    /// </summary>
    public Booking Touch()
    {
        ConcurrencyVersion++;
        return this;
    }

    public Booking FlagBookingAsPaid()
    {
        IsPaid = true;
        BookingConfirmed = true;
        AmountOutstanding = 0;

        return this;
    }

    /// <summary>
    /// Applies a manually recorded payment (cash or a Stripe payment taken outside the checkout flow),
    /// reducing the outstanding balance. When the balance reaches zero the booking is flagged as paid.
    /// </summary>
    public Booking ApplyManualPayment(decimal amount)
    {
        var currentOutstanding = AmountOutstanding ?? (IsPaid ? 0m : TotalAmount);
        var newOutstanding = currentOutstanding - amount;

        if (newOutstanding <= 0)
        {
            IsPaid = true;
            BookingConfirmed = true;
            AmountOutstanding = 0;
        }
        else
        {
            AmountOutstanding = newOutstanding;
        }

        return this;
    }
    
    public Booking FlagBookingAsUnPaid()
    {
        IsPaid = false;
        BookingConfirmed = false;
        AmountOutstanding = null;
        
        return this;
    }

    public Booking FlagConfirmationEmailSent()
    {
        ConfirmationEmailSent = true;

        return this;
    }
    
    public Booking FlagBookingAsCancelled()
    {
        Cancelled = true;
        Refunded = false;
        PartialRefund = false;
        IsPaid = false;
        
        Items = Items.Select(x =>
        {
            x.FlagAsCancelled();
            return x;
        }).ToList();
        
        return this;
    }
    
    public Booking FlagBookingAsRefunded()
    {
        Refunded = true;
        Payments = Payments.Select(x =>
        {
            x.Refunded = true;
            return x;
        }).ToList();
        
        Items = Items.Select(x =>
        {
            x.FlagAsRefunded();
            return x;
        }).ToList();
        
        return this;
    }
    
    /// <summary>
    /// Cancels the booking and every tour and guest on it, keeping payment and refund flags (as the
    /// CancelBooking procedure did). Spaces are freed by the caller.
    /// </summary>
    public void Cancel()
    {
        Cancelled = true;
        foreach (var item in Items ?? [])
            item.Cancel();
    }

    /// <summary>Anything refunded, wholly or in part: on the booking, one of its tours or one of its payments.</summary>
    public bool HasRefunds =>
        Refunded == true || PartialRefund == true
        || (Items ?? []).Any(i => i.Refunded == true || i.PartialRefund == true)
        || (Payments ?? []).Any(p => p.Refunded || p.PartialRefund == true);

    /// <summary>Undoes <see cref="Cancel"/>: the booking and every tour and guest on it are live again. Spaces are retaken by the caller.</summary>
    public void Uncancel()
    {
        Cancelled = false;
        foreach (var item in Items ?? [])
            item.Uncancel();
    }

    public Booking FlagBookingAsPartialRefund()
    {
        PartialRefund = true;

        return this;
    }

    public Booking SetStripeSessonId(string stripeSessionId)
    {
        StripeSessionId = stripeSessionId;
        return this;
    }
}