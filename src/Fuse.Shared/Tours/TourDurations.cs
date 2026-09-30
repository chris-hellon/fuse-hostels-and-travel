namespace Fuse.Shared.Tours;

/// <summary>Departure timing rules, shared by the server and the admin wizard (which previews unsaved dates).</summary>
public static class TourDurations
{
    /// <summary>
    /// When a departure starting at <paramref name="start"/> ends. Days win over nights (a night
    /// counts as half a day); hours are added on top. Same rules as legacy.
    /// </summary>
    public static DateTime EndFor(DateTime start, decimal? dayDuration, decimal? nightDuration, decimal? hourDuration)
    {
        var days = dayDuration.HasValue ? (double)dayDuration.Value
            : nightDuration.HasValue ? (double)nightDuration.Value / 2.0
            : 0;
        var wholeDays = Math.Floor(days);
        var end = start.AddDays(wholeDays).AddHours((days - wholeDays) * 24);
        return hourDuration.HasValue ? end.AddHours((double)hourDuration.Value) : end;
    }
}
