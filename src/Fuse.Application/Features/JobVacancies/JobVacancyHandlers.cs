using Fuse.Contracts.JobVacancies;
using Fuse.Domain.Catalog.JobVacancies;

namespace Fuse.Application.Features.JobVacancies;

internal sealed class SearchJobVacanciesHandler(IAppDbContext db) : IQueryHandler<SearchJobVacancies, PagedResult<JobVacancySummary>>
{
    public ValueTask<PagedResult<JobVacancySummary>> Handle(SearchJobVacancies query, CancellationToken cancellationToken)
    {
        var vacancies = db.JobVacancies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            vacancies = vacancies.Where(v => (v.JobTitle != null && v.JobTitle.Contains(query.Keyword)) || v.Location.Contains(query.Keyword));

        return new(vacancies
            .Select(v => new JobVacancySummary
            {
                Id = v.Id,
                JobTitle = v.JobTitle,
                Location = v.Location,
                Responses = db.JobVacancyResponses.Count(r => r.JobVacancyId == v.Id),
            })
            .OrderBy(query.OrderBy, v => v.JobTitle)
            .ToPagedResultAsync(query, cancellationToken));
    }
}

internal sealed class GetJobVacancyHandler(IAppDbContext db) : IQueryHandler<GetJobVacancy, JobVacancyInput>
{
    public async ValueTask<JobVacancyInput> Handle(GetJobVacancy query, CancellationToken cancellationToken) =>
        await db.JobVacancies.AsNoTracking().Where(v => v.Id == query.Id)
            .Select(v => new JobVacancyInput { JobTitle = v.JobTitle ?? "", Location = v.Location, CallToAction = v.CallToAction, Description = v.Description })
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Job vacancy not found.");
}

internal sealed class SaveJobVacancyHandler(IAppDbContext db) : ICommandHandler<SaveJobVacancy, DefaultIdType>
{
    public async ValueTask<DefaultIdType> Handle(SaveJobVacancy command, CancellationToken cancellationToken)
    {
        var d = command.Details;
        JobVacancy vacancy;
        if (command.Id is { } id)
        {
            vacancy = await db.JobVacancies.FirstOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new NotFoundException("Job vacancy not found.");
            vacancy.Apply(d.JobTitle, d.Location, d.Description, d.CallToAction);
        }
        else
        {
            vacancy = JobVacancy.Create(d.JobTitle, d.Location, d.Description, d.CallToAction);
            db.JobVacancies.Add(vacancy);
        }
        await db.SaveChangesAsync(cancellationToken);
        return vacancy.Id;
    }
}

/// <summary>A soft delete: applications already received stay on the Job Vacancies enquiries screen.</summary>
internal sealed class DeleteJobVacancyHandler(IAppDbContext db) : ICommandHandler<DeleteJobVacancy>
{
    public async ValueTask<Unit> Handle(DeleteJobVacancy command, CancellationToken cancellationToken)
    {
        var vacancy = await db.JobVacancies.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Job vacancy not found.");
        db.JobVacancies.Remove(vacancy);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
