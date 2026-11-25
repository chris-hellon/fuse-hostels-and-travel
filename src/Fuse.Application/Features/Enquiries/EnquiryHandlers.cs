using Fuse.Application.Common.Files;
using Fuse.Application.Common.Time;
using Fuse.Contracts.Enquiries;
using Fuse.Domain.Catalog.Enquiries;
using Fuse.Domain.Catalog.JobVacancies;

namespace Fuse.Application.Features.Enquiries;

// The admin's enquiry screens. Newest first unless sorted otherwise; the search matches names, emails, numbers and
// (for tours and jobs) the tour or position. Deleting is a soft delete, as everywhere.

internal static class EnquiryQueries
{
    public static IQueryable<GeneralEnquiry> General(IAppDbContext db, string? keyword, bool? replied, EnquiryStatus? status = null)
    {
        var q = db.GeneralEnquiries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(e => e.Name.Contains(keyword) || e.Email.Contains(keyword) || e.ContactNumber.Contains(keyword));
        if (status is { } wanted)
        {
            var stage = (EnquiryStage)(int)wanted;
            q = q.Where(e => (db.EnquiryStates.Where(s => s.EnquiryType == EnquiryType.General && s.EnquiryId == e.Id).Select(s => (EnquiryStage?)s.Stage).FirstOrDefault()
                              ?? (e.ResponseSent ? EnquiryStage.Replied : EnquiryStage.New)) == stage);
        }
        return replied is { } r ? q.Where(e => e.ResponseSent == r) : q;
    }

    public static IQueryable<TourEnquiry> Tours(IAppDbContext db, string? keyword, bool? replied, EnquiryStatus? status = null)
    {
        var q = db.TourEnquiries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(e => e.Name.Contains(keyword) || e.Email.Contains(keyword) || e.ContactNumber.Contains(keyword)
                             || db.Tours.IgnoreQueryFilters().Any(t => t.Id == e.TourId && t.Name.Contains(keyword)));
        if (status is { } wanted)
        {
            var stage = (EnquiryStage)(int)wanted;
            q = q.Where(e => (db.EnquiryStates.Where(s => s.EnquiryType == EnquiryType.Tour && s.EnquiryId == e.Id).Select(s => (EnquiryStage?)s.Stage).FirstOrDefault()
                              ?? (e.ResponseSent ? EnquiryStage.Replied : EnquiryStage.New)) == stage);
        }
        return replied is { } r ? q.Where(e => e.ResponseSent == r) : q;
    }

    public static IQueryable<JobVacancyResponse> Jobs(IAppDbContext db, string? keyword, EnquiryStatus? status = null)
    {
        if (status is { } wanted)
        {
            var stage = (EnquiryStage)(int)wanted;
            return JobsMatching(db, keyword).Where(e => (db.EnquiryStates.Where(s => s.EnquiryType == EnquiryType.JobApplication && s.EnquiryId == e.Id)
                .Select(s => (EnquiryStage?)s.Stage).FirstOrDefault() ?? EnquiryStage.New) == stage);
        }
        return JobsMatching(db, keyword);
    }

    private static IQueryable<JobVacancyResponse> JobsMatching(IAppDbContext db, string? keyword)
    {
        // Applications stay when their vacancy is deleted, so the vacancy is read past the soft-delete filter.
        var q = db.JobVacancyResponses.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
            q = q.Where(e => e.FirstName.Contains(keyword) || e.LastName.Contains(keyword) || e.Email.Contains(keyword)
                             || db.JobVacancies.IgnoreQueryFilters().Any(v => v.Id == e.JobVacancyId && (v.JobTitle!.Contains(keyword) || v.Location.Contains(keyword))));
        return q;
    }

    public static IQueryable<GeneralEnquirySummary> ToSummary(this IQueryable<GeneralEnquiry> q, IAppDbContext db) => q.Select(e => new GeneralEnquirySummary
    {
        Id = e.Id, Name = e.Name, Email = e.Email, ContactNumber = e.ContactNumber, Submitted = e.CreatedOn, Replied = e.ResponseSent,
        Status = (EnquiryStatus)(int)(db.EnquiryStates.Where(s => s.EnquiryType == EnquiryType.General && s.EnquiryId == e.Id).Select(s => (EnquiryStage?)s.Stage).FirstOrDefault()
                                      ?? (e.ResponseSent ? EnquiryStage.Replied : EnquiryStage.New)),
    });

