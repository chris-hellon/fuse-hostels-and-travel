namespace Fuse.Application.Common.Time;

/// <summary>
/// "Now" and "today" where the business is (Fuse: Vietnam). Tour dates and booking dates are stored
/// in local time, so anything that means "today" (dashboard cards, today's departures) uses this,
/// never the server's or the database's clock (Azure runs in UTC).
/// </summary>
public interface IBusinessClock
{
    DateTime Now { get; }
    DateTime Today => Now.Date;
}

internal sealed class BusinessClock(TimeProvider time) : IBusinessClock
{
    public const string TimeZoneId = "Asia/Ho_Chi_Minh";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public DateTime Now => TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).DateTime;

    /// <summary>A UTC time stamp (e.g. when a record was created) in the business's time.</summary>
    public static DateTime FromUtc(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
}
