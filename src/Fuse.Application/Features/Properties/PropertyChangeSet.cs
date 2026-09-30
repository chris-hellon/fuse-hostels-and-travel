using Fuse.Application.Common.Files;
using Fuse.Contracts.Properties;
using Fuse.Domain.Catalog.Pages;
using Fuse.Domain.Catalog.Properties;

namespace Fuse.Application.Features.Properties;

/// <summary>Applies everything the property wizard changed to a tracked property, for one SaveChanges.</summary>
/// <param name="previousName">The name before this save; null for a new property.</param>
internal sealed class PropertyChangeSet(IAppDbContext db, IFileStore files, Property property, string? previousName)
{
    private bool IsNew => previousName is null;

    /// <summary>
    /// Replaced or removed media files are left in storage, as legacy did: some are shared with (or hardcoded
    /// on) the site, and soft-deleted rows still point at them.
    /// </summary>
    public async Task ApplyAsync(PropertyChanges changes, CancellationToken cancellationToken)
    {
        await SyncLocationAsync(changes.Details.LocationId, cancellationToken);
        await ApplyRoomsAsync(changes.Rooms, cancellationToken);
        await ApplyDirectionsAsync(changes.Directions, cancellationToken);
        ApplyMedia(changes);
        await ApplyGalleryAsync(changes.Gallery, cancellationToken);
        await SyncPageAsync(cancellationToken);
    }

    private async Task SyncLocationAsync(DefaultIdType? locationId, CancellationToken cancellationToken)
    {
        var links = IsNew ? [] : await db.PropertyDestinationLookups.Where(l => l.PropertyId == property.Id).ToListAsync(cancellationToken);
        db.PropertyDestinationLookups.RemoveRange(links.Where(l => l.DestinationId != locationId));
        if (locationId is { } id && links.All(l => l.DestinationId != id))
            db.PropertyDestinationLookups.Add(new PropertyDestinationLookup(property.Id, id));
    }

    private async Task ApplyRoomsAsync(List<PropertyRoomEdit> wanted, CancellationToken cancellationToken)
    {
        var rooms = IsNew ? [] : await db.PropertyRooms.Where(r => r.PropertyId == property.Id).ToListAsync(cancellationToken);
        db.PropertyRooms.RemoveRange(rooms.Where(r => wanted.All(w => w.Id != r.Id)));

        foreach (var edit in wanted)
        {
            var room = edit.Id is { } id ? rooms.FirstOrDefault(r => r.Id == id) : null;
            if (room is null)
            {
                room = PropertyRoom.Create(property.Id, edit.Name, edit.ShortDescription, edit.Description);
                db.PropertyRooms.Add(room);
            }
            else
            {
                room.Apply(edit.Name, edit.ShortDescription, edit.Description);
            }

            var current = string.IsNullOrEmpty(room.ImagePath) ? null : room.ImagePath;
            if (Domain.Common.ImageUrls.Unsized(edit.ImageUrl) == Domain.Common.ImageUrls.Unsized(current))
                continue;
            if (edit.ImageUrl is { } url && !files.IsTenantFile(url, FileKind.Image))
                throw new RequestValidationException("Rooms", $"The image for {edit.Name} wasn't uploaded here. Choose it again.");
            room.SetImage(edit.ImageUrl);
        }
    }

