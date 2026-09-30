namespace Fuse.Domain.Catalog.Enquiries;

public class GeneralEnquiry : AuditableEntity<DefaultIdType>, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private GeneralEnquiry() { }

    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string ContactNumber { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public bool ResponseSent { get; private set; }

    public GeneralEnquiry(string name, string email, string contactNumber, string message, bool responseSent)
    {
        Name = name;
        Email = email;
        ContactNumber = contactNumber;
        Message = message;
        ResponseSent = responseSent;
    }

    public GeneralEnquiry Update(string? name, string? email, string? contactNumber, string? message, bool? responseSent)
    {
        if (name is not null && Name?.Equals(name) is not true) Name = name;
        if (email is not null && Email?.Equals(email) is not true) Email = email;
        if (contactNumber is not null && ContactNumber?.Equals(contactNumber) is not true) ContactNumber = contactNumber;
        if (message is not null && Message?.Equals(message) is not true) Message = message;
        if (responseSent.HasValue && ResponseSent != responseSent.Value) ResponseSent = responseSent.Value;

        return this;
    }
}