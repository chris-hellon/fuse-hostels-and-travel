using Fuse.Application.Common.Files;
using Fuse.Contracts.Packages;
using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Packages;

namespace Fuse.Application.Features.Packages;

internal sealed class SearchPackagesHandler(IAppDbContext db) : IQueryHandler<SearchPackages, PagedResult<PackageSummary>>
{
    public async ValueTask<PagedResult<PackageSummary>> Handle(SearchPackages query, CancellationToken cancellationToken)
    {
        var packages = db.Packages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            packages = packages.Where(p => p.Name.Contains(query.Keyword));

        var page = await packages
            .Select(p => new PackageSummary
            {
                Id = p.Id,
                Name = p.Name,
                CategoryName = p.TourCategory != null ? p.TourCategory.Name : null,
                PublishToSite = p.PublishToSite == true,
                Tours = db.PackageItems.Count(i => i.PackageId == p.Id),
            })
            .OrderBy(query.OrderBy, p => p.Name)
            .ToPagedResultAsync(query, cancellationToken);

        // Price range (lowest to highest tier total) for just this page's packages.
        var totals = await PackagePricing.TierTotalsAsync(db, [.. page.Items.Select(p => p.Id)], cancellationToken);
        return page with
        {
            Items = [.. page.Items.Select(p => totals.TryGetValue(p.Id, out var t) && t.Count > 0 ? p with { PriceLow = t.Min(), PriceHigh = t.Max() } : p)],
        };
    }
}

internal static class PackagePricing
{
    /// <summary>Each package's tier totals: per tier, the sum of its tours' tier price × quantity.</summary>
    public static async Task<Dictionary<DefaultIdType, List<decimal>>> TierTotalsAsync(IAppDbContext db, IReadOnlyCollection<DefaultIdType> packageIds,
        CancellationToken cancellationToken)
    {
        if (packageIds.Count == 0)
            return [];
        var tiers = await db.PackageTiers.Where(t => packageIds.Contains(t.PackageId)).Select(t => new { t.PackageId, t.Id }).ToListAsync(cancellationToken);
        var lines = await db.PackageItemPrices
            .Where(p => db.PackageItems.Any(i => i.Id == p.PackageItemId && packageIds.Contains(i.PackageId)))
            .Select(p => new
            {
                p.PackageTierId,
                Line = p.Price * db.PackageItems.Where(i => i.Id == p.PackageItemId).Select(i => i.Quantity).First(),
            })
            .ToListAsync(cancellationToken);
        var byTier = lines.GroupBy(l => l.PackageTierId).ToDictionary(g => g.Key, g => g.Sum(l => l.Line));
        return tiers.GroupBy(t => t.PackageId).ToDictionary(g => g.Key, g => g.Select(t => byTier.GetValueOrDefault(t.Id)).ToList());
    }
}

internal sealed class GetPackageEditorHandler(IAppDbContext db) : IQueryHandler<GetPackageEditor, PackageEditor>
{
    public async ValueTask<PackageEditor> Handle(GetPackageEditor query, CancellationToken cancellationToken)
    {
        var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken)
                      ?? throw new NotFoundException("Package not found.");
        var tiers = await db.PackageTiers.AsNoTracking().Where(t => t.PackageId == package.Id).OrderBy(t => t.SortOrder)
            .Select(t => new PackageTierEdit { Key = t.Id, Title = t.Title }).ToListAsync(cancellationToken);
        var items = await db.PackageItems.AsNoTracking().Where(i => i.PackageId == package.Id).OrderBy(i => i.SortOrder)
            .Select(i => new
            {
                i.Id, i.TourId, TourName = i.Tour != null ? i.Tour.Name : "(tour removed)", i.Quantity, i.MinimumPax, i.MinimumPaxScope,
                Prices = db.PackageItemPrices.Where(p => p.PackageItemId == i.Id).Select(p => new { p.PackageTierId, p.Price }).ToList(),
            })
            .ToListAsync(cancellationToken);

