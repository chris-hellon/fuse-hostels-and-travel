using Microsoft.Extensions.Options;
using Fuse.Application.Common.Payments;
using Fuse.Application.Features.Site;
using Fuse.Contracts.Identity;

namespace Fuse.Application.Features.Identity;

internal sealed class ListMyTourQrCodesHandler(IAppDbContext db, ICurrentUser currentUser, IQrCodes qr, IOptions<SiteOptions> site)
    : IQueryHandler<ListMyTourQrCodes, IReadOnlyList<TourQrCode>>
{
    public async ValueTask<IReadOnlyList<TourQrCode>> Handle(ListMyTourQrCodes query, CancellationToken cancellationToken)
    {
        if (currentUser.IsInRole(FuseRoles.Supplier) || currentUser.UserId is not { } userId)
            return [];
        var tours = await db.Tours.AsNoTracking()
            .Where(t => t.PublishToSite == true || t.ShowBookingQRCode == true)
            .OrderBy(t => t.Name)
            .Select(t => new { t.Name, t.UrlSlug })
            .ToListAsync(cancellationToken);
        return [.. tours.Select(t =>
        {
            var url = $"{site.Value.LinksUrl}/tours/staff-booking/{SiteCache.Slug(t.UrlSlug, t.Name)}/{userId}";
            return new TourQrCode(t.Name, url, qr.PngDataUrl(url));
        })];
    }
}
