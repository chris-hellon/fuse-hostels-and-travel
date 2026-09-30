namespace Fuse.Contracts.JobVacancies;

public sealed record JobVacancySummary
{
    public DefaultIdType Id { get; init; }
    public string? JobTitle { get; init; }
    public string Location { get; init; } = "";
    /// <summary>Applications received (they come in through the website).</summary>
    public int Responses { get; init; }
}

[Post("job-vacancies/search"), Permission(FuseAction.Search, FuseResource.JobVacancies)]
public sealed record SearchJobVacancies : PagedQuery, IQuery<PagedResult<JobVacancySummary>>;

public sealed record JobVacancyInput
{
    public string JobTitle { get; set; } = "";
    public string Location { get; set; } = "";
    /// <summary>Where to apply: a web address or an email address.</summary>
    public string? CallToAction { get; set; }
    public string? Description { get; set; }
}

[Get("job-vacancies/{Id}"), Permission(FuseAction.View, FuseResource.JobVacancies)]
public sealed record GetJobVacancy(DefaultIdType Id) : IQuery<JobVacancyInput>;

/// <summary>Creates (no <see cref="Id"/>) or updates a vacancy.</summary>
[Put("job-vacancies"), Permission(FuseAction.Update, FuseResource.JobVacancies)]
public sealed record SaveJobVacancy(DefaultIdType? Id, JobVacancyInput Details) : ICommand<DefaultIdType>;

[Delete("job-vacancies/{Id}"), Permission(FuseAction.Delete, FuseResource.JobVacancies)]
public sealed record DeleteJobVacancy(DefaultIdType Id) : ICommand;

public sealed class SaveJobVacancyValidator : AbstractValidator<SaveJobVacancy>
{
    public SaveJobVacancyValidator()
    {
        RuleFor(x => x.Details.JobTitle).NotEmpty().MaximumLength(256).WithName("Job title");
        RuleFor(x => x.Details.Location).NotEmpty().MaximumLength(256).WithName("Location");
        RuleFor(x => x.Details.CallToAction).MaximumLength(1024).WithName("Call to action");
    }
}
