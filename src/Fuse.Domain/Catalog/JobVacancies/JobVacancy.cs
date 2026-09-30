namespace Fuse.Domain.Catalog.JobVacancies;

public class JobVacancy : AuditableEntity, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private JobVacancy() { }

    public JobVacancy(string location, string? jobTitle, string? description, string? callToAction)
    {
        Location = location;
        JobTitle = jobTitle;
        Description = description.NormalizeRichText();
        CallToAction = callToAction;
    }

    public string Location { get; private set; }
    public string? JobTitle { get; private set; }
    public string? Description { get; private set; }
    public string? CallToAction { get; private set; }

    public virtual IList<JobVacancyResponse>? Responses { get; set; }

    public JobVacancy Update(string? location, string? jobTitle, string? description, string? callToAction)
    {
        if (location is not null && Location?.Equals(location) is not true) Location = location;
        if (jobTitle is not null && JobTitle?.Equals(jobTitle) is not true) JobTitle = jobTitle;
        if (description is not null && Description?.Equals(description) is not true) Description = description.NormalizeRichText();;
        if (callToAction is not null && CallToAction?.Equals(callToAction) is not true) CallToAction = callToAction;
        return this;
    }

    public static JobVacancy Create(string jobTitle, string location, string? description, string? callToAction)
    {
        var vacancy = new JobVacancy();
        vacancy.Apply(jobTitle, location, description, callToAction);
        return vacancy;
    }

    /// <summary>Sets every field (legacy Update skipped empty values, so a call to action could never be cleared).</summary>
    public void Apply(string jobTitle, string location, string? description, string? callToAction)
    {
        JobTitle = jobTitle.Trim();
        Location = location.Trim();
        Description = description.NormalizeRichText();
        CallToAction = string.IsNullOrWhiteSpace(callToAction) ? null : callToAction.Trim();
    }
}
