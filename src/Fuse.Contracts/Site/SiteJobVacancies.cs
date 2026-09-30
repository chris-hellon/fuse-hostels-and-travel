namespace Fuse.Contracts.Site;

// The website's Join Our Crew (legacy JoinOurCrew page): the open positions, and applying for one. Applications are
// saved for the admin's Job Vacancies enquiries and emailed to the team, like the other enquiry forms.

public sealed record SiteJobVacancy
{
    public DefaultIdType Id { get; init; }
    public string JobTitle { get; init; } = "";
    public string Location { get; init; } = "";
    /// <summary>Rich text (HTML).</summary>
    public string? Description { get; init; }
    /// <summary>
    /// Where to apply instead of our form, when the vacancy says (its call to action: a website, or an email address
    /// as a mailto: link).
    /// </summary>
    public string? ApplyUrl { get; init; }
}

[Get("site/job-vacancies"), Public]
public sealed record ListSiteJobVacancies : IQuery<IReadOnlyList<SiteJobVacancy>>;

[Get("site/job-vacancies/{Id}"), Public]
public sealed record GetSiteJobVacancy(DefaultIdType Id) : IQuery<SiteJobVacancy>;

/// <summary>Legacy JoinOurCrewComponent: an application for a vacancy.</summary>
[Post("site/job-vacancies/{JobVacancyId}/applications"), Public, RateLimited]
public sealed record ApplyForJobVacancy(
    DefaultIdType JobVacancyId,
    string FirstName,
    string LastName,
    string Email,
    string HowCanWeCollaborate,
    string EstimatedDates,
    string DestinationsVisited,
    string? Instagram,
    string? TikTok,
    string? YouTube,
    string? Portfolio,
    string? Equipment,
    string Captcha,
    string? Website = null) : ICommand;

public sealed class ApplyForJobVacancyValidator : AbstractValidator<ApplyForJobVacancy>
{
    public ApplyForJobVacancyValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Please enter your First Name.").MaximumLength(256);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Please enter your Last Name.").MaximumLength(256);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Please enter an Email Address.").EmailAddress().MaximumLength(256);
        RuleFor(x => x.HowCanWeCollaborate).NotEmpty().WithMessage("Please tell us how we can collaborate.").MaximumLength(2000);
        RuleFor(x => x.EstimatedDates).NotEmpty().WithMessage("Please tell us your estimated dates.").MaximumLength(500);
        RuleFor(x => x.DestinationsVisited).NotEmpty().WithMessage("Please tell us where you've been.").MaximumLength(500);
        RuleFor(x => x.Instagram).MaximumLength(256);
        RuleFor(x => x.TikTok).MaximumLength(256);
        RuleFor(x => x.YouTube).MaximumLength(256);
        RuleFor(x => x.Portfolio).MaximumLength(256);
        RuleFor(x => x.Equipment).MaximumLength(500);
        RuleFor(x => x.Captcha).NotEmpty().WithMessage(EnquiryMessages.Captcha);
    }
}