    // The tour is read past the soft-delete filter: enquiries about a tour since deleted still show.
    public static IQueryable<TourEnquirySummary> ToSummary(this IQueryable<TourEnquiry> q, IAppDbContext db) => q.Select(e => new TourEnquirySummary
    {
        Id = e.Id, TourName = db.Tours.IgnoreQueryFilters().Where(t => t.Id == e.TourId).Select(t => t.Name).FirstOrDefault() ?? "", Name = e.Name, Email = e.Email, ContactNumber = e.ContactNumber,
        RequestedDate = e.RequestedDate, NumberOfPeople = e.NumberOfPeople, Submitted = e.CreatedOn, Replied = e.ResponseSent,
        Status = (EnquiryStatus)(int)(db.EnquiryStates.Where(s => s.EnquiryType == EnquiryType.Tour && s.EnquiryId == e.Id).Select(s => (EnquiryStage?)s.Stage).FirstOrDefault()
                                      ?? (e.ResponseSent ? EnquiryStage.Replied : EnquiryStage.New)),
    });

    public static IQueryable<JobApplicationSummary> ToSummary(this IQueryable<JobVacancyResponse> q, IAppDbContext db) => q.Select(e => new JobApplicationSummary
    {
        Id = e.Id,
        JobTitle = db.JobVacancies.IgnoreQueryFilters().Where(v => v.Id == e.JobVacancyId).Select(v => v.JobTitle).FirstOrDefault() ?? "",
        Location = db.JobVacancies.IgnoreQueryFilters().Where(v => v.Id == e.JobVacancyId).Select(v => v.Location).FirstOrDefault() ?? "",
        Name = e.FirstName + " " + e.LastName,
        Email = e.Email,
        Submitted = e.CreatedOn,
        Status = (EnquiryStatus)(int)(db.EnquiryStates.Where(s => s.EnquiryType == EnquiryType.JobApplication && s.EnquiryId == e.Id).Select(s => (EnquiryStage?)s.Stage).FirstOrDefault()
                                      ?? EnquiryStage.New),
    });

    /// <summary>Stored in UTC; shown in the business's time.</summary>
    public static PagedResult<T> Local<T>(this PagedResult<T> page, Func<T, T> local) => page with { Items = [.. page.Items.Select(local)] };
}

internal sealed class SearchGeneralEnquiriesHandler(IAppDbContext db) : IQueryHandler<SearchGeneralEnquiries, PagedResult<GeneralEnquirySummary>>
{
    public async ValueTask<PagedResult<GeneralEnquirySummary>> Handle(SearchGeneralEnquiries query, CancellationToken cancellationToken) =>
        (await EnquiryQueries.General(db, query.Keyword, query.Replied, query.Status).ToSummary(db)
            .OrderBy(query.OrderBy, e => e.Submitted, descending: true)
            .ToPagedResultAsync(query, cancellationToken))
        .Local(e => e with { Submitted = BusinessClock.FromUtc(e.Submitted) });
}

internal sealed class GetGeneralEnquiryHandler(IAppDbContext db) : IQueryHandler<GetGeneralEnquiry, GeneralEnquiryDetails>
{
    public async ValueTask<GeneralEnquiryDetails> Handle(GetGeneralEnquiry query, CancellationToken cancellationToken)
    {
        var summary = await db.GeneralEnquiries.AsNoTracking().Where(x => x.Id == query.Id).ToSummary(db).FirstOrDefaultAsync(cancellationToken)
                      ?? throw new NotFoundException("Enquiry not found.");
        var message = await db.GeneralEnquiries.AsNoTracking().Where(x => x.Id == query.Id).Select(x => x.Message).FirstAsync(cancellationToken);
        return new GeneralEnquiryDetails { Summary = summary with { Submitted = BusinessClock.FromUtc(summary.Submitted) }, Message = message };
    }
}

internal sealed class SearchTourEnquiriesHandler(IAppDbContext db) : IQueryHandler<SearchTourEnquiries, PagedResult<TourEnquirySummary>>
{
    public async ValueTask<PagedResult<TourEnquirySummary>> Handle(SearchTourEnquiries query, CancellationToken cancellationToken) =>
        (await EnquiryQueries.Tours(db, query.Keyword, query.Replied, query.Status).ToSummary(db)
            .OrderBy(query.OrderBy, e => e.Submitted, descending: true)
            .ToPagedResultAsync(query, cancellationToken))
        .Local(e => e with { Submitted = BusinessClock.FromUtc(e.Submitted) });
}

