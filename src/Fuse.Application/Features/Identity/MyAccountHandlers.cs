using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Fuse.Application.Common.Jobs;
using Fuse.Application.Common.Mail;
using Fuse.Application.Common.Time;
using Fuse.Application.Features.Bookings;
using Fuse.Application.Features.Site;
using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Identity;
using Fuse.Domain.Catalog.Bookings;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Identity;

// The public site's My Account. A guest sees only the bookings they made (they're its lead guest), as legacy.

internal static class MyBookings
{
    public static string UserId(ICurrentUser currentUser) => currentUser.UserId?.ToString() ?? throw new UnauthorizedException();

    public static MyBookingStatus Status(Booking booking) => BookingStatuses.Of(booking) switch
    {
        BookingState.Cancelled => MyBookingStatus.Cancelled,
        BookingState.Refunded => MyBookingStatus.Refunded,
        // A website checkout is paid on Stripe's page or released; another page must not be opened for it.
        _ when !booking.IsPaid && booking.BookingSource == "Website" && booking.Payments.Count == 0 => MyBookingStatus.AwaitingPayment,
        _ => BookingStatuses.Outstanding(booking) > 0 ? MyBookingStatus.Due : MyBookingStatus.Paid,
    };

    /// <summary>The guest's bookings with their tours, guests and payments.</summary>
    public static IQueryable<Booking> Mine(IAppDbContext db, string userId) =>
        db.Bookings.AsNoTracking().AsSplitQuery()
            .Where(b => b.GuestId == userId && b.Items.Any())
            .Include(b => b.Items).ThenInclude(i => i.Tour)
            .Include(b => b.Items).ThenInclude(i => i.Guests)
            .Include(b => b.Payments);

    public static async Task<IReadOnlyList<MyBooking>> DetailsAsync(IAppDbContext db, IReadOnlyList<Booking> bookings, CancellationToken cancellationToken)
    {
        var guestIds = bookings.SelectMany(b => b.Items).SelectMany(i => i.Guests ?? []).Where(g => g.DeletedOn == null).Select(g => g.GuestId.ToString()).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => guestIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync(cancellationToken);
        var nameOf = names.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), StringComparer.OrdinalIgnoreCase);

        return [.. bookings.Select(b => new MyBooking
        {
            Id = b.Id,
            Reference = b.InvoiceId,
            BookedOn = b.BookingDate,
            Status = Status(b),
            Total = b.TotalAmount,
            Outstanding = Status(b) is MyBookingStatus.Cancelled or MyBookingStatus.Refunded ? 0 : BookingStatuses.Outstanding(b),
            Notes = b.AdditionalNotes,
            Tours = [.. b.Items.Where(i => i.TourId != null).OrderBy(i => i.StartDate).Select(i => new MyBookingTour
            {
                Name = i.Tour?.Name ?? "Tour",
                Slug = i.Tour is { PublishToSite: true } tour ? SiteCache.Slug(tour.UrlSlug, tour.Name) : null,
                ImagePath = i.Tour?.ImagePath,
                Start = i.StartDate,
                End = i.EndDate,
                Guests = BookingStatuses.GuestCount(i),
                GuestNames = [b.GuestName ?? "You", .. (i.Guests ?? []).Where(g => g.DeletedOn == null).Select(g => nameOf.GetValueOrDefault(g.GuestId.ToString()) ?? "Guest")],
                Pickup = i.PickupLocation,
                Price = i.Amount,
                Cancelled = i.Cancelled == true,
            })],
            // A payment with no method recorded is a Stripe one (the default, as the admin shows it).
            Payments = [.. b.Payments.Where(p => !p.Refunded).OrderBy(p => p.PaymentDate)
                .Select(p => new MyBookingPayment(p.PaymentDate ?? b.BookingDate, p.Amount, (p.PaymentMethod ?? BookingPaymentMethod.Stripe).ToString()))],
        })];
    }
}

internal sealed class ListMyBookingsHandler(IAppDbContext db, ICurrentUser currentUser, IBusinessClock clock) : IQueryHandler<ListMyBookings, IReadOnlyList<MyBookingSummary>>
{
    public async ValueTask<IReadOnlyList<MyBookingSummary>> Handle(ListMyBookings query, CancellationToken cancellationToken)
    {
        var bookings = await MyBookings.Mine(db, MyBookings.UserId(currentUser)).OrderByDescending(b => b.BookingDate).ToListAsync(cancellationToken);
        return [.. bookings.Select(b =>
        {
            var tours = b.Items.Where(i => i.TourId != null && i.Cancelled != true).OrderBy(i => i.StartDate).ToList();
            if (tours.Count == 0)
                tours = [.. b.Items.Where(i => i.TourId != null).OrderBy(i => i.StartDate)];
            var status = MyBookings.Status(b);
            return new MyBookingSummary
            {
                Id = b.Id,
                Reference = b.InvoiceId,
                BookedOn = b.BookingDate,
                Status = status,
                Total = b.TotalAmount,
                Outstanding = status is MyBookingStatus.Cancelled or MyBookingStatus.Refunded ? 0 : BookingStatuses.Outstanding(b),
                Title = tours.FirstOrDefault()?.Tour?.Name ?? b.Description,
                MoreTours = Math.Max(0, tours.Count - 1),
                ImagePath = tours.FirstOrDefault()?.Tour?.ImagePath,
                Starts = tours.FirstOrDefault()?.StartDate,
                Upcoming = status is not (MyBookingStatus.Cancelled or MyBookingStatus.Refunded) && tours.Any(i => i.StartDate >= clock.Today),
            };
        })];
    }
}

