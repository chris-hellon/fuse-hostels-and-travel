using Fuse.Application.Common.Files;
using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Pages;
using Fuse.Domain.Catalog.Tours;

namespace Fuse.Application.Features.Tours;

/// <summary>
/// Applies everything the tour wizard changed (<see cref="TourChanges"/>) to a tracked tour, for
/// one SaveChanges. Business rules that need the whole picture live here: a departure with
/// bookings can't be deleted or moved, and a price can only go once none of its upcoming
/// departures remain.
/// </summary>
/// <param name="previousName">The tour's name before this save; null for a new tour.</param>
internal sealed class TourChangeSet(IAppDbContext db, IFileStore files, Tour tour, string? previousName)
{
    private bool IsNew => previousName is null;

    /// <summary>
    /// Replaced or removed media files are left in storage, as legacy did: some are shared with (or hardcoded
    /// on) the site, and soft-deleted rows still point at them.
    /// </summary>
    public async Task ApplyAsync(TourChanges changes, int capacityChange, CancellationToken cancellationToken)
    {
        await SyncLinksAsync(changes.Details, cancellationToken);
        var (prices, removedPrices) = await ApplyPricesAsync(changes.Prices, cancellationToken);
        await ApplyDatesAsync(changes, prices, removedPrices, capacityChange, cancellationToken);
        ApplyBanner(changes);
        await ApplyGalleryAsync(changes.Gallery, cancellationToken);
        await SyncPageAsync(changes.NewBanner, cancellationToken);
    }

    private async Task SyncLinksAsync(TourInput details, CancellationToken cancellationToken)
    {
        var locations = IsNew ? [] : await db.TourDestinationLookups.Where(l => l.TourId == tour.Id).ToListAsync(cancellationToken);
        var pickups = IsNew ? [] : await db.TourPickupLocations.Where(p => p.TourId == tour.Id).ToListAsync(cancellationToken);
        TourLinks.Sync(db, tour.Id, details, locations, pickups);
    }

    /// <summary>
    /// The CMS page carrying the tour's SEO, found by title (which follows the tour's name). As legacy: its image is
    /// the banner uploaded in this save ("" for a new tour without one; otherwise unchanged). Its custom scripts start
    /// as "" and are then kept (legacy reset them to "" on every save; the user decided they must survive).
    /// </summary>
    private async Task SyncPageAsync(string? newBanner, CancellationToken cancellationToken)
    {
        var page = previousName is null ? null
            : await db.Pages.FirstOrDefaultAsync(p => p.Title == TourMapping.PageTitle(previousName), cancellationToken);
        if (page is null)
        {
            db.Pages.Add(new Page(TourMapping.PageTitle(tour.Name), tour.MetaKeywords, tour.MetaDescription, IsNew ? newBanner ?? string.Empty : newBanner,
                string.Empty, tour.UrlSlug, tour.H1, tour.H2, null, tour.SeoPageTitle));
        }
        else
        {
            page.Update(TourMapping.PageTitle(tour.Name), tour.MetaKeywords, tour.MetaDescription, newBanner, null,
                tour.UrlSlug, tour.H1, tour.H2, null, tour.SeoPageTitle);
        }
    }

    /// <summary>Wanted prices by key (existing ones updated, new ones added) and the existing ones no longer wanted.</summary>
    private async Task<(Dictionary<DefaultIdType, TourPrice> ByKey, List<TourPrice> Removed)> ApplyPricesAsync(
        List<TourPriceEdit> edits, CancellationToken cancellationToken)
    {
        var existing = IsNew ? [] : await db.TourPrices.Where(p => p.TourId == tour.Id).ToListAsync(cancellationToken);
        var byKey = new Dictionary<DefaultIdType, TourPrice>();
        foreach (var edit in edits)
        {
            var price = existing.FirstOrDefault(p => p.Id == edit.Key);
            if (price is null)
            {
                price = TourPrice.Create(tour.Id, WithTourDuration(edit.Details.ToDomain()));
                db.TourPrices.Add(price);
            }
            else
            {
                price.Apply(WithTourDuration(edit.Details.ToDomain()));
            }
            byKey[edit.Key] = price;
        }
        return (byKey, [.. existing.Where(p => !byKey.ContainsKey(p.Id))]);
    }

