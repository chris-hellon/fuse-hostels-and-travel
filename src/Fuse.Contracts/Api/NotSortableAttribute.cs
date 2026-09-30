namespace Fuse.Contracts.Api;

/// <summary>
/// A list-row value worked out after the page is read (e.g. a price range), so it can't be sorted on.
/// Sorting requests for it are ignored.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotSortableAttribute : Attribute;
