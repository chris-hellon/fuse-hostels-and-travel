using Fuse.Contracts.Dashboard;
using Fuse.Domain.Catalog.Bookings;

namespace Fuse.Application.Features.Bookings;

internal static class BookingStatuses
{
    /// <summary>A booking's state as the bookings table shows it (legacy GetStatusLabel).</summary>
    public static BookingState Of(Booking b) => Of(b.Cancelled, b.Refunded, b.PartialRefund, b.AmountOutstanding, b.IsPaid);

    /// <summary><see cref="Of(Booking)"/> from its columns (when only they are read).</summary>
    public static BookingState Of(bool? cancelled, bool? refunded, bool? partialRefund, decimal? amountOutstanding, bool isPaid) =>
        cancelled == true ? BookingState.Cancelled
        : refunded == true ? BookingState.Refunded
        : partialRefund == true ? BookingState.PartialRefund
        : amountOutstanding > 0 ? BookingState.PartiallyPaid
        : isPaid ? BookingState.Paid
        : BookingState.Unpaid;

    /// <summary>A booked tour's (or one guest's) state: its own flags first, then the booking's payment.</summary>
    public static BookingState Of(bool? cancelled, bool? refunded, bool? partialRefund, bool? noShow, bool? checkedIn, Booking booking)
    {
        if (cancelled == true) return BookingState.Cancelled;
        if (refunded == true) return BookingState.Refunded;
        if (partialRefund == true) return BookingState.PartialRefund;
        if (noShow == true) return BookingState.NoShow;
        if (checkedIn == true) return BookingState.CheckedIn;
        var state = Of(booking);
        return state is BookingState.Cancelled or BookingState.Refunded ? state : booking.IsPaid ? BookingState.Paid : BookingState.Unpaid;
    }

    /// <summary>What the guest still owes: the recorded balance, else the whole total until paid.</summary>
    public static decimal Outstanding(Booking b) => Outstanding(b.AmountOutstanding, b.IsPaid, b.TotalAmount);

    public static decimal Outstanding(decimal? amountOutstanding, bool isPaid, decimal total) =>
        amountOutstanding > 0 ? amountOutstanding.Value : isPaid ? 0m : total;

    /// <summary>The lead guest plus everyone added to the tour.</summary>
    public static int GuestCount(BookingItem item) => (item.Guests?.Count(g => g.DeletedOn == null) ?? 0) + 1;
}
