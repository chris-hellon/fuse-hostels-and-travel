using Microsoft.Extensions.Options;
using Fuse.Application.Common.Mail;
using Fuse.Application.Common.Payments;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Tours;
using Fuse.Contracts.Bookings;
using Fuse.Domain.Catalog.Bookings;

namespace Fuse.Application.Features.Bookings;

internal static class BookingLoader
{
    /// <summary>A booking with its tours, their guests and its payments, tracked for changes.</summary>
    public static async Task<Booking> LoadAsync(IAppDbContext db, SupplierScope scope, DefaultIdType id, CancellationToken cancellationToken)
    {
        var booking = await db.Bookings.AsSplitQuery()
            .Include(b => b.Items).ThenInclude(i => i.Guests)
            .Include(b => b.Payments)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException("Booking not found.");
        await BookingAccess.EnsureVisibleAsync(booking, scope, cancellationToken);
        return booking;
    }
}

internal sealed class AddBookingPaymentHandler(IAppDbContext db, SupplierScope scope, IBusinessClock clock, SupplierNotices suppliers)
    : ICommandHandler<AddBookingPayment>
{
    public async ValueTask<Unit> Handle(AddBookingPayment command, CancellationToken cancellationToken)
    {
        var booking = await BookingLoader.LoadAsync(db, scope, command.BookingId, cancellationToken);
        if (booking.Cancelled == true)
            throw new BusinessRuleException("This booking has been cancelled and can't take a payment.");
        if (booking.Refunded == true)
            throw new BusinessRuleException("This booking has been refunded and can't take a payment.");

        var outstanding = BookingStatuses.Outstanding(booking);
        if (outstanding <= 0)
            throw new BusinessRuleException("This booking has nothing outstanding.");
        if (command.Amount > outstanding)
            throw new BusinessRuleException($"The payment can't be more than the outstanding {outstanding:$#,##0.00}.");

        // StripeSessionId is NOT NULL in the database, so cash payments store an empty string (as legacy).
        var payment = new BookingPayment(booking.Id, command.Amount,
            command.Method == BookingPaymentMethod.Stripe ? command.StripePaymentId!.Trim() : "",
            refunded: false, booking.GuestName, booking.GuestEmail, booking.CurrencyCode, command.Method)
        {
            PaymentDate = clock.Now,
        };
        db.BookingPayments.Add(payment);
        var wasPaid = booking.IsPaid;
        booking.ApplyManualPayment(command.Amount).Touch();
        await db.SaveChangesAsync(cancellationToken);
        await suppliers.ConfirmIfNewlyPaidAsync(booking, wasPaid, cancellationToken);
        return Unit.Value;
    }
}

internal sealed class FlagBookingAsPaidHandler(IAppDbContext db, SupplierScope scope, ICurrentUser user, IOptions<BookingOptions> options,
    PaidBookingNotifications notifications, SupplierNotices suppliers)
    : ICommandHandler<FlagBookingAsPaid>
{
    public async ValueTask<Unit> Handle(FlagBookingAsPaid command, CancellationToken cancellationToken)
    {
        if (!BookingPolicy.For(user, options.Value).CanFlagAsPaid)
            throw new ForbiddenException("You can't mark bookings as paid.");

        var booking = await BookingLoader.LoadAsync(db, scope, command.Id, cancellationToken);
        if (booking.Cancelled == true || booking.Refunded == true)
            throw new BusinessRuleException("Cancelled and refunded bookings can't be marked as paid.");
        var wasPaid = booking.IsPaid;
        booking.FlagBookingAsPaid().Touch();
        await db.SaveChangesAsync(cancellationToken);
        if (!wasPaid)
            await notifications.EnqueueAsync(booking, cancellationToken);
        await suppliers.ConfirmIfNewlyPaidAsync(booking, wasPaid, cancellationToken);
        return Unit.Value;
    }
}