        return new PackageEditor
        {
            Id = package.Id,
            ImagePath = string.IsNullOrEmpty(package.ImagePath) ? null : package.ImagePath,
            Details = new PackageInput
            {
                Name = package.Name,
                TourCategoryId = package.TourCategoryId,
                PriceLabel = package.PriceLabel,
                CommissionAmount = package.CommissionAmount,
                CommissionIgnoresMinimumPax = package.CommissionIgnoresMinimumPax == true,
                Notes = package.Notes,
                ShortDescription = package.ShortDescription,
                Description = package.Description,
                PublishToSite = package.PublishToSite == true,
            },
            Tiers = tiers,
            Items = [.. items.Select(i => new PackageItemView
            {
                Id = i.Id,
                TourId = i.TourId,
                TourName = i.TourName,
                Quantity = i.Quantity,
                MinimumPax = i.MinimumPax,
                MinimumPaxScope = i.MinimumPaxScope is { } scope ? (CommissionScope)scope : null,
                Prices = i.Prices.GroupBy(p => p.PackageTierId).ToDictionary(g => g.Key, g => g.First().Price),
            })],
        };
    }
}

/// <summary>Name uniqueness (needs the database, so it isn't in the contract's validator).</summary>
internal sealed class SavePackageNameValidator : AbstractValidator<SavePackage>
{
    public SavePackageNameValidator(IAppDbContext db) =>
        RuleFor(x => x.Changes.Details.Name)
            .MustAsync(async (command, name, ct) => !await db.Packages.AnyAsync(p => p.Name == name.Trim() && p.Id != command.Id, ct))
            .WithName("Name").WithMessage("A package with this name already exists.");
}

internal sealed class SavePackageHandler(IAppDbContext db, IFileStore files) : ICommandHandler<SavePackage, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SavePackage command, CancellationToken cancellationToken)
    {
        var changes = command.Changes;
        var details = changes.Details.ToDomain();

        Package package;
        if (command.Id is { } id)
        {
            package = await db.Packages
                          .Include(p => p.PackageTiers)
                          .Include(p => p.PackageItems)!.ThenInclude(i => i.PackageItemPrices)
                          .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Package not found.");
            package.Apply(details);
        }
        else
        {
            package = Package.Create(details);
            package.PackageTiers = [];
            package.PackageItems = [];
            db.Packages.Add(package);
        }

        var tourIds = changes.Items.Select(i => i.TourId).Distinct().ToList();
        if (await db.Tours.CountAsync(t => tourIds.Contains(t.Id), cancellationToken) != tourIds.Count)
            throw new BusinessRuleException("One of the tours in this package no longer exists. Remove it and try again.");

        // Tours and prices first: a removed tier's prices must go before the tier itself.
        ReconcileItems(package, changes);
        ReconcileTiers(package, changes.Tiers);

        // The old file stays in storage, as legacy (it may be shared with the site).
        if (command.Image is not null || command.RemoveImage)
        {
            package.SetImage(command.Image is null ? null : await files.SaveAsync(command.Image, FileKind.Image, cancellationToken));
        }
        if (package.PublishToSite == true && string.IsNullOrEmpty(package.ImagePath))
            throw new RequestValidationException("Image", "A package on the website needs an image.");

        await db.SaveChangesAsync(cancellationToken);
        return package.Id;
    }

    /// <summary>Tiers in the given order. New tiers keep the editor's key as their id, so prices can refer to them.</summary>
    private void ReconcileTiers(Package package, List<PackageTierEdit> wanted)
    {
        var tiers = package.PackageTiers!;
        foreach (var removed in tiers.Where(t => wanted.All(w => w.Key != t.Id)).ToList())
        {
            tiers.Remove(removed);
            db.PackageTiers.Remove(removed);
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var tier = tiers.FirstOrDefault(t => t.Id == wanted[i].Key);
            if (tier is null)
            {
                // Added explicitly: the id is set (the editor's key), so found through the navigation
                // EF would take it for an existing row and try to update it.
                tier = new PackageTier(wanted[i].Key, wanted[i].Title.Trim(), i, package.Id);
                tiers.Add(tier);
                db.PackageTiers.Add(tier);
            }
            else
                tier.Update(wanted[i].Title.Trim(), i);
        }
    }

    /// <summary>Tours in the given order, each with exactly one price per tier (0 when not given).</summary>
    private void ReconcileItems(Package package, PackageChanges changes)
    {
        var items = package.PackageItems!;
        foreach (var removed in items.Where(i => changes.Items.All(w => w.Id != i.Id)).ToList())
        {
            db.PackageItemPrices.RemoveRange(removed.PackageItemPrices ?? []);
            items.Remove(removed);
            db.PackageItems.Remove(removed);
        }

        for (var index = 0; index < changes.Items.Count; index++)
        {
            var edit = changes.Items[index];
            var scope = edit.MinimumPax is null ? (int?)null : (int)(edit.MinimumPaxScope ?? CommissionScope.PerDeparture);
            var item = edit.Id is { } itemId ? items.FirstOrDefault(i => i.Id == itemId) : null;
            if (item is null)
            {
                item = new PackageItem(edit.TourId, index, edit.Quantity, null, package.Id, edit.MinimumPax, scope) { PackageItemPrices = [] };
                items.Add(item);
                db.PackageItems.Add(item);
            }
            else
            {
                item.Update(edit.TourId, index, edit.Quantity, item.TourPriceId, edit.MinimumPax, scope);
                item.PackageItemPrices ??= [];
            }

            var prices = item.PackageItemPrices!;
            foreach (var stale in prices.Where(p => changes.Tiers.All(t => t.Key != p.PackageTierId)).ToList())
            {
                prices.Remove(stale);
                db.PackageItemPrices.Remove(stale);
            }
            foreach (var tier in changes.Tiers)
            {
                var amount = edit.Prices.GetValueOrDefault(tier.Key);
                var price = prices.FirstOrDefault(p => p.PackageTierId == tier.Key);
                if (price is null)
                {
                    price = new PackageItemPrice(tier.Key, amount, item.Id);
                    prices.Add(price);
                    db.PackageItemPrices.Add(price);
                }
                else
                    price.Update(amount);
            }
        }
    }
}

