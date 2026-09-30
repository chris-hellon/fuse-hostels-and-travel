using Fuse.Domain.Catalog.Tours;

namespace Fuse.Application.Features.Bookings;

/// <summary>
/// Spaces on departures. A departure and its siblings (same tour, start and end: one per price) share
/// capacity, so taking or freeing spaces moves them all. Entities are tracked, so several changes in one
/// request add up and are saved together.
/// </summary>
internal sealed class TourCapacity(IAppDbContext db)
{
    /// <summary>Books <paramref name="spaces"/> on a departure, or refuses if it hasn't that many left.</summary>
    public async Task<TourDate> TakeAsync(DefaultIdType tourDateId, int spaces, CancellationToken cancellationToken)
    {
        var group = await GroupAsync(tourDateId, cancellationToken)
            ?? throw new NotFoundException("That departure no longer exists. Choose another date.");
        var date = group.Single(d => d.Id == tourDateId);
        if (date.AvailableSpaces < spaces)
            throw new BusinessRuleException(
                $"The {date.StartDate:d MMM yyyy HH:mm} departure has {Math.Max(0, date.AvailableSpaces)} space(s) left, not {spaces}. Choose another date or fewer guests.");

        foreach (var d in group)
        {
            d.AvailableSpaces = Math.Max(0, d.AvailableSpaces - spaces);
            d.ConcurrencyVersion++;
        }
        return date;
    }

    /// <summary>Gives spaces back (a cancelled, removed or moved booking). Missing departures are ignored.</summary>
    public async Task ReleaseAsync(DefaultIdType? tourDateId, int spaces, CancellationToken cancellationToken)
    {
        if (tourDateId is not { } id || spaces <= 0)
            return;
        foreach (var d in await GroupAsync(id, cancellationToken) ?? [])
        {
            d.AvailableSpaces += spaces;
            d.ConcurrencyVersion++;
        }
    }

    private async Task<List<TourDate>?> GroupAsync(DefaultIdType tourDateId, CancellationToken cancellationToken)
    {
        var date = await db.TourDates.FirstOrDefaultAsync(d => d.Id == tourDateId, cancellationToken);
        if (date is null)
            return null;
        return await db.TourDates
            .Where(d => d.TourId == date.TourId && d.StartDate == date.StartDate && d.EndDate == date.EndDate)
            .ToListAsync(cancellationToken);
    }
}
