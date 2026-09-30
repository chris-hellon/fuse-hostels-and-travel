using System.Text.RegularExpressions;
using Fuse.Application.Common.Mail;
using Fuse.Contracts.Site;
using Fuse.Domain.Catalog.JobVacancies;

namespace Fuse.Application.Features.Site;

// Legacy JoinOurCrew: every vacancy (deleted ones are gone), by title, and applying for one.

internal sealed class ListSiteJobVacanciesHandler(IAppDbContext db, SiteCache cache) : IQueryHandler<ListSiteJobVacancies, IReadOnlyList<SiteJobVacancy>>
{
    public async ValueTask<IReadOnlyList<SiteJobVacancy>> Handle(ListSiteJobVacancies query, CancellationToken cancellationToken) =>
        await cache.GetAsync<IReadOnlyList<SiteJobVacancy>>("site:vacancies", async () =>
            [.. (await db.JobVacancies.AsNoTracking().OrderBy(v => v.JobTitle).ThenBy(v => v.Location).ToListAsync(cancellationToken))
                .Select(SiteJobVacancies.ToSite)]);
}

internal sealed class GetSiteJobVacancyHandler(IAppDbContext db) : IQueryHandler<GetSiteJobVacancy, SiteJobVacancy>
{
    public async ValueTask<SiteJobVacancy> Handle(GetSiteJobVacancy query, CancellationToken cancellationToken) =>
        SiteJobVacancies.ToSite(await db.JobVacancies.AsNoTracking().FirstOrDefaultAsync(v => v.Id == query.Id, cancellationToken)
                                ?? throw new NotFoundException("This position is no longer available."));
}

internal sealed class ApplyForJobVacancyHandler(IAppDbContext db, SiteEnquiries enquiries) : ICommandHandler<ApplyForJobVacancy>
{
    public async ValueTask<Unit> Handle(ApplyForJobVacancy command, CancellationToken cancellationToken)
    {
        var vacancy = await db.JobVacancies.AsNoTracking().Where(v => v.Id == command.JobVacancyId)
            .Select(v => new { v.JobTitle, v.Location }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("This position is no longer available.");
        if (!await enquiries.AcceptAsync(command.Website, command.Captcha, cancellationToken))
            return Unit.Value;

        var clean = SiteEnquiries.Clean;
        var (first, last, email) = (command.FirstName.Trim(), command.LastName.Trim(), command.Email.Trim());
        var (collaborate, dates, destinations) = (command.HowCanWeCollaborate.Trim(), command.EstimatedDates.Trim(), command.DestinationsVisited.Trim());
        db.JobVacancyResponses.Add(new JobVacancyResponse(first, last, email, collaborate, dates, destinations,
            clean(command.Instagram), clean(command.TikTok), clean(command.YouTube), clean(command.Portfolio), clean(command.Equipment), command.JobVacancyId));
        await db.SaveChangesAsync(cancellationToken);

        await enquiries.SendAsync(new EnquiryEmail("Job Application", $"Job Application: {vacancy.JobTitle}",
        [
            new("Position", vacancy.JobTitle),
            new("Location", vacancy.Location),
            new("Name", $"{first} {last}"),
            new("Email", email),
            new("How can we collaborate?", collaborate),
            new("Estimated dates", dates),
            new("Destinations visited", destinations),
            new("Instagram", clean(command.Instagram)),
            new("TikTok", clean(command.TikTok)),
            new("YouTube", clean(command.YouTube)),
            new("Portfolio", clean(command.Portfolio)),
            new("Equipment", clean(command.Equipment)),
        ]), email);
        return Unit.Value;
    }
}

internal static partial class SiteJobVacancies
{
    public static SiteJobVacancy ToSite(JobVacancy vacancy) => new()
    {
        Id = vacancy.Id,
        JobTitle = vacancy.JobTitle ?? "",
        Location = vacancy.Location,
        Description = vacancy.Description,
        ApplyUrl = ApplyUrl(vacancy.CallToAction),
    };

    /// <summary>The call to action as a link: an email address (mailto:) or a website; anything else is ignored.</summary>
    public static string? ApplyUrl(string? callToAction)
    {
        var value = callToAction?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        if (Email().IsMatch(value))
            return $"mailto:{value}";
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = $"https://{value}";
        return Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Host.Contains('.') ? url.ToString() : null;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();
}
