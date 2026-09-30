using System.Text.Json;
using Fuse.Contracts.Events;
using Fuse.Shared.Events;

namespace Fuse.Admin.Pages.Events;

/// <summary>The event wizard's working copy; Save sends it (image included) in one command.</summary>
public sealed class EventDraft
{
    private string _original = "";

    private EventDraft(DefaultIdType? id, EventInput details, string? imagePath)
    {
        Id = id;
        Details = details;
        ImagePath = imagePath;
    }

    /// <summary>A new event: tonight at 6pm, one-off.</summary>
    public static EventDraft New()
    {
        var draft = new EventDraft(null, new EventInput { StartDate = DateTime.Today.AddHours(18), PublishToSite = true }, null);
        draft._original = draft.Serialize();
        return draft;
    }

    public static EventDraft From(EventEditor editor)
    {
        var draft = new EventDraft(editor.Id, editor.Details, editor.ImagePath);
        draft._original = draft.Serialize();
        return draft;
    }

    public DefaultIdType? Id { get; }
    public bool IsNew => Id is null;
    public EventInput Details { get; }

    public string? ImagePath { get; set; }
    public FileUpload? NewImage { get; set; }
    public bool RemoveImage { get; set; }

    public bool Repeats => Details.RecurrenceType != EventRecurrenceType.None;

    public bool HasChanges => NewImage is not null || RemoveImage || Serialize() != _original;

    public bool IsCancelled(DateTime occurrence) => Details.CancelledDates.Any(d => d.Date == occurrence.Date);

    public void ToggleCancelled(DateTime occurrence)
    {
        if (Details.CancelledDates.RemoveAll(d => d.Date == occurrence.Date) == 0)
            Details.CancelledDates.Add(occurrence.Date);
    }

    public SaveEvent ToCommand() => new(Id, Details, NewImage, RemoveImage);

    private string Serialize() => JsonSerializer.Serialize(Details);
}