internal sealed class GetTourEnquiryHandler(IAppDbContext db) : IQueryHandler<GetTourEnquiry, TourEnquiryDetails>
{
    public async ValueTask<TourEnquiryDetails> Handle(GetTourEnquiry query, CancellationToken cancellationToken)
    {
        var summary = await db.TourEnquiries.AsNoTracking().Where(x => x.Id == query.Id).ToSummary(db).FirstOrDefaultAsync(cancellationToken)
                      ?? throw new NotFoundException("Enquiry not found.");
        var info = await db.TourEnquiries.AsNoTracking().Where(x => x.Id == query.Id).Select(x => x.AdditionalInformation).FirstOrDefaultAsync(cancellationToken);
        return new TourEnquiryDetails { Summary = summary with { Submitted = BusinessClock.FromUtc(summary.Submitted) }, AdditionalInformation = info };
    }
}

internal sealed class SearchJobApplicationsHandler(IAppDbContext db) : IQueryHandler<SearchJobApplications, PagedResult<JobApplicationSummary>>
{
    public async ValueTask<PagedResult<JobApplicationSummary>> Handle(SearchJobApplications query, CancellationToken cancellationToken) =>
        (await EnquiryQueries.Jobs(db, query.Keyword, query.Status).ToSummary(db)
            .OrderBy(query.OrderBy, e => e.Submitted, descending: true)
            .ToPagedResultAsync(query, cancellationToken))
        .Local(e => e with { Submitted = BusinessClock.FromUtc(e.Submitted) });
}

internal sealed class GetJobApplicationHandler(IAppDbContext db) : IQueryHandler<GetJobApplication, JobApplicationDetails>
{
    public async ValueTask<JobApplicationDetails> Handle(GetJobApplication query, CancellationToken cancellationToken)
    {
        var summary = await db.JobVacancyResponses.AsNoTracking().Where(x => x.Id == query.Id).ToSummary(db).FirstOrDefaultAsync(cancellationToken)
                      ?? throw new NotFoundException("Application not found.");
        var e = await db.JobVacancyResponses.AsNoTracking().FirstAsync(x => x.Id == query.Id, cancellationToken);
        return new JobApplicationDetails
        {
            Summary = summary with { Submitted = BusinessClock.FromUtc(summary.Submitted) },
            HowCanWeCollaborate = e.HowCanWeCollaborate, EstimatedDates = e.EstimatedDates, DestinationsVisited = e.DestinationsVisited,
            Instagram = e.Instagram, TikTok = e.TikTok, YouTube = e.YouTube, Portfolio = e.Portfolio, Equipment = e.Equipment,
        };
    }
}

