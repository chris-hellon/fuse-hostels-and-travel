using System.Text.Json;
using Fuse.Contracts.Tours;

namespace Fuse.Admin.Pages.Tours;

/// <summary>
/// The tour wizard's working copy. Every step edits this; nothing reaches the server until Save,
/// which sends <see cref="ToChanges"/> as one command. Cancel just drops it.
/// </summary>
public sealed class TourDraft
{
    private readonly HashSet<DefaultIdType> _deletedDateIds = [];
    private readonly string? _originalBanner;
    private readonly string _originalDetails;
    private bool _changed;

    private TourDraft(DefaultIdType? id, TourInput details, int capacity, string? banner)
    {
        Id = id;
        Details = details;
        Capacity = capacity;
        _originalBanner = Banner = banner;
        _originalDetails = JsonSerializer.Serialize(details);
    }

    public static TourDraft New() => new(null, new TourInput { MinCapacity = 1, PublishToSite = false }, Tour.UnlimitedSpaces, null);

    public static TourDraft From(TourEditor editor)
    {
        var draft = new TourDraft(editor.Id, editor.Details, editor.Capacity, editor.ImagePath);
        draft.Prices.AddRange(editor.Prices.Select(p => new PriceDraft(p.Key, p.Details, IsNew: false)));
        draft.Dates.AddRange(editor.Dates.Select(d => new DateDraft
        {
            Id = d.Id,
            PriceKey = d.PriceKey,
            StartDate = d.StartDate,
            PriceOverride = d.PriceOverride,
            AvailableSpaces = d.AvailableSpaces,
            Booked = d.Booked,
            Original = d,
        }));
        draft.Gallery.AddRange(editor.Gallery.Select(g => new GalleryDraft(g.Id, g.ThumbnailImagePath, NewUrl: null)));
        return draft;
    }

    public DefaultIdType? Id { get; }
    public bool IsNew => Id is null;
    public TourInput Details { get; }
    /// <summary>Spaces a new departure starts with (the saved max capacity).</summary>
    public int Capacity { get; }

    public List<PriceDraft> Prices { get; } = [];
    /// <summary>Upcoming departures, including unsaved new ones; deleted ones are removed.</summary>
    public List<DateDraft> Dates { get; } = [];
    public int PendingDeletes => _deletedDateIds.Count;

    public string? Banner { get; private set; }
    public bool BannerChanged => Banner != _originalBanner;
    public List<GalleryDraft> Gallery { get; } = [];
    /// <summary>Anything to save (or lose on Cancel): a price, date or image change, or an edited field.</summary>
    public bool HasChanges => _changed || JsonSerializer.Serialize(Details) != _originalDetails;

    // Prices --------------------------------------------------------------------------------

    public PriceDraft? FindPrice(DefaultIdType? key) => key is null ? null : Prices.FirstOrDefault(p => p.Key == key);

    public void SavePrice(DefaultIdType? key, TourPriceInput details)
    {
        _changed = true;
        if (key is null)
        {
            Prices.Add(new PriceDraft(Guid.NewGuid(), details, IsNew: true));
            return;
        }
        var index = Prices.FindIndex(p => p.Key == key);
        Prices[index] = Prices[index] with { Details = details };
    }

    public IReadOnlyList<DateDraft> DatesFor(PriceDraft price) => [.. Dates.Where(d => d.PriceKey == price.Key)];

    /// <summary>Removes a price and its departures. Refused (returns false) when any of them has bookings.</summary>
    public bool RemovePrice(PriceDraft price)
    {
        var dates = DatesFor(price);
        if (dates.Any(d => d.Booked))
            return false;
        DeleteDates(dates);
        Prices.Remove(price);
        _changed = true;
        return true;
    }

    // Dates -----------------------------------------------------------------------------------

    public DateTime EndFor(DateDraft date) =>
        FindPrice(date.PriceKey) is { } price ? TourSchedule.EndFor(date.StartDate, price.Details) : date.Original?.EndDate ?? date.StartDate;

    /// <summary>Adds departures at each start for each price, skipping ones that already exist. Returns how many were added.</summary>
    public int AddDates(IEnumerable<DateTime> starts, IReadOnlyList<DefaultIdType> priceKeys, decimal? priceOverride)
    {
        var taken = Dates.Select(d => (d.PriceKey, d.StartDate)).ToHashSet();
        var added = 0;
        foreach (var start in starts)
        foreach (var key in priceKeys)
        {
            if (!taken.Add((key, start)))
                continue;
            Dates.Add(new DateDraft { PriceKey = key, StartDate = start, PriceOverride = priceOverride, AvailableSpaces = Capacity });
            added++;
        }
        if (added > 0)
        {
            Dates.Sort((a, b) => a.StartDate.CompareTo(b.StartDate));
            _changed = true;
        }
        return added;
    }

