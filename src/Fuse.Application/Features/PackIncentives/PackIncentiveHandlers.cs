using Fuse.Contracts.PackIncentives;
using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Commission;

namespace Fuse.Application.Features.PackIncentives;

internal sealed class SearchPackIncentivesHandler(IAppDbContext db) : IQueryHandler<SearchPackIncentives, PagedResult<PackIncentiveSummary>>
{
    public ValueTask<PagedResult<PackIncentiveSummary>> Handle(SearchPackIncentives query, CancellationToken cancellationToken)
    {
        var incentives = db.PackIncentives.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            incentives = incentives.Where(p => p.Name.Contains(query.Keyword));

        return new(incentives
            .Select(p => new PackIncentiveSummary
            {
                Id = p.Id,
                Name = p.Name,
                MinimumProductCount = p.MinimumProductCount,
                CommissionAmount = p.CommissionAmount,
                Tours = db.PackIncentiveTours.Count(t => t.PackIncentiveId == p.Id),
                IsActive = p.IsActive,
            })
            .OrderBy(query.OrderBy, p => p.Name)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetPackIncentiveEditorHandler(IAppDbContext db) : IQueryHandler<GetPackIncentiveEditor, PackIncentiveEditor>
{
    public async ValueTask<PackIncentiveEditor> Handle(GetPackIncentiveEditor query, CancellationToken cancellationToken)
    {
        var incentive = await db.PackIncentives.AsNoTracking().FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken)
                        ?? throw new NotFoundException("Pack incentive not found.");
        var tours = await db.PackIncentiveTours.AsNoTracking().Where(t => t.PackIncentiveId == incentive.Id)
            .Select(t => new { t.TourId, Name = t.Tour != null ? t.Tour.Name : "(tour removed)" })
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
        var tiers = await db.PackIncentiveTiers.AsNoTracking().Where(t => t.PackIncentiveId == incentive.Id).OrderBy(t => t.SortOrder)
            .Select(t => new PackIncentiveTierEdit { Id = t.Id, Title = t.Title, Price = t.Price })
            .ToListAsync(cancellationToken);

        return new PackIncentiveEditor
        {
            Id = incentive.Id,
            Details = new PackIncentiveInput
            {
                Name = incentive.Name,
                MinimumProductCount = incentive.MinimumProductCount,
                CommissionAmount = incentive.CommissionAmount,
                IgnoresMinimumPax = incentive.IgnoresMinimumPax,
                IsActive = incentive.IsActive,
                Notes = incentive.Notes,
            },
            // Tour ids formatted in .NET so they match the options' ids exactly.
            Tours = [.. tours.Select(t => new LookupItem(t.TourId.ToString(), t.Name))],
            Tiers = tiers,
        };
    }
}

internal sealed class SavePackIncentiveHandler(IAppDbContext db) : ICommandHandler<SavePackIncentive, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SavePackIncentive command, CancellationToken cancellationToken)
    {
        var d = command.Details;
        PackIncentive incentive;
        if (command.Id is { } id)
        {
            incentive = await db.PackIncentives.Include(p => p.Tours).Include(p => p.Tiers).FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                        ?? throw new NotFoundException("Pack incentive not found.");
            incentive.Update(d.Name.Trim(), d.MinimumProductCount, d.CommissionAmount, d.IgnoresMinimumPax, d.IsActive, NullIfBlank(d.Notes));
        }
        else
        {
            incentive = new PackIncentive(d.Name.Trim(), d.MinimumProductCount, d.CommissionAmount, d.IgnoresMinimumPax, d.IsActive, NullIfBlank(d.Notes))
            {
                Tours = [],
                Tiers = [],
            };
            db.PackIncentives.Add(incentive);
        }

        var tourIds = command.TourIds.Distinct().ToList();
        if (await db.Tours.CountAsync(t => tourIds.Contains(t.Id), cancellationToken) != tourIds.Count)
            throw new BusinessRuleException("One of the chosen tours no longer exists. Remove it and try again.");

        // Eligible tours by tour: keep, add, remove.
        var tours = incentive.Tours ??= [];
        foreach (var removed in tours.Where(t => !tourIds.Contains(t.TourId)).ToList())
        {
            tours.Remove(removed);
            db.PackIncentiveTours.Remove(removed);
        }
        foreach (var tourId in tourIds.Where(id => tours.All(t => t.TourId != id)))
            tours.Add(new PackIncentiveTour(tourId, incentive.Id));

        // Tiers by id, in the given order.
        var tiers = incentive.Tiers ??= [];
        foreach (var removed in tiers.Where(t => command.Tiers.All(w => w.Id != t.Id)).ToList())
        {
            tiers.Remove(removed);
            db.PackIncentiveTiers.Remove(removed);
        }
        for (var i = 0; i < command.Tiers.Count; i++)
        {
            var edit = command.Tiers[i];
            var tier = edit.Id is { } tierId ? tiers.FirstOrDefault(t => t.Id == tierId) : null;
            if (tier is null)
                tiers.Add(new PackIncentiveTier(edit.Title.Trim(), i, edit.Price, incentive.Id));
            else
                tier.Update(edit.Title.Trim(), i, edit.Price);
        }

        await db.SaveChangesAsync(cancellationToken);
        return incentive.Id;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

internal sealed class DeletePackIncentiveHandler(IAppDbContext db) : ICommandHandler<DeletePackIncentive>
{
    public async ValueTask<Unit> Handle(DeletePackIncentive command, CancellationToken cancellationToken)
    {
        var incentive = await db.PackIncentives.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
                        ?? throw new NotFoundException("Pack incentive not found.");
        db.PackIncentives.Remove(incentive);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class ListPackIncentiveTourOptionsHandler(IAppDbContext db) : IQueryHandler<ListPackIncentiveTourOptions, IReadOnlyList<LookupItem>>
{
    public async ValueTask<IReadOnlyList<LookupItem>> Handle(ListPackIncentiveTourOptions query, CancellationToken cancellationToken)
    {
        var tours = await db.Tours.AsNoTracking().OrderBy(t => t.Name).Select(t => new { t.Id, t.Name }).ToListAsync(cancellationToken);
        return [.. tours.Select(t => new LookupItem(t.Id.ToString(), t.Name))];
    }
}
