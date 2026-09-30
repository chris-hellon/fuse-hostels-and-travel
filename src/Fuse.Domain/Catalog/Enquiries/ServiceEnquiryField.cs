namespace Fuse.Domain.Catalog.Enquiries;

public class ServiceEnquiryField : AuditableEntity<DefaultIdType>, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private ServiceEnquiryField() { }

    public DefaultIdType ServiceEnquiryId { get; private set; }
    public string Field { get; private set; } = null!;
    public string Value { get; private set; } = null!;

    public virtual ServiceEnquiry ServiceEnquiry { get; set; } = null!;

    public ServiceEnquiryField(string field, string value)
    {
        Field = field;
        Value = value;
    }

    public ServiceEnquiryField Update(string? field, string? value)
    {
        if (field is not null && Field?.Equals(field) is not true) Field = field;
        if (value is not null && Value?.Equals(value) is not true) Value = value;

        return this;
    }
}