using Fuse.Contracts.Dashboard;
using Fuse.Contracts.Tours;

namespace Fuse.Contracts.Manifest;

/// <summary>A guest (lead or additional) on a tour departing today.</summary>
public sealed record ManifestRow
{
    public int Reference { get; init; }
    public DefaultIdType TourId { get; init; }
    public string TourName { get; init; } = "";
    public DefaultIdType? TourDateId { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string GuestId { get; init; } = "";
    public string GuestName { get; init; } = "";
    public bool WaiverSigned { get; init; }
    public BookingState Status { get; init; }
}

/// <summary>Today's manifest (business time zone), optionally one tour and one departure time.</summary>
[Post("manifest/search"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record SearchManifest : PagedQuery, IQuery<PagedResult<ManifestRow>>
{
    public DefaultIdType? TourId { get; init; }
    public TimeOnly? StartTime { get; init; }
}

/// <summary>Tours departing today (the manifest's tour filter).</summary>
[Get("manifest/tours"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record ListManifestTours : IQuery<IReadOnlyList<LookupItem>>;

/// <summary>A tour's departure times today (the manifest's time filter).</summary>
[Get("manifest/tours/{TourId}/times"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record ListManifestTimes(DefaultIdType TourId) : IQuery<IReadOnlyList<TimeOnly>>;

public enum GuestAttendance
{
    CheckedIn,
    NoShow,
    Cancelled,
}

/// <summary>
/// Checks a guest in, or marks them a no show or cancelled (clearing the other two), as legacy.
/// Anyone who can see the manifest can do this: it's the front desk's job.
/// </summary>
[Put("manifest/attendance"), Permission(FuseAction.View, FuseResource.TourBookings)]
public sealed record SetGuestAttendance(int Reference, DefaultIdType TourId, DateTime StartDate, DefaultIdType? TourDateId, string GuestId, GuestAttendance Attendance) : ICommand;

/// <summary>Today's paid guests as a spreadsheet (the legacy manifest columns).</summary>
[Post("manifest/export"), Permission(FuseAction.Export, FuseResource.TourBookings)]
public sealed record ExportManifest(DefaultIdType? TourId, TimeOnly? StartTime, string? Keyword) : IQuery<FileDownload>;
