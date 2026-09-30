using System.Text.Json;
using Fuse.Contracts.TravelGuides;

namespace Fuse.Admin.Pages.TravelGuides;

/// <summary>The travel guide wizard's working copy; Save sends <see cref="ToChanges"/> in one command.</summary>
public sealed class TravelGuideDraft
{
    private string _original = "";

    private TravelGuideDraft(DefaultIdType? id, TravelGuideInput details)
    {
        Id = id;
        Details = details;
    }

    public static TravelGuideDraft New()
    {
        var draft = new TravelGuideDraft(null, new TravelGuideInput());
        draft._original = draft.Serialize();
        return draft;
    }

    public static TravelGuideDraft From(TravelGuideEditor editor)
    {
        var draft = new TravelGuideDraft(editor.Id, editor.Details) { Banner = editor.ImagePath };
        draft.Gallery.AddRange(editor.Gallery.Select(g => new GuideSlide(g.Id, g.ImagePath, null)));
        draft._original = draft.Serialize();
        return draft;
    }

    public DefaultIdType? Id { get; }
    public bool IsNew => Id is null;
    public TravelGuideInput Details { get; }

    /// <summary>The banner (saved, or uploaded this session and attached on Save).</summary>
    public string? Banner { get; set; }
    public List<GuideSlide> Gallery { get; } = [];

    public bool HasChanges => Serialize() != _original;

    public TravelGuideChanges ToChanges() => new()
    {
        Details = Details,
        Banner = Banner,
        Gallery = [.. Gallery.Select(g => new GalleryRef(g.Id, g.NewUrl))],
    };

    private string Serialize() => JsonSerializer.Serialize(ToChanges());
}

/// <summary>A slideshow image: saved (<see cref="Id"/>) or uploaded this session (<see cref="NewUrl"/>).</summary>
public sealed record GuideSlide(DefaultIdType? Id, string DisplayUrl, string? NewUrl);
