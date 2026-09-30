using Fuse.Application.Common.Files;
using Fuse.Contracts.Events;
using Fuse.Contracts.Tours;
using Fuse.Domain.Catalog.Events;
using Fuse.Shared.Events;

namespace Fuse.Application.Features.Events;

// Events repeat by rule (daily / weekly on chosen days / monthly); dates are expanded on the fly by
// EventRecurrence, never stored. Cancelled dates, times of day and the properties an event is at are
// owned rows, reconciled on every save. Event.PropertyId (one property) is legacy and no longer written.

internal sealed class SearchEventsHandler(IAppDbContext db) : IQueryHandler<SearchEvents, PagedResult<EventSummary>>
{
    public ValueTask<PagedResult<EventSummary>> Handle(SearchEvents query, CancellationToken cancellationToken)
    {
        var events = db.Events.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            events = events.Where(e => e.Name != null && e.Name.Contains(query.Keyword));
        if (query.EventCategoryId is { } categoryId)
            events = events.Where(e => e.EventCategoryId == categoryId);

        return new(events
            .Select(e => new EventSummary
            {
                Id = e.Id,
                Name = e.Name ?? "",
                CategoryName = e.EventCategory == null ? null : e.EventCategory.Name,
                CategoryColour = e.EventCategory == null ? null : e.EventCategory.Color,
                BackgroundColour = e.BackgroundColor,
                StartDate = e.StartDate,
                AllDay = e.AllDay,
                RecurrenceType = e.RecurrenceType,
                RecurrenceInterval = e.RecurrenceInterval,
                RecurrenceDaysOfWeek = e.RecurrenceDaysOfWeek,
                RecurrenceEndDate = e.RecurrenceEndDate,
                PublishToSite = e.PublishToSite == true,
                IsFeatured = e.IsFeatured == true,
                FollowsTourDates = e.FollowsTourDates,
            })
            .OrderBy(query.OrderBy, e => e.Name)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetEventEditorHandler(IAppDbContext db) : IQueryHandler<GetEventEditor, EventEditor>
{
    public async ValueTask<EventEditor> Handle(GetEventEditor query, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
                ?? throw new NotFoundException("Event not found.");
        // Live properties only: deleting a property is a soft delete that leaves its event links behind.
        var propertyIds = await db.EventPropertyLookups.Where(l => l.EventId == e.Id && db.Properties.Any(p => p.Id == l.PropertyId))
            .Select(l => l.PropertyId).ToListAsync(cancellationToken);
        var cancelled = await db.EventOccurrenceExceptions.Where(x => x.EventId == e.Id).OrderBy(x => x.OccurrenceDate)
            .Select(x => x.OccurrenceDate).ToListAsync(cancellationToken);
        var slots = await db.EventTimeSlots.Where(x => x.EventId == e.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.StartTime)
            .Select(x => new EventTimeSlotInput { StartTime = x.StartTime, EndTime = x.EndTime }).ToListAsync(cancellationToken);
        // A category that has since been deleted reads as none.
        var categoryId = e.EventCategoryId is { } id && await db.EventCategories.AnyAsync(c => c.Id == id, cancellationToken) ? id : (DefaultIdType?)null;

        return new EventEditor(e.Id, new EventInput
        {
            Name = e.Name ?? "",
            EventCategoryId = categoryId,
            Colour = EventColours.ToHex(e.BackgroundColor),
            PublishToSite = e.PublishToSite == true,
            IsFeatured = e.IsFeatured == true,
            ExternalUrl = e.ExternalUrl,
            TourId = e.TourId,
            FollowsTourDates = e.FollowsTourDates,
            ShortDescription = e.ShortDescription,
            Description = e.Description,
            StartDate = e.StartDate,
            EndDate = e.EndDate,
            AllDay = e.AllDay,
            RecurrenceType = e.RecurrenceType,
            RecurrenceInterval = e.RecurrenceInterval,
            RecurrenceDaysOfWeek = e.RecurrenceDaysOfWeek,
            RecurrenceEndDate = e.RecurrenceEndDate,
            CancelledDates = cancelled,
            TimeSlots = slots,
            AllProperties = e.AllProperties == true,
            PropertyIds = propertyIds,
        }, string.IsNullOrWhiteSpace(Domain.Common.ImageUrls.Unsized(e.ImagePath)) ? null : e.ImagePath);
    }
}

internal sealed class SaveEventReferencesValidator : AbstractValidator<SaveEvent>
{
    public SaveEventReferencesValidator(IAppDbContext db)
    {
        RuleFor(x => x.Details.EventCategoryId)
            .MustAsync(async (id, ct) => await db.EventCategories.AnyAsync(c => c.Id == id, ct))
            .When(x => x.Details.EventCategoryId.HasValue)
            .WithName("Category").WithMessage("That category no longer exists.");
        RuleFor(x => x.Details.PropertyIds)
            .MustAsync(async (ids, ct) =>
            {
                var distinct = ids.Distinct().ToList();
                return await db.Properties.CountAsync(p => distinct.Contains(p.Id), ct) == distinct.Count;
            })
            .When(x => !x.Details.AllProperties && x.Details.PropertyIds.Count > 0)
            .WithName("Properties").WithMessage("One of the chosen properties no longer exists.");
    }
}

internal sealed class SaveEventHandler(IAppDbContext db, IFileStore files) : ICommandHandler<SaveEvent, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SaveEvent command, CancellationToken cancellationToken)
    {
        var input = command.Details;
        Event @event;
        if (command.Id is { } id)
        {
            @event = await db.Events.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                     ?? throw new NotFoundException("Event not found.");
            @event.Apply(input.ToDomain());
        }
        else
        {
            @event = Event.Create(input.ToDomain());
            db.Events.Add(@event);
        }

        await SyncPropertiesAsync(@event.Id, input, cancellationToken);
        await SyncCancelledDatesAsync(@event, input, cancellationToken);
        await SyncTimeSlotsAsync(@event, input, cancellationToken);

        // The old file stays in storage, as legacy (it may be shared with the site).
        var saved = command.Image is null ? null : await files.SaveAsync(command.Image, FileKind.Image, cancellationToken);
        if (saved is not null || command.RemoveImage)
        {
            @event.SetImage(saved);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await files.DeleteAsync(saved, CancellationToken.None);
            throw;
        }

        return @event.Id;
    }

    private async Task SyncPropertiesAsync(DefaultIdType eventId, EventInput input, CancellationToken cancellationToken)
    {
        var wanted = input.AllProperties ? [] : input.PropertyIds.Distinct().ToHashSet();
        var current = await db.EventPropertyLookups.Where(l => l.EventId == eventId).ToListAsync(cancellationToken);

        db.EventPropertyLookups.RemoveRange(current.Where(l => !wanted.Contains(l.PropertyId)));
        var have = current.Select(l => l.PropertyId).ToHashSet();
        db.EventPropertyLookups.AddRange(wanted.Where(p => !have.Contains(p)).Select(p => new EventPropertyLookup(eventId, p)));
    }

    /// <summary>Only a repeating event has dates to cancel; a one-off keeps none.</summary>
    private async Task SyncCancelledDatesAsync(Event @event, EventInput input, CancellationToken cancellationToken)
    {
        // Days off only mean something for an event on more than one date (a repeating one, or one on the tour's departures).
        var wanted = @event.RecurrenceType == EventRecurrenceType.None && !@event.FollowsTourDates ? [] : input.CancelledDates.Select(d => d.Date).ToHashSet();
        var current = await db.EventOccurrenceExceptions.Where(x => x.EventId == @event.Id).ToListAsync(cancellationToken);

        db.EventOccurrenceExceptions.RemoveRange(current.Where(x => !wanted.Contains(x.OccurrenceDate.Date)));
        var have = current.Select(x => x.OccurrenceDate.Date).ToHashSet();
        db.EventOccurrenceExceptions.AddRange(wanted.Where(d => !have.Contains(d)).Select(d => new EventOccurrenceException(@event.Id, d)));
    }

    /// <summary>Replaced on every save, in time order (as legacy). An all-day event has none.</summary>
    private async Task SyncTimeSlotsAsync(Event @event, EventInput input, CancellationToken cancellationToken)
    {
        db.EventTimeSlots.RemoveRange(await db.EventTimeSlots.Where(x => x.EventId == @event.Id).ToListAsync(cancellationToken));
        if (@event.AllDay)
            return;
        db.EventTimeSlots.AddRange(input.TimeSlots.OrderBy(t => t.StartTime)
            .Select((t, i) => new EventTimeSlot(@event.Id, t.StartTime, t.EndTime, i)));
    }
}

internal sealed class DeleteEventHandler(IAppDbContext db) : ICommandHandler<DeleteEvent>
{
    public async ValueTask<Unit> Handle(DeleteEvent command, CancellationToken cancellationToken)
    {
        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
                     ?? throw new NotFoundException("Event not found.");
        db.Events.Remove(@event);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class ListEventOccurrencesHandler(IAppDbContext db) : IQueryHandler<ListEventOccurrences, IReadOnlyList<EventOccurrence>>
{
    public async ValueTask<IReadOnlyList<EventOccurrence>> Handle(ListEventOccurrences query, CancellationToken cancellationToken) =>
        [.. (await EventOccurrences.ExpandAsync(db, db.Events, query.From, query.To, cancellationToken))
            .Select(o => new EventOccurrence(o.EventId, o.Name, o.Start, o.End, o.AllDay, o.Colour, o.CategoryName))];
}

internal sealed class ListEventPropertiesHandler(IAppDbContext db) : IQueryHandler<ListEventProperties, IReadOnlyList<LookupItem>>
{
    public async ValueTask<IReadOnlyList<LookupItem>> Handle(ListEventProperties query, CancellationToken cancellationToken) =>
        // Ids formatted here, not in SQL (which would upper-case them).
        [.. (await db.Properties.AsNoTracking().OrderBy(p => p.Name).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken))
            .Select(p => new LookupItem(p.Id.ToString(), p.Name))];
}

internal sealed class ListEventToursHandler(IAppDbContext db) : IQueryHandler<ListEventTours, IReadOnlyList<LookupItem>>
{
    public async ValueTask<IReadOnlyList<LookupItem>> Handle(ListEventTours query, CancellationToken cancellationToken) =>
        [.. (await db.Tours.AsNoTracking().OrderBy(t => t.Name).Select(t => new { t.Id, t.Name, t.PublishToSite }).ToListAsync(cancellationToken))
            .Select(t => new LookupItem(t.Id.ToString(), t.PublishToSite == true ? t.Name : $"{t.Name} (not on website)"))];
}

internal sealed class ListEventTourDeparturesHandler(IAppDbContext db, Common.Time.IBusinessClock clock)
    : IQueryHandler<ListEventTourDepartures, IReadOnlyList<EventTourDeparture>>
{
    /// <summary>As the schedule preview: about two months.</summary>
    private const int Days = 62;

    public async ValueTask<IReadOnlyList<EventTourDeparture>> Handle(ListEventTourDepartures query, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        return await db.TourDates.AsNoTracking()
            .Where(d => d.TourId == query.TourId && d.StartDate >= today && d.StartDate < today.AddDays(Days))
            .OrderBy(d => d.StartDate)
            .Select(d => new EventTourDeparture(d.StartDate, d.EndDate, d.AvailableSpaces, d.TourPrice.PublishToWebsite == true && d.TourPrice.InHouseOnly != true))
            .ToListAsync(cancellationToken);
    }
}

internal static class EventMapping
{
    public static EventDetails ToDomain(this EventInput input) => new()
    {
        Name = input.Name,
        EventCategoryId = input.EventCategoryId,
        Colour = input.Colour,
        PublishToSite = input.PublishToSite,
        IsFeatured = input.IsFeatured,
        ExternalUrl = input.ExternalUrl,
        TourId = input.TourId,
        FollowsTourDates = input.FollowsTourDates,
        ShortDescription = input.ShortDescription,
        Description = input.Description,
        StartDate = input.StartDate,
        EndDate = input.EndDate,
        AllDay = input.AllDay,
        AllProperties = input.AllProperties,
        RecurrenceType = input.RecurrenceType,
        RecurrenceInterval = input.RecurrenceInterval,
        RecurrenceDaysOfWeek = input.RecurrenceDaysOfWeek,
        RecurrenceEndDate = input.RecurrenceEndDate,
    };
}
