using Fuse.Domain.Catalog.Services;

namespace Fuse.Domain.Catalog.Enquiries;

public class ServiceEnquiry : AuditableEntity<DefaultIdType>, IAggregateRoot
{
    /// <summary>For EF Core, which must never run the other constructor's logic when reading.</summary>
    private ServiceEnquiry() { }

    public ServiceEnquiry(DefaultIdType serviceId, bool responseSent)
    {
        ServiceId = serviceId;
        ResponseSent = responseSent;
    }

    public DefaultIdType ServiceId { get; private set; }
    public bool ResponseSent { get; private set; }

    public virtual IList<ServiceEnquiryField> Fields { get; set; } = null!;
    public virtual Service Service { get; set; } = null!;

    public ServiceEnquiry Update(DefaultIdType? serviceId, bool? responseSent)
    {
        if (serviceId.HasValue && ServiceId != serviceId) ServiceId = serviceId.Value;
        if (responseSent.HasValue && ResponseSent != responseSent.Value) ResponseSent = responseSent.Value;

        return this;
    }
}