    private async Task ApplyDirectionsAsync(List<PropertyDirectionEdit> wanted, CancellationToken cancellationToken)
    {
        var directions = IsNew ? [] : await db.PropertyDirections.Where(d => d.PropertyId == property.Id).ToListAsync(cancellationToken);
        var ids = directions.Select(d => d.Id).ToList();
        var contents = ids.Count == 0 ? [] : await db.PropertyDirectionContents.Where(c => ids.Contains(c.PropertyDirectionId)).ToListAsync(cancellationToken);

        foreach (var removed in directions.Where(d => wanted.All(w => w.Id != d.Id)))
        {
            db.PropertyDirectionContents.RemoveRange(contents.Where(c => c.PropertyDirectionId == removed.Id));
            db.PropertyDirections.Remove(removed);
        }

        foreach (var edit in wanted)
        {
            var direction = edit.Id is { } id ? directions.FirstOrDefault(d => d.Id == id) : null;
            if (direction is null)
            {
                direction = PropertyDirection.Create(property.Id, edit.Title);
                db.PropertyDirections.Add(direction);
                db.PropertyDirectionContents.Add(PropertyDirectionContent.Create(direction.Id, edit.Body));
                continue;
            }

            direction.Update(edit.Title.Trim());
            // One text per set of directions (legacy's editor only ever made one).
            var content = contents.Where(c => c.PropertyDirectionId == direction.Id).OrderBy(c => c.CreatedOn).FirstOrDefault();
            if (content is null)
                db.PropertyDirectionContents.Add(PropertyDirectionContent.Create(direction.Id, edit.Body));
            else
                content.Update(edit.Body, null);
        }
    }

    private void ApplyMedia(PropertyChanges changes)
    {
        var currentBanner = string.IsNullOrEmpty(property.ImagePath) ? null : property.ImagePath;
        if (Domain.Common.ImageUrls.Unsized(changes.Banner) != Domain.Common.ImageUrls.Unsized(currentBanner))
        {
            if (changes.Banner is { } banner && !files.IsTenantFile(banner, FileKind.Image))
                throw new RequestValidationException("Banner", "The banner image wasn't uploaded here. Choose it again.");
            property.SetBanner(changes.Banner);
        }

        var currentVideo = string.IsNullOrEmpty(property.VideoPath) ? null : property.VideoPath;
        if (changes.Video != currentVideo)
        {
            if (changes.Video is { } video && !files.IsTenantFile(video, FileKind.Video))
                throw new RequestValidationException("Video", "The video wasn't uploaded here. Choose it again.");
            property.SetVideo(changes.Video);
        }
    }

    private async Task ApplyGalleryAsync(List<GalleryRef> wanted, CancellationToken cancellationToken)
    {
        var existing = IsNew ? [] : await db.PropertyImages.Where(i => i.PropertyId == property.Id).ToListAsync(cancellationToken);
        var kept = new HashSet<DefaultIdType>();
        for (var index = 0; index < wanted.Count; index++)
        {
            var item = wanted[index];
            if (item.Id is { } id)
            {
                var image = existing.FirstOrDefault(i => i.Id == id)
                            ?? throw new BusinessRuleException("A slideshow image was removed by someone else. Reopen the property and try again.");
                image.SortOrder = index;
                kept.Add(id);
            }
            else if (item.Url is { } url && files.IsTenantFile(url, FileKind.Image))
            {
                db.PropertyImages.Add(PropertyImage.Create(property.Id, url, index));
            }
            else
            {
                throw new RequestValidationException("Gallery", "A slideshow image wasn't uploaded here. Add it again.");
            }
        }

        db.PropertyImages.RemoveRange(existing.Where(i => !kept.Contains(i.Id)));
    }

    private async Task SyncPageAsync(CancellationToken cancellationToken)
    {
        var page = previousName is null ? null
            : await db.Pages.FirstOrDefaultAsync(p => p.Title == PropertyMapping.PageTitle(previousName), cancellationToken);
        if (page is null)
        {
            db.Pages.Add(new Page(PropertyMapping.PageTitle(property.Name), property.MetaKeywords, property.MetaDescription, property.ImagePath, string.Empty,
                property.UrlSlug, property.H1, property.H2, null, property.SeoPageTitle));
        }
        else
        {
            page.Update(PropertyMapping.PageTitle(property.Name), property.MetaKeywords, property.MetaDescription, property.ImagePath, null,
                property.UrlSlug, property.H1, property.H2, null, property.SeoPageTitle);
        }
    }
}
