namespace Fuse.Domain.Common.Contracts;

/// <summary>Raised by an entity; published through Mediator after the unit of work commits.</summary>
public abstract class DomainEvent : INotification
{
    public DateTime TriggeredOn { get; protected set; } = DateTime.UtcNow;
}