internal sealed class GetMyBookingHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMyBooking, MyBooking>
{
    public async ValueTask<MyBooking> Handle(GetMyBooking query, CancellationToken cancellationToken)
    {
        var booking = await MyBookings.Mine(db, MyBookings.UserId(currentUser)).FirstOrDefaultAsync(b => b.Id == query.Id, cancellationToken)
                      ?? throw new NotFoundException("This booking could not be found.");
        return (await MyBookings.DetailsAsync(db, [booking], cancellationToken))[0];
    }
}

internal sealed class PayMyBookingHandler(IAppDbContext db, ICurrentUser currentUser, BookingCheckouts checkouts) : ICommandHandler<PayMyBooking, string>
{
    public async ValueTask<string> Handle(PayMyBooking command, CancellationToken cancellationToken)
    {
        var userId = MyBookings.UserId(currentUser);
        var booking = await db.Bookings.Include(b => b.Items).ThenInclude(i => i.Guests).Include(b => b.Payments)
                          .FirstOrDefaultAsync(b => b.Id == command.Id && b.GuestId == userId, cancellationToken)
                      ?? throw new NotFoundException("This booking could not be found.");
        if (MyBookings.Status(booking) != MyBookingStatus.Due)
            throw new BusinessRuleException("There's nothing to pay on this booking.");
        var (session, _) = await checkouts.CreateAsync(booking, cancellationToken);
        return session.Url;
    }
}

internal sealed class RequestMyEmailChangeHandler(UserManager<ApplicationUser> users, ICurrentUser currentUser, IAccountEmails emails, IBackgroundJobs jobs, IOptions<SiteOptions> site)
    : ICommandHandler<RequestMyEmailChange>
{
    public async ValueTask<Unit> Handle(RequestMyEmailChange command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(MyBookings.UserId(currentUser)) ?? throw new NotFoundException("Your account could not be found.");
        var email = command.NewEmail.Trim();
        if (string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
            throw new RequestValidationException(nameof(RequestMyEmailChange.NewEmail), "That's already your email address.");
        if (await users.FindByEmailAsync(email) is not null)
            throw new RequestValidationException(nameof(RequestMyEmailChange.NewEmail), "Another account already uses that email address.");

        var token = await users.GenerateChangeEmailTokenAsync(user, email);
        var link = $"{site.Value.LinksUrl}/my-account/confirm-email?user={Uri.EscapeDataString(user.Id)}&email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        jobs.EnqueueEmail(await emails.ConfirmEmailChangeAsync(user.FirstName ?? "", email, link));
        return Unit.Value;
    }
}

internal sealed class ConfirmMyEmailChangeHandler(UserManager<ApplicationUser> users) : ICommandHandler<ConfirmMyEmailChange>
{
    private const string Expired = "This link has expired or has already been used. Ask for a new one from My Account.";

    public async ValueTask<Unit> Handle(ConfirmMyEmailChange command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(command.UserId) ?? throw new BusinessRuleException(Expired);
        if (string.Equals(user.Email, command.Email, StringComparison.OrdinalIgnoreCase))
            return Unit.Value; // already done (the link was followed twice)
        var changed = await users.ChangeEmailAsync(user, command.Email, command.Token);
        if (!changed.Succeeded)
            throw new BusinessRuleException(changed.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail))
                ? "Another account already uses that email address." : Expired);
        // Sign-in is by email; the user name follows it (as new accounts are made).
        (await users.SetUserNameAsync(user, command.Email)).ThrowIfFailed();
        return Unit.Value;
    }
}

internal sealed class SetMyPasswordHandler(UserManager<ApplicationUser> users, ICurrentUser currentUser) : ICommandHandler<SetMyPassword>
{
    public async ValueTask<Unit> Handle(SetMyPassword command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(MyBookings.UserId(currentUser)) ?? throw new NotFoundException("Your account could not be found.");
        if (await users.HasPasswordAsync(user))
            throw new BusinessRuleException("Your account already has a password. Change it instead.");
        (await users.AddPasswordAsync(user, command.NewPassword)).ThrowIfFailed(nameof(SetMyPassword.NewPassword));
        return Unit.Value;
    }
}

internal sealed class GetMyDataHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetMyData, MyData>
{
    public async ValueTask<MyData> Handle(GetMyData query, CancellationToken cancellationToken)
    {
        var userId = MyBookings.UserId(currentUser);
        var me = await db.Users.AsNoTracking().Where(u => u.Id == userId).ToDetails().FirstOrDefaultAsync(cancellationToken)
                 ?? throw new NotFoundException("Your account could not be found.");
        var bookings = await MyBookings.Mine(db, userId).OrderByDescending(b => b.BookingDate).ToListAsync(cancellationToken);
        return new MyData(me.Profile, me.SignUpDate, await MyBookings.DetailsAsync(db, bookings, cancellationToken));
    }
}
