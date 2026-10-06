namespace Fuse.Contracts.Dashboard;

// The dashboard's tour-selling panels: reception's morning list (Sell a Tour Today) and how many hostel stays end up
// with a tour (Tour Attach Rate). Both from each hostel's Cloudbeds, so both need View Reports.

/// <summary>A guest on the Sell a Tour Today list: in house now, or checking in today, with no tour booked during their stay.</summary>
public sealed record SellATourRow
{
    public string Name { get; init; } = "";
    /// <summary>Their account here; null when the Cloudbeds import hasn't brought them in yet.</summary>
    [NotSortable] public string? GuestId { get; init; }
    public string Hostel { get; init; } = "";
    /// <summary>Checked in now (else checking in today).</summary>
    public bool InHouse { get; init; }
    public DateOnly CheckIn { get; init; }
    public DateOnly CheckOut { get; init; }
    /// <summary>Nights from today (or check-in, when later) to check-out.</summary>
    public int NightsLeft { get; init; }
}

/// <summary>
/// Today's arrivals and in-house guests with no tour booked during their stay (all hostels, or one); searched by name or
/// room. By default in house leaving soonest first (the last chance to sell them one), then today's arrivals.
/// </summary>
[Post("dashboard/sell-a-tour/search"), Permission(FuseAction.View, FuseResource.Reports)]
public sealed record SearchSellATourToday : PagedQuery, IQuery<PagedResult<SellATourRow>>
{
    /// <summary>Only this hostel's guests.</summary>
    public string? Hostel { get; init; }
}

/// <param name="ArrivingToday">Checking in today with no tour booked.</param>
/// <param name="InHouse">Checked in now with no tour booked.</param>
/// <param name="Hostels">Hostels connected to Cloudbeds (the tabs).</param>
/// <param name="Unreachable">Hostels whose Cloudbeds couldn't be asked just now (their guests are missing).</param>
public sealed record SellATourSummary(int ArrivingToday, int InHouse, IReadOnlyList<string> Hostels, IReadOnlyList<string> Unreachable);

/// <summary>The Sell a Tour Today counts (all hostels, or one) and the hostels; same snapshot as the table.</summary>
[Post("dashboard/sell-a-tour/summary"), Permission(FuseAction.View, FuseResource.Reports)]
public sealed record GetSellATourSummary(string? Hostel = null) : IQuery<SellATourSummary>;
