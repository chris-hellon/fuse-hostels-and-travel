namespace Fuse.Contracts.Site;

// The website's enquiry forms (legacy ContactComponent and EnquireNowComponent): saved for the admin's Enquiries
// screens and emailed to the team (Site:ContactEmail), who reply to the visitor's address. Each carries the reCAPTCHA
// response (Captcha) and a field people never see (Website): anything in it means a bot, which is thanked and ignored.

/// <summary>The contact page's "How can we help?" (legacy Get In Touch).</summary>
[Post("site/enquiries"), Public, RateLimited]
public sealed record SendEnquiry(
    string Name,
    string Email,
    string ContactNumber,
    string Message,
    string? Hostel,
    string? Tour,
    string Captcha,
    string? Website = null) : ICommand;

/// <summary>Enquire Now on a tour that can't be booked online (no web prices or no open dates).</summary>
[Post("site/tours/enquiries"), Public, RateLimited]
public sealed record SendTourEnquiry(
    DefaultIdType TourId,
    string Name,
    string Email,
    string ContactNumber,
    DateTime? Date,
    int? People,
    string? SpecialRequests,
    string Captcha,
    string? Website = null) : ICommand;

public sealed class SendEnquiryValidator : AbstractValidator<SendEnquiry>
{
    public SendEnquiryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Please enter a Name.").MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Please enter an Email Address.").EmailAddress().MaximumLength(256);
        RuleFor(x => x.ContactNumber).NotEmpty().WithMessage("Please enter a Contact Number.").MaximumLength(50);
        RuleFor(x => x.Message).NotEmpty().WithMessage("Please enter a Message.").MaximumLength(4000);
        RuleFor(x => x.Hostel).MaximumLength(150);
        RuleFor(x => x.Tour).MaximumLength(150);
        RuleFor(x => x.Captcha).NotEmpty().WithMessage(EnquiryMessages.Captcha);
    }
}

public sealed class SendTourEnquiryValidator : AbstractValidator<SendTourEnquiry>
{
    public SendTourEnquiryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Please enter a Name.").MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Please enter an Email Address.").EmailAddress().MaximumLength(256);
        RuleFor(x => x.ContactNumber).NotEmpty().WithMessage("Please enter a Contact Number.").MaximumLength(50);
        RuleFor(x => x.Date).NotNull().WithMessage("Please select a Date.")
            .GreaterThanOrEqualTo(_ => DateTime.Today.AddDays(-1)).WithMessage("Please choose a date that hasn't passed.");
        RuleFor(x => x.People).NotNull().WithMessage("Please enter a Number of People.")
            .InclusiveBetween(1, 100).WithMessage("Please enter between 1 and 100 people.");
        RuleFor(x => x.SpecialRequests).MaximumLength(4000);
        RuleFor(x => x.Captcha).NotEmpty().WithMessage(EnquiryMessages.Captcha);
    }
}

public static class EnquiryMessages
{
    public const string Captcha = "Please complete the Google Captcha to send your message.";
    public const string Sent = "Your request has been sent successfully. A member of our team will contact you shortly.";
}
