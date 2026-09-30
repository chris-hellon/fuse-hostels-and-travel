using Fuse.Application.Common.Time;
using Fuse.Application.Features.Bookings;
using Fuse.Domain.Catalog.Bookings;

namespace Fuse.Application.Features.Site;

/// <summary>
/// Website bookings waiting for payment. As legacy, one that's never paid stays Unpaid, holding its places, for staff to
/// delete; the only automatic removal is the visitor's own earlier attempt when they check out again (<see cref="ReplaceAsync"/>).
/// </summary>
internal sealed class UnpaidCheckouts(IAppDbContext db, TourCapacity capacity, IBusinessClock clock)
{
    /// <summary>Unpaid website bookings whose only checkout is the one they were made with (no payment link sent since).</summary>
    private IQueryable<Booking> Unpaid =>
        db.Bookings.Where(b => !b.IsPaid && b.Cancelled != true && b.Refunded != true && b.BookingSource == "Website"
                               && b.StripeSessionId != null && b.UpdateStripeSessionId == null && !b.Payments.Any());

    /// <summary>
    /// The visitor's earlier attempt, replaced by a new checkout: only an unpaid website booking from the last day for
    /// the same email, so a stray or guessed id can't touch anything else. (Legacy deleted it too, when the visitor came
    /// back to the checkout page.)
    /// </summary>
    public async Task ReplaceAsync(DefaultIdType bookingId, string email, CancellationToken cancellationToken)
    {
        var since = clock.Now.AddDays(-1);
        var address = email.Trim();
        if (await LoadAsync(Unpaid.Where(b => b.Id == bookingId && b.BookingDate >= since && b.GuestEmail == address), cancellationToken) is { } booking)
            await WithdrawAsync(booking, cancellationToken);
    }

    private static Task<Booking?> LoadAsync(IQueryable<Booking> bookings, CancellationToken cancellationToken) =>
        bookings.Include(b => b.Items).ThenInclude(i => i.Guests).FirstOrDefaultAsync(cancellationToken);

    private async Task WithdrawAsync(Booking booking, CancellationToken cancellationToken)
    {
        foreach (var item in booking.Items.Where(i => i.Cancelled != true))
            await capacity.ReleaseAsync(item.TourDateId, BookingStatuses.GuestCount(item), cancellationToken);
        db.Bookings.Remove(booking);
        await db.SaveChangesAsync(cancellationToken);
    }
}