    public void UpdateDate(DateDraft date, DateTime start, DefaultIdType priceKey, decimal? priceOverride)
    {
        date.StartDate = start;
        date.PriceKey = priceKey;
        date.PriceOverride = priceOverride;
        Dates.Sort((a, b) => a.StartDate.CompareTo(b.StartDate));
        _changed = true;
    }

    /// <summary>Departures starting on a day from <paramref name="first"/> to <paramref name="last"/>, optionally at one time / on one price.</summary>
    public IReadOnlyList<DateDraft> DatesMatching(DateOnly first, DateOnly last, TimeOnly? startTime = null, DefaultIdType? priceKey = null) =>
    [
        .. Dates.Where(d => DateOnly.FromDateTime(d.StartDate) >= first && DateOnly.FromDateTime(d.StartDate) <= last
                            && (startTime is null || TimeOnly.FromDateTime(d.StartDate) == startTime)
                            && (priceKey is null || d.PriceKey == priceKey)),
    ];

    /// <summary>Removes the departures without bookings. Returns how many were kept because they're booked.</summary>
    public int DeleteDates(IEnumerable<DateDraft> dates)
    {
        var kept = 0;
        foreach (var date in dates.ToList())
        {
            if (date.Booked)
            {
                kept++;
                continue;
            }
            Dates.Remove(date);
            if (date.Id is { } id)
                _deletedDateIds.Add(id);
            _changed = true;
        }
        return kept;
    }

    // Images ----------------------------------------------------------------------------------

    public void SetBanner(string? url)
    {
        Banner = url;
        _changed = true;
    }

    public void AddGalleryImage(string url)
    {
        Gallery.Add(new GalleryDraft(null, url, url));
        _changed = true;
    }

    public void RemoveGalleryImage(GalleryDraft image)
    {
        Gallery.Remove(image);
        _changed = true;
    }

    public void MoveGalleryImage(int index, int by)
    {
        (Gallery[index], Gallery[index + by]) = (Gallery[index + by], Gallery[index]);
        _changed = true;
    }

    // Save ------------------------------------------------------------------------------------

    public TourChanges ToChanges() => new()
    {
        Details = Details,
        Prices = [.. Prices.Select(p => new TourPriceEdit { Key = p.Key, Details = p.Details })],
        AddedDates = [.. Dates.Where(d => d.Id is null).Select(d => d.ToEdit())],
        ChangedDates = [.. Dates.Where(d => d.IsChanged && d.PriceKey is not null).Select(d => d.ToEdit())],
        DeletedDateIds = [.. _deletedDateIds],
        NewBanner = BannerChanged ? Banner : null,
        RemoveBanner = BannerChanged && Banner is null,
        Gallery = [.. Gallery.Select(g => new TourGalleryRef(g.Id, g.NewUrl))],
    };

    private static class Tour
    {
        /// <summary>Matches the server's "no max capacity" seat count.</summary>
        public const int UnlimitedSpaces = 99999;
    }
}

public sealed record PriceDraft(DefaultIdType Key, TourPriceInput Details, bool IsNew)
{
    public string Label => $"${Details.Price:N2} {Details.Title}";
}

public sealed class DateDraft
{
    public DefaultIdType? Id { get; init; }
    public DefaultIdType? PriceKey { get; set; }
    public DateTime StartDate { get; set; }
    public decimal? PriceOverride { get; set; }
    public int AvailableSpaces { get; init; }
    public bool Booked { get; init; }
    /// <summary>As loaded; null for a new departure.</summary>
    public TourDateItem? Original { get; init; }

    public bool IsNew => Id is null;
    public bool IsChanged => Original is not null
                             && (StartDate != Original.StartDate || PriceKey != Original.PriceKey || PriceOverride != Original.PriceOverride);

    public TourDateEdit ToEdit() => new() { Id = Id, PriceKey = PriceKey!.Value, StartDate = StartDate, PriceOverride = PriceOverride };
}

/// <summary>A slideshow image: saved (<see cref="Id"/>) or uploaded this session (<see cref="NewUrl"/>, attached on Save).</summary>
public sealed record GalleryDraft(DefaultIdType? Id, string DisplayUrl, string? NewUrl);