internal sealed class DeletePackageHandler(IAppDbContext db) : ICommandHandler<DeletePackage>
{
    public async ValueTask<Unit> Handle(DeletePackage command, CancellationToken cancellationToken)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
                      ?? throw new NotFoundException("Package not found.");
        db.Packages.Remove(package);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class ListPackageTourOptionsHandler(IAppDbContext db) : IQueryHandler<ListPackageTourOptions, IReadOnlyList<PackageTourOption>>
{
    public async ValueTask<IReadOnlyList<PackageTourOption>> Handle(ListPackageTourOptions query, CancellationToken cancellationToken)
    {
        var tours = await db.Tours.AsNoTracking().OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken);
        var prices = (await db.TourPrices.AsNoTracking().Select(p => new { p.TourId, p.Price, p.PublishToWebsite }).ToListAsync(cancellationToken))
            .ToLookup(p => p.TourId);

        return [.. tours.Select(t =>
        {
            var all = prices[t.Id].ToList();
            var published = all.Where(p => p.PublishToWebsite == true).ToList();
            return new PackageTourOption
            {
                Id = t.Id,
                Name = t.Name,
                PriceLow = all.Count == 0 ? null : all.Min(p => p.Price),
                PriceHigh = all.Count == 0 ? null : all.Max(p => p.Price),
                DefaultPrice = (published.Count > 0 ? published : all) is { Count: > 0 } pool ? pool.Min(p => p.Price) : null,
            };
        })];
    }
}

internal static class PackageMapping
{
    public static PackageDetails ToDomain(this PackageInput input) => new()
    {
        Name = input.Name,
        TourCategoryId = input.TourCategoryId,
        PriceLabel = input.PriceLabel,
        CommissionAmount = input.CommissionAmount,
        CommissionIgnoresMinimumPax = input.CommissionIgnoresMinimumPax,
        Notes = input.Notes,
        ShortDescription = input.ShortDescription,
        Description = input.Description,
        PublishToSite = input.PublishToSite,
    };
}
