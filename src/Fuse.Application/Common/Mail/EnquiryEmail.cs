namespace Fuse.Application.Common.Mail;

/// <summary>A website form sent to the team: its heading and each answer, in order.</summary>
public sealed record EnquiryEmail(string Subject, string Title, IReadOnlyList<EnquiryEmailField> Fields);

public sealed record EnquiryEmailField(string Label, string? Value);

/// <summary>Renders the website's enquiry emails (legacy ContactTemplate, TourEnquiryTemplate), branded from Mail:Brand.</summary>
public interface IEnquiryEmails
{
    Task<EmailMessage> RenderAsync(EnquiryEmail enquiry, string to, string replyTo);
}
