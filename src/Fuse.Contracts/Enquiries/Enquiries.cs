namespace Fuse.Contracts.Enquiries;

// The admin's Enquiries (legacy General / Tours / Job Vacancies enquiry screens): what's come in through the website's
// forms, newest first, to read, reply to (by email), mark as replied, export or delete. Times are Vietnam time.

public enum EnquiryKind
{
    General,
    Tour,
    JobApplication,
}

/// <summary>Where an enquiry stands: New (nothing sent yet), Waiting on us (they wrote last), Replied (we did), Closed (done).</summary>
public enum EnquiryStatus
{
    New,
    WaitingOnUs,
    Replied,
    Closed,
}

public sealed record GeneralEnquirySummary
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string ContactNumber { get; init; } = "";
    public DateTime Submitted { get; init; }
    public bool Replied { get; init; }
    public EnquiryStatus Status { get; init; }
}

public sealed record GeneralEnquiryDetails
{
    public GeneralEnquirySummary Summary { get; init; } = new();
    public string Message { get; init; } = "";
}

[Post("enquiries/general/search"), Permission(FuseAction.Search, FuseResource.Enquiries)]
public sealed record SearchGeneralEnquiries : PagedQuery, IQuery<PagedResult<GeneralEnquirySummary>>
{
    /// <summary>Only replied (true) or not yet (false) ones; all when null.</summary>
    public bool? Replied { get; init; }
    public EnquiryStatus? Status { get; init; }
}

[Get("enquiries/general/{Id}"), Permission(FuseAction.View, FuseResource.Enquiries)]
public sealed record GetGeneralEnquiry(DefaultIdType Id) : IQuery<GeneralEnquiryDetails>;

public sealed record TourEnquirySummary
{
    public DefaultIdType Id { get; init; }
    public string TourName { get; init; } = "";
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string ContactNumber { get; init; } = "";
    public DateTime RequestedDate { get; init; }
    public int NumberOfPeople { get; init; }
    public DateTime Submitted { get; init; }
    public bool Replied { get; init; }
    public EnquiryStatus Status { get; init; }
}

public sealed record TourEnquiryDetails
{
    public TourEnquirySummary Summary { get; init; } = new();
    public string? AdditionalInformation { get; init; }
}

[Post("enquiries/tours/search"), Permission(FuseAction.Search, FuseResource.Enquiries)]
public sealed record SearchTourEnquiries : PagedQuery, IQuery<PagedResult<TourEnquirySummary>>
{
    public bool? Replied { get; init; }
    public EnquiryStatus? Status { get; init; }
}

[Get("enquiries/tours/{Id}"), Permission(FuseAction.View, FuseResource.Enquiries)]
public sealed record GetTourEnquiry(DefaultIdType Id) : IQuery<TourEnquiryDetails>;

public sealed record JobApplicationSummary
{
    public DefaultIdType Id { get; init; }
    public string JobTitle { get; init; } = "";
    public string Location { get; init; } = "";
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public DateTime Submitted { get; init; }
    public EnquiryStatus Status { get; init; }
}

public sealed record JobApplicationDetails
{
    public JobApplicationSummary Summary { get; init; } = new();
    public string HowCanWeCollaborate { get; init; } = "";
    public string EstimatedDates { get; init; } = "";
    public string DestinationsVisited { get; init; } = "";
    public string? Instagram { get; init; }
    public string? TikTok { get; init; }
    public string? YouTube { get; init; }
    public string? Portfolio { get; init; }
    public string? Equipment { get; init; }
}

[Post("enquiries/job-applications/search"), Permission(FuseAction.Search, FuseResource.Enquiries)]
public sealed record SearchJobApplications : PagedQuery, IQuery<PagedResult<JobApplicationSummary>>
{
    public EnquiryStatus? Status { get; init; }
}

[Get("enquiries/job-applications/{Id}"), Permission(FuseAction.View, FuseResource.Enquiries)]
public sealed record GetJobApplication(DefaultIdType Id) : IQuery<JobApplicationDetails>;

/// <summary>Marks a general or tour enquiry as replied to (or not). Job applications have no such flag.</summary>
[Put("enquiries/{Kind}/{Id}/replied"), Permission(FuseAction.Update, FuseResource.Enquiries)]
public sealed record SetEnquiryReplied(EnquiryKind Kind, DefaultIdType Id, bool Replied) : ICommand;

[Delete("enquiries/{Kind}/{Id}"), Permission(FuseAction.Delete, FuseResource.Enquiries)]
public sealed record DeleteEnquiry(EnquiryKind Kind, DefaultIdType Id) : ICommand;

/// <summary>The enquiries of one kind (matching the search) as a spreadsheet, newest first.</summary>
[Post("enquiries/{Kind}/export"), Permission(FuseAction.Export, FuseResource.Enquiries)]
public sealed record ExportEnquiries(EnquiryKind Kind, string? Keyword, bool? Replied, EnquiryStatus? Status = null) : IQuery<FileDownload>;

/// <summary>Sets an enquiry's status by hand (mostly Closed). Replied and Closed mark it replied, as legacy's flag.</summary>
[Put("enquiries/{Kind}/{Id}/status"), Permission(FuseAction.Update, FuseResource.Enquiries)]
public sealed record SetEnquiryStatus(EnquiryKind Kind, DefaultIdType Id, EnquiryStatus Status) : ICommand;

public sealed class SetEnquiryRepliedValidator : AbstractValidator<SetEnquiryReplied>
{
    public SetEnquiryRepliedValidator() =>
        RuleFor(x => x.Kind).NotEqual(EnquiryKind.JobApplication).WithMessage("Job applications can't be marked as replied.");
}
