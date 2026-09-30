namespace Fuse.Contracts.Dashboard;

// Reports › In House, No Tours Booked: hostel guests staying now (or arriving today or tomorrow) with no tour booked
// during their stay, so reception knows who to talk to. From each hostel's Cloudbeds, kept for a couple of minutes so
// paging and sorting don't ask Cloudbeds again.

public sealed record GuestWithoutToursRow
{
    public string Name { get; init; } = "";
    /// <summary>Their account here; null when the Cloudbeds import hasn't brought them in yet.</summary>
    public string? GuestId { get; init; }
    public string Hostel { get; init; } = "";
    public DateOnly CheckIn { get; init; }
    public DateOnly CheckOut { get; init; }
    public string? Rooms { get; init; }
    [NotSortable] public string? ReservationUrl { get; init; }
}

/// <summary>In house (or, with <see cref="Arriving"/>, arriving today or tomorrow); by default leaving (arriving) soonest first.</summary>
[Post("dashboard/guests-without-tours/search"), Permission(FuseAction.View, FuseResource.Reports)]
public sealed record SearchGuestsWithoutTours : PagedQuery, IQuery<PagedResult<GuestWithoutToursRow>>
{
    public bool Arriving { get; init; }
    /// <summary>Only this hostel's guests.</summary>
    public string? Hostel { get; init; }
}

/// <param name="Hostels">Hostels connected to Cloudbeds (the filter).</param>
/// <param name="Unreachable">Hostels whose Cloudbeds couldn't be asked just now (their guests are missing).</param>
public sealed record GuestsWithoutToursHostels(IReadOnlyList<string> Hostels, IReadOnlyList<string> Unreachable);

[Get("dashboard/guests-without-tours/hostels"), Permission(FuseAction.View, FuseResource.Reports)]
public sealed record GetGuestsWithoutToursHostels : IQuery<GuestsWithoutToursHostels>;