internal sealed class SetEnquiryRepliedHandler(IAppDbContext db) : ICommandHandler<SetEnquiryReplied>
{
    public async ValueTask<Unit> Handle(SetEnquiryReplied command, CancellationToken cancellationToken)
    {
        await EnquiryMail.EnsureExistsAsync(db, command.Kind, command.Id, cancellationToken);
        await EnquiryMail.SetStageAsync(db, EnquiryMail.Type(command.Kind), command.Id, command.Replied ? EnquiryStage.Replied : EnquiryStage.New, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class SetEnquiryStatusHandler(IAppDbContext db) : ICommandHandler<SetEnquiryStatus>
{
    public async ValueTask<Unit> Handle(SetEnquiryStatus command, CancellationToken cancellationToken)
    {
        await EnquiryMail.EnsureExistsAsync(db, command.Kind, command.Id, cancellationToken);
        await EnquiryMail.SetStageAsync(db, EnquiryMail.Type(command.Kind), command.Id, (EnquiryStage)(int)command.Status, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class DeleteEnquiryHandler(IAppDbContext db) : ICommandHandler<DeleteEnquiry>
{
    public async ValueTask<Unit> Handle(DeleteEnquiry command, CancellationToken cancellationToken)
    {
        switch (command.Kind)
        {
            case EnquiryKind.General:
                db.GeneralEnquiries.Remove(await db.GeneralEnquiries.FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Enquiry not found."));
                break;
            case EnquiryKind.Tour:
                db.TourEnquiries.Remove(await db.TourEnquiries.FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Enquiry not found."));
                break;
            default:
                db.JobVacancyResponses.Remove(await db.JobVacancyResponses.FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Application not found."));
                break;
        }
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

internal sealed class ExportEnquiriesHandler(IAppDbContext db, ISpreadsheetWriter spreadsheets, IBusinessClock clock) : IQueryHandler<ExportEnquiries, FileDownload>
{
    /// <summary>A bound on one export.</summary>
    private const int Max = 20_000;

    public async ValueTask<FileDownload> Handle(ExportEnquiries query, CancellationToken cancellationToken)
    {
        var stamp = clock.Today.ToString("yyyy-MM-dd");
        const string When = "dd/mm/yyyy hh:mm";
        switch (query.Kind)
        {
            case EnquiryKind.General:
            {
                var rows = await EnquiryQueries.General(db, query.Keyword, query.Replied, query.Status).OrderByDescending(e => e.CreatedOn).Take(Max)
                    .Select(e => new { e.Name, e.Email, e.ContactNumber, e.Message, e.CreatedOn, e.ResponseSent }).ToListAsync(cancellationToken);
                return spreadsheets.Write($"General enquiries {stamp}.xlsx", rows,
                [
                    new("Submitted", r => BusinessClock.FromUtc(r.CreatedOn), When), new("Name", r => r.Name), new("Email", r => r.Email),
                    new("Contact Number", r => r.ContactNumber), new("Message", r => r.Message), new("Replied", r => r.ResponseSent ? "Yes" : "No"),
                ]);
            }
            case EnquiryKind.Tour:
            {
                var rows = await EnquiryQueries.Tours(db, query.Keyword, query.Replied, query.Status).OrderByDescending(e => e.CreatedOn).Take(Max)
                    .Select(e => new { Tour = db.Tours.IgnoreQueryFilters().Where(t => t.Id == e.TourId).Select(t => t.Name).FirstOrDefault(), e.Name, e.Email, e.ContactNumber, e.RequestedDate, e.NumberOfPeople, e.AdditionalInformation, e.CreatedOn, e.ResponseSent })
                    .ToListAsync(cancellationToken);
                return spreadsheets.Write($"Tour enquiries {stamp}.xlsx", rows,
                [
                    new("Submitted", r => BusinessClock.FromUtc(r.CreatedOn), When), new("Tour", r => r.Tour), new("Requested Date", r => r.RequestedDate, "dd/mm/yyyy"),
                    new("People", r => r.NumberOfPeople), new("Name", r => r.Name), new("Email", r => r.Email), new("Contact Number", r => r.ContactNumber),
                    new("Special Requests", r => r.AdditionalInformation), new("Replied", r => r.ResponseSent ? "Yes" : "No"),
                ]);
            }
            default:
            {
                var ids = EnquiryQueries.Jobs(db, query.Keyword, query.Status).Select(e => e.Id);
                var rows = await db.JobVacancyResponses.AsNoTracking().Where(e => ids.Contains(e.Id)).OrderByDescending(e => e.CreatedOn).Take(Max)
                    .Select(e => new
                    {
                        Job = db.JobVacancies.IgnoreQueryFilters().Where(v => v.Id == e.JobVacancyId).Select(v => v.JobTitle + " (" + v.Location + ")").FirstOrDefault(),
                        e.FirstName, e.LastName, e.Email, e.HowCanWeCollaborate, e.EstimatedDates, e.DestinationsVisited,
                        e.Instagram, e.TikTok, e.YouTube, e.Portfolio, e.Equipment, e.CreatedOn,
                    })
                    .ToListAsync(cancellationToken);
                return spreadsheets.Write($"Job applications {stamp}.xlsx", rows,
                [
                    new("Submitted", r => BusinessClock.FromUtc(r.CreatedOn), When), new("Position", r => r.Job), new("First Name", r => r.FirstName),
                    new("Last Name", r => r.LastName), new("Email", r => r.Email), new("How can we collaborate?", r => r.HowCanWeCollaborate),
                    new("Estimated dates", r => r.EstimatedDates), new("Destinations visited", r => r.DestinationsVisited), new("Instagram", r => r.Instagram),
                    new("TikTok", r => r.TikTok), new("YouTube", r => r.YouTube), new("Portfolio", r => r.Portfolio), new("Equipment", r => r.Equipment),
                ]);
            }
        }
    }
}