internal sealed class DeleteBookingHandler(IAppDbContext db, SupplierScope scope, TourCapacity capacity, ICurrentUser user, IOptions<BookingOptions> options)
    : ICommandHandler<DeleteBooking>
{
    public async ValueTask<Unit> Handle(DeleteBooking command, CancellationToken cancellationToken)
    {
        var booking = await BookingLoader.LoadAsync(db, scope, command.Id, cancellationToken);
        // As legacy: only unpaid bookings, or cancelled ones for the staff who can mark bookings paid.
        var allowed = !booking.IsPaid || (booking.Cancelled == true && BookingPolicy.For(user, options.Value).CanFlagAsPaid);
        if (!allowed)
            throw new BusinessRuleException("Paid bookings can't be deleted. Refund or cancel them instead.");

        foreach (var item in booking.Items.Where(i => i.Cancelled != true))
            await capacity.ReleaseAsync(item.TourDateId, BookingStatuses.GuestCount(item), cancellationToken);
        db.Bookings.Remove(booking); // soft delete
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class CancelBookingHandler(IAppDbContext db, SupplierScope scope, TourCapacity capacity, BookingRefunds refunds, SupplierNotices suppliers)
    : ICommandHandler<CancelBooking, string>
{
    public async ValueTask<string> Handle(CancelBooking command, CancellationToken cancellationToken)
    {
        var booking = await BookingLoader.LoadAsync(db, scope, command.Id, cancellationToken);
        if (booking.Cancelled == true)
            throw new BusinessRuleException("This booking is already cancelled.");

        var refunded = command.RefundAmount > 0
            // Legacy always treated a refund while cancelling as partial.
            ? await refunds.RefundBookingAsync(booking, command.RefundAmount, partial: true, cancellationToken)
            : null;

        var live = booking.Items.Where(i => i.Cancelled != true).ToList();
        foreach (var item in live)
            await capacity.ReleaseAsync(item.TourDateId, BookingStatuses.GuestCount(item), cancellationToken);
        booking.Cancel();
        booking.Touch();
        await db.SaveChangesAsync(cancellationToken);
        // Suppliers hear about the tours that were still on (any cancelled before were told then).
        await suppliers.SendAsync(booking, [.. live.Select(i => i.Id)], SupplierNotice.Cancelled, cancellationToken);
        return refunded is null ? "Booking cancelled." : $"Booking cancelled. {refunded}";
    }
}

internal sealed class UncancelBookingHandler(IAppDbContext db, SupplierScope scope, TourCapacity capacity, SupplierNotices suppliers)
    : ICommandHandler<UncancelBooking>
{
    public async ValueTask<Unit> Handle(UncancelBooking command, CancellationToken cancellationToken)
    {
        var booking = await BookingLoader.LoadAsync(db, scope, command.Id, cancellationToken);
        if (booking.Cancelled != true)
            throw new BusinessRuleException("This booking isn't cancelled.");
        if (booking.HasRefunds)
            throw new BusinessRuleException("This booking has been refunded, so it can't be un-cancelled.");

        // Every tour comes back (cancelling the booking cancelled them all), so each retakes its spaces.
        foreach (var item in booking.Items.Where(i => i.TourDateId is not null))
            await capacity.TakeAsync(item.TourDateId!.Value, BookingStatuses.GuestCount(item), cancellationToken);
        booking.Uncancel();
        booking.Touch();
        await db.SaveChangesAsync(cancellationToken);
        await suppliers.SendAsync(booking, [.. booking.Items.Select(i => i.Id)], SupplierNotice.Reinstated, cancellationToken);
        return Unit.Value;
    }
}

internal sealed class RefundBookingHandler(IAppDbContext db, SupplierScope scope, BookingRefunds refunds) : ICommandHandler<RefundBooking, string>
{
    public async ValueTask<string> Handle(RefundBooking command, CancellationToken cancellationToken)
    {
        var booking = await BookingLoader.LoadAsync(db, scope, command.Id, cancellationToken);
        if (booking.Cancelled == true)
            throw new BusinessRuleException("This booking is cancelled.");
        if (!booking.IsPaid)
            throw new BusinessRuleException("Only paid bookings can be refunded.");

        var message = await refunds.RefundBookingAsync(booking, command.Amount, partial: command.Amount < booking.TotalAmount, cancellationToken);
        booking.Touch();
        await db.SaveChangesAsync(cancellationToken);
        return message;
    }
}

internal sealed class CancelBookingItemHandler(IAppDbContext db, SupplierScope scope, TourCapacity capacity, BookingRefunds refunds, SupplierNotices suppliers)
    : ICommandHandler<CancelBookingItem, string>
{
    public async ValueTask<string> Handle(CancelBookingItem command, CancellationToken cancellationToken)
    {
        var (booking, item) = await BookingItems.LoadAsync(db, scope, command.ItemId, cancellationToken);
        if (item.Cancelled == true)
            throw new BusinessRuleException("This tour is already cancelled.");

        var refunded = command.RefundAmount > 0
            ? await refunds.RefundItemAsync(booking, item, command.RefundAmount, cancellationToken)
            : null;

        await capacity.ReleaseAsync(item.TourDateId, BookingStatuses.GuestCount(item), cancellationToken);
        item.Cancel();
        // The last tour cancelled cancels the booking.
        if (booking.Items.All(i => i.Cancelled == true))
            booking.Cancel();
        booking.Touch();
        await db.SaveChangesAsync(cancellationToken);
        await suppliers.SendAsync(booking, [item.Id], SupplierNotice.Cancelled, cancellationToken);
        return refunded is null ? "Tour cancelled." : $"Tour cancelled. {refunded}";
    }
}

internal sealed class RefundBookingItemHandler(IAppDbContext db, SupplierScope scope, BookingRefunds refunds) : ICommandHandler<RefundBookingItem, string>
{
    public async ValueTask<string> Handle(RefundBookingItem command, CancellationToken cancellationToken)
    {
        var (booking, item) = await BookingItems.LoadAsync(db, scope, command.ItemId, cancellationToken);
        if (item.Refunded == true)
            throw new BusinessRuleException("This tour has already been refunded.");
        if (!booking.IsPaid && booking.PartialRefund != true)
            throw new BusinessRuleException("Only paid bookings can be refunded.");

        var message = await refunds.RefundItemAsync(booking, item, command.Amount, cancellationToken);
        booking.Touch();
        await db.SaveChangesAsync(cancellationToken);
        return message;
    }
}

/// <summary>Legacy RefundBookingPaymentRequest (Reports › Payments): refunds one payment through its own Stripe checkout.</summary>
internal sealed class RefundBookingPaymentHandler(IAppDbContext db, SupplierScope scope, BookingRefunds refunds) : ICommandHandler<RefundBookingPayment, string>
{
    public async ValueTask<string> Handle(RefundBookingPayment command, CancellationToken cancellationToken)
    {
        var bookingId = await db.BookingPayments.Where(p => p.Id == command.PaymentId).Select(p => (DefaultIdType?)p.BookingId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Booking Payment not found.");
        var booking = await BookingLoader.LoadAsync(db, scope, bookingId, cancellationToken);
        var payment = booking.Payments.Single(p => p.Id == command.PaymentId);
        if (payment.Refunded)
            throw new BusinessRuleException("This payment has already been refunded.");
        if (booking.Cancelled == true)
            throw new BusinessRuleException("This booking is cancelled.");

        var message = await refunds.RefundPaymentAsync(booking, payment, command.Amount, cancellationToken);
        booking.Touch();
        await db.SaveChangesAsync(cancellationToken);
        return message;
    }
}

internal static class BookingItems
{
    public static async Task<(Booking Booking, BookingItem Item)> LoadAsync(IAppDbContext db, SupplierScope scope, DefaultIdType itemId, CancellationToken cancellationToken)
    {
        var bookingId = await db.BookingItems.Where(i => i.Id == itemId).Select(i => (DefaultIdType?)i.BookingId).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("That tour is no longer on the booking.");
        var booking = await BookingLoader.LoadAsync(db, scope, bookingId, cancellationToken);
        return (booking, booking.Items.Single(i => i.Id == itemId));
    }
}

/// <summary>
/// Refunds through Stripe (for bookings paid by Stripe checkout) and the flags that record them. Card
/// refunds happen first, so a refund that Stripe refuses records nothing. Callers <see cref="Booking.Touch"/> the booking.
/// </summary>
internal sealed class BookingRefunds(IPaymentGateway payments)
{
    public async Task<string> RefundBookingAsync(Booking booking, decimal amount, bool partial, CancellationToken cancellationToken)
    {
        if (amount > booking.TotalAmount)
            throw new BusinessRuleException($"The refund can't be more than the booking's {booking.TotalAmount:$#,##0.00}.");

        string message;
        if (string.IsNullOrEmpty(booking.StripeSessionId))
        {
            message = $"{amount:$#,##0.00} recorded as refunded. It wasn't paid by Stripe checkout, so give the money back by hand.";
        }
        else if (partial)
        {
            await RequireGateway();
            await payments.RefundAsync(booking.StripeSessionId, amount, cancellationToken);
            message = $"{amount:$#,##0.00} refunded through Stripe.";
        }
        else
        {
            // A full refund returns everything each checkout took (legacy: the booking's session and any
            // session paying for later changes).
            await RequireGateway();
            var total = 0m;
            foreach (var session in new[] { booking.StripeSessionId, booking.UpdateStripeSessionId }.Where(s => !string.IsNullOrEmpty(s)))
            {
                var charged = await payments.SessionTotalAsync(session!, cancellationToken);
                if (charged <= 0)
                    continue;
                await payments.RefundAsync(session!, charged, cancellationToken);
                total += charged;
            }
            message = $"{total:$#,##0.00} refunded through Stripe.";
        }

        if (partial)
            booking.FlagBookingAsPartialRefund();
        else
            booking.FlagBookingAsRefunded();
        return message;
    }

    public async Task<string> RefundItemAsync(Booking booking, BookingItem item, decimal amount, CancellationToken cancellationToken)
    {
        var itemTotal = item.Amount * BookingStatuses.GuestCount(item);
        if (amount > itemTotal)
            throw new BusinessRuleException($"The refund can't be more than the tour's {itemTotal:$#,##0.00}.");

        string message;
        if (string.IsNullOrEmpty(booking.StripeSessionId))
        {
            message = $"{amount:$#,##0.00} recorded as refunded. It wasn't paid by Stripe checkout, so give the money back by hand.";
        }
        else
        {
            await RequireGateway();
            await payments.RefundAsync(booking.StripeSessionId, amount, cancellationToken);
            message = $"{amount:$#,##0.00} refunded through Stripe.";
        }

        if (amount < itemTotal)
            item.FlagAsPartialRefund();
        else
            item.FlagAsRefunded();

        // As legacy: the booking is refunded once no other tour is left unrefunded, otherwise partly refunded.
        if (booking.Items.Where(i => i.Id != item.Id).All(i => i.Refunded == true))
            booking.FlagBookingAsRefunded();
        else
            booking.FlagBookingAsPartialRefund();
        return message;
    }

    /// <summary>
    /// As legacy: the payment's own checkout gives back everything it charged (the amount is the payment's) or part of it.
    /// The payment is flagged refunded or partly refunded; the booking partly refunded, or refunded once every other payment is.
    /// </summary>
    public async Task<string> RefundPaymentAsync(Booking booking, BookingPayment payment, decimal amount, CancellationToken cancellationToken)
    {
        if (amount > payment.Amount)
            throw new BusinessRuleException($"The refund can't be more than the payment's {payment.Amount:$#,##0.00}.");
        // As legacy, only a payment made through Stripe checkout can be refunded here (cash, or a payment recorded by hand, can't).
        if (string.IsNullOrEmpty(payment.StripeSessionId) || !payment.StripeSessionId.StartsWith("cs_", StringComparison.Ordinal))
            throw new BusinessRuleException("There is no Stripe checkout for this payment, so it can't be refunded here.");
        await RequireGateway();

        var partial = amount < payment.Amount;
        var refund = amount;
        if (!partial)
        {
            // As legacy: all of what the checkout charged.
            refund = await payments.SessionTotalAsync(payment.StripeSessionId, cancellationToken);
            if (refund <= 0)
                throw new BusinessRuleException("Stripe has no payment for this checkout, so there's nothing to refund.");
        }
        await payments.RefundAsync(payment.StripeSessionId, refund, cancellationToken);

        if (partial)
            booking.FlagBookingAsPartialRefund();
        else if (booking.Payments.Where(p => p.Id != payment.Id).All(p => p.Refunded))
            booking.FlagBookingAsRefunded();
        if (partial)
            payment.FlagAsPartialRefund();
        else
            payment.FlagAsRefund();
        return $"{refund:$#,##0.00} refunded through Stripe.";
    }

    private Task RequireGateway() => payments.IsConfigured
        ? Task.CompletedTask
        : throw new BusinessRuleException("Stripe isn't set up in this environment, so card payments can't be refunded here.");
}
