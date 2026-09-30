using System.Text.Json;
using Fuse.Contracts.Properties;

namespace Fuse.Admin.Pages.Properties;

/// <summary>The property wizard's working copy; Save sends <see cref="ToChanges"/> in one command.</summary>
public sealed class PropertyDraft
{
    private string _original = "";

    private PropertyDraft(DefaultIdType? id, PropertyInput details, CloudbedsSettings cloudbeds)
    {
        Id = id;
        Details = details;
        Cloudbeds = cloudbeds;
    }

    public static PropertyDraft New()
    {
        var draft = new PropertyDraft(null, new PropertyInput(), new CloudbedsSettings(null, null, null));
        draft._original = draft.Serialize();
        return draft;
    }

    public static PropertyDraft From(PropertyEditor editor)
    {
        var draft = new PropertyDraft(editor.Id, editor.Details, editor.Cloudbeds) { Banner = editor.ImagePath, Video = editor.VideoPath };
        draft.Rooms.AddRange(editor.Rooms);
        draft.Directions.AddRange(editor.Directions);
        draft.Gallery.AddRange(editor.Gallery.Select(g => new PropertySlide(g.Id, g.ThumbnailImagePath, null)));
        draft._original = draft.Serialize();
        return draft;
    }

    public DefaultIdType? Id { get; }
    public bool IsNew => Id is null;
    public PropertyInput Details { get; }
    public CloudbedsSettings Cloudbeds { get; }
    public List<PropertyRoomEdit> Rooms { get; } = [];
    public List<PropertyDirectionEdit> Directions { get; } = [];

    /// <summary>The banner (saved, or uploaded this session and attached on Save).</summary>
    public string? Banner { get; set; }
    public string? Video { get; set; }
    public List<PropertySlide> Gallery { get; } = [];

    public bool HasChanges => Serialize() != _original;

    public PropertyChanges ToChanges() => new()
    {
        Details = Details,
        Rooms = [.. Rooms],
        Directions = [.. Directions],
        Banner = Banner,
        Video = Video,
        Gallery = [.. Gallery.Select(g => new GalleryRef(g.Id, g.NewUrl))],
    };

    private string Serialize() => JsonSerializer.Serialize(ToChanges());
}

/// <summary>A slideshow image: saved (<see cref="Id"/>) or uploaded this session (<see cref="NewUrl"/>).</summary>
public sealed record PropertySlide(DefaultIdType? Id, string DisplayUrl, string? NewUrl);
