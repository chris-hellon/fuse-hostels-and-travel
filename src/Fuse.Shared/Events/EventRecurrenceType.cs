namespace Fuse.Shared.Events;

/// <summary>How an event repeats. Occurrences are computed on the fly from the rule, not stored per date.</summary>
public enum EventRecurrenceType
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}