    private async Task ApplyDatesAsync(TourChanges changes, Dictionary<DefaultIdType, TourPrice> prices, List<TourPrice> removedPrices,
        int capacityChange, CancellationToken cancellationToken)
    {
        var upcoming = IsNew ? [] : await db.TourDates.Where(d => d.TourId == tour.Id).Upcoming().ToListAsync(cancellationToken);
        var byId = upcoming.ToDictionary(d => d.Id);

        var touched = changes.DeletedDateIds.Concat(changes.ChangedDates.Select(d => d.Id!.Value)).ToList();
        if (touched.FirstOrDefault(id => !byId.ContainsKey(id)) is var missing && missing != Guid.Empty)
            throw new BusinessRuleException("A date you changed has already passed or no longer exists. Reopen the tour and try again.");
        var booked = await db.BookedAsync(touched, cancellationToken);

        var deleted = changes.DeletedDateIds.ToHashSet();
        if (deleted.Count(booked.Contains) is var bookedDeletes and > 0)
            throw new BusinessRuleException($"{bookedDeletes} of the dates you removed have bookings, so they can't be removed.");
        db.TourDates.RemoveRange(deleted.Select(id => byId[id]));

        foreach (var change in changes.ChangedDates)
        {
            var date = byId[change.Id!.Value];
            var price = prices[change.PriceKey];
            if (booked.Contains(date.Id) && (date.StartDate != change.StartDate || date.TourPriceId != price.Id))
                throw new BusinessRuleException($"{date.StartDate:dddd d MMMM yyyy} at {date.StartDate:HH:mm} has bookings, so only its price override can change.");
            date.Reschedule(change.StartDate, price, change.PriceOverride);
        }

        var remaining = upcoming.Where(d => !deleted.Contains(d.Id)).ToList();
        foreach (var date in remaining)
        {
            if (capacityChange != 0)
                date.AdjustCapacity(capacityChange);
            // A price's duration may have changed: every upcoming departure's end follows it.
            if (date.TourPriceId is { } priceId && prices.TryGetValue(priceId, out var price))
                date.EndDate = price.EndFor(date.StartDate);
        }

        if (removedPrices.FirstOrDefault(p => remaining.Any(d => d.TourPriceId == p.Id)) is { } stillUsed)
            throw new BusinessRuleException($"{stillUsed.Title} still has upcoming dates. Remove those dates too, or keep the price.");
        db.TourPrices.RemoveRange(removedPrices);

        var taken = remaining.Select(d => (d.TourPriceId, d.StartDate)).ToHashSet();
        foreach (var added in changes.AddedDates)
        {
            var price = prices[added.PriceKey];
            if (taken.Add((price.Id, added.StartDate)))
                db.TourDates.Add(TourDate.Create(tour.Id, price, added.StartDate, tour.Capacity, added.PriceOverride));
        }
    }

    private void ApplyBanner(TourChanges changes)
    {
        if (changes.NewBanner is null && !changes.RemoveBanner)
            return;
        if (changes.NewBanner is { } url && !files.IsTenantFile(url, FileKind.Image))
            throw new RequestValidationException(nameof(changes.NewBanner), "The banner image wasn't uploaded here. Choose it again.");

        tour.SetBanner(changes.NewBanner);
    }

    private async Task ApplyGalleryAsync(List<TourGalleryRef> wanted, CancellationToken cancellationToken)
    {
        var existing = IsNew ? [] : await db.TourImages.Where(i => i.TourId == tour.Id).ToListAsync(cancellationToken);
        var kept = new HashSet<DefaultIdType>();
        for (var index = 0; index < wanted.Count; index++)
        {
            var item = wanted[index];
            if (item.Id is { } id)
            {
                var image = existing.FirstOrDefault(i => i.Id == id)
                            ?? throw new BusinessRuleException("A slideshow image was removed by someone else. Reopen the tour and try again.");
                image.SortOrder = index;
                kept.Add(id);
            }
            else if (item.Url is { } url && files.IsTenantFile(url, FileKind.Image))
            {
                db.TourImages.Add(TourImage.Create(tour.Id, url, index));
            }
            else
            {
                throw new RequestValidationException("Gallery", "A slideshow image wasn't uploaded here. Add it again.");
            }
        }

        db.TourImages.RemoveRange(existing.Where(i => !kept.Contains(i.Id)));
    }

    /// <summary>
    /// A price with no duration of its own (blank, or legacy's 0) takes the tour's, where that's a plain number (not a
    /// range like "1-1.5"): departures on the price end by it.
    /// </summary>
    private TourPriceDetails WithTourDuration(TourPriceDetails price)
    {
        if (price.DayDuration > 0 || price.NightDuration > 0 || price.HourDuration > 0)
            return price;
        static decimal? Number(string? text) =>
            decimal.TryParse(text?.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
        return price with { DayDuration = Number(tour.DayDuration), NightDuration = Number(tour.NightDuration), HourDuration = Number(tour.HourDuration) };
    }
}
