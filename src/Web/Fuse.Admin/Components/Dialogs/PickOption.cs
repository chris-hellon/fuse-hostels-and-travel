namespace Fuse.Admin.Components.Dialogs;

/// <summary>One choice in a <see cref="PickerDialog"/>: an id, a name and an optional detail (e.g. a price).</summary>
public sealed record PickOption(DefaultIdType Id, string Name, string? Detail = null);
