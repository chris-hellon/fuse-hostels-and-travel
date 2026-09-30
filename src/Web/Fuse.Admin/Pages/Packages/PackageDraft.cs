using System.Text.Json;
using Fuse.Contracts.Packages;
using Fuse.Contracts.Tours;

namespace Fuse.Admin.Pages.Packages;

/// <summary>The package wizard's working copy; Save sends <see cref="ToChanges"/> in one command.</summary>
public sealed class PackageDraft
{
    private string _original = "";

    private PackageDraft(DefaultIdType? id, PackageInput details, string? imagePath)
    {
        Id = id;
        Details = details;
        ImagePath = imagePath;
    }

    public static PackageDraft New()
    {
        var draft = new PackageDraft(null, new PackageInput(), null);
        draft.Tiers.Add(new TierDraft(Guid.NewGuid(), "Standard"));
        draft.Snapshot();
        return draft;
    }

    public static PackageDraft From(PackageEditor editor)
    {
        var draft = new PackageDraft(editor.Id, editor.Details, editor.ImagePath);
        draft.Tiers.AddRange(editor.Tiers.Select(t => new TierDraft(t.Key, t.Title)));
        if (draft.Tiers.Count == 0)
            draft.Tiers.Add(new TierDraft(Guid.NewGuid(), "Standard"));
        draft.Items.AddRange(editor.Items.Select(i => new ItemDraft
        {
            Id = i.Id, TourId = i.TourId, TourName = i.TourName, Quantity = i.Quantity,
            MinimumPax = i.MinimumPax, MinimumPaxScope = i.MinimumPaxScope, Prices = new(i.Prices),
        }));
        draft.EnsurePrices();
        draft.Snapshot();
        return draft;
    }

    public DefaultIdType? Id { get; }
    public bool IsNew => Id is null;
    public PackageInput Details { get; }
    public List<TierDraft> Tiers { get; } = [];
    public List<ItemDraft> Items { get; } = [];

    public string? ImagePath { get; set; }
    public FileUpload? NewImage { get; set; }
    public bool RemoveImage { get; set; }

    /// <summary>Starting package price per tour (its lowest published price), used for new tours and tiers.</summary>
    public Dictionary<DefaultIdType, decimal> DefaultPrices { get; } = [];

    public bool HasChanges => NewImage is not null || RemoveImage || Serialize() != _original;

    public decimal TotalFor(TierDraft tier) => Items.Sum(i => i.Prices.GetValueOrDefault(tier.Key) * i.Quantity);

    public void AddTours(IEnumerable<(DefaultIdType Id, string Name)> tours)
    {
        foreach (var (tourId, name) in tours.Where(t => Items.All(i => i.TourId != t.Id)))
        {
            var start = DefaultPrices.GetValueOrDefault(tourId);
            Items.Add(new ItemDraft { TourId = tourId, TourName = name, Prices = Tiers.ToDictionary(t => t.Key, _ => start) });
        }
    }

    public void Move(ItemDraft item, int by)
    {
        var index = Items.IndexOf(item);
        (Items[index], Items[index + by]) = (Items[index + by], Items[index]);
    }

    /// <summary>Replaces the tiers (from the tiers dialog); every tour keeps one price per tier.</summary>
    public void SetTiers(IEnumerable<TierDraft> tiers)
    {
        Tiers.Clear();
        Tiers.AddRange(tiers);
        EnsurePrices();
    }

    public void SetImage(FileUpload? image)
    {
        if (image is null && NewImage is not null)
        {
            NewImage = null; // discard the picked (unsaved) one
            return;
        }
        NewImage = image;
        if (image is null)
        {
            ImagePath = null;
            RemoveImage = true;
        }
    }

    public PackageChanges ToChanges() => new()
    {
        Details = Details,
        Tiers = [.. Tiers.Select(t => new PackageTierEdit { Key = t.Key, Title = t.Title })],
        Items = [.. Items.Select(i => new PackageItemEdit
        {
            Id = i.Id, TourId = i.TourId, Quantity = i.Quantity, MinimumPax = i.MinimumPax,
            MinimumPaxScope = i.MinimumPax is null ? null : i.MinimumPaxScope ?? CommissionScope.PerDeparture,
            Prices = Tiers.ToDictionary(t => t.Key, t => i.Prices.GetValueOrDefault(t.Key)),
        })],
    };

    private void EnsurePrices()
    {
        foreach (var item in Items)
        {
            foreach (var stale in item.Prices.Keys.Where(k => Tiers.All(t => t.Key != k)).ToList())
                item.Prices.Remove(stale);
            foreach (var tier in Tiers.Where(t => !item.Prices.ContainsKey(t.Key)))
                item.Prices[tier.Key] = DefaultPrices.GetValueOrDefault(item.TourId);
        }
    }

    private void Snapshot() => _original = Serialize();

    private string Serialize() => JsonSerializer.Serialize(ToChanges());
}

public sealed record TierDraft(DefaultIdType Key, string Title);

public sealed class ItemDraft
{
    public DefaultIdType? Id { get; init; }
    public DefaultIdType TourId { get; init; }
    public string TourName { get; init; } = "";
    public int Quantity { get; set; } = 1;
    public int? MinimumPax { get; set; }
    public CommissionScope? MinimumPaxScope { get; set; }
    public Dictionary<DefaultIdType, decimal> Prices { get; init; } = [];
}
