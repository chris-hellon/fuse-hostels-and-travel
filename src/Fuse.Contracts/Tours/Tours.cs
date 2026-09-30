namespace Fuse.Contracts.Tours;

public sealed record TourSummary
{
    public DefaultIdType Id { get; init; }
    public string Name { get; init; } = "";
    public string? CategoryName { get; init; }
    public bool PublishToSite { get; init; }
    public string? ThumbnailImagePath { get; init; }
    public int FutureDates { get; init; }
}

/// <summary>A tour's own fields (prices, dates and images travel alongside in <see cref="TourChanges"/>).</summary>
public sealed record TourInput : ISeoFields
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ShortDescription { get; set; }
    public string? WhatsIncluded { get; set; }
    public string? WhatsNotIncluded { get; set; }
    public string? AdditionalInformation { get; set; }
    public string? ImportantInformation { get; set; }
    public string? TelephoneNumber { get; set; }
    public string? DayDuration { get; set; }
    public string? NightDuration { get; set; }
    public string? HourDuration { get; set; }
    public int? MinCapacity { get; set; }
    /// <summary>Empty = unlimited.</summary>
    public int? MaxCapacity { get; set; }
    public bool PublishToSite { get; set; }
    public DefaultIdType? TourCategoryId { get; set; }
    public List<DefaultIdType> LocationIds { get; set; } = [];
    /// <summary>Properties guests can be collected from.</summary>
    public List<DefaultIdType> PickupPropertyIds { get; set; } = [];
    public string? SupplierId { get; set; }
    public bool WaiverRequired { get; set; }
    public bool ShowBookingQRCode { get; set; }
    public bool ExcludeFromStaffSales { get; set; }
    public bool AdditionalGuestDetailsRequired { get; set; }
    public string? BookingConfirmationEmailDetails { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? CancellationPolicy { get; set; }
    public string? SeoPageTitle { get; set; }
    public string? H1 { get; set; }
    public string? H2 { get; set; }
    public string? UrlSlug { get; set; }
    public string? MetaKeywords { get; set; }
    public string? MetaDescription { get; set; }
    public string? CustomSeoScripts { get; set; }
}

public sealed record TourImageItem(DefaultIdType Id, string ImagePath, string ThumbnailImagePath, int SortOrder);

/// <summary>Everything the tour wizard edits. Only upcoming departures are included.</summary>
public sealed record TourEditor
{
    public DefaultIdType Id { get; init; }
    public TourInput Details { get; init; } = new();
    public IReadOnlyList<TourPriceEdit> Prices { get; init; } = [];
    public IReadOnlyList<TourDateItem> Dates { get; init; } = [];
    /// <summary>The tour's capacity per departure (new departures start with this many spaces).</summary>
    public int Capacity { get; init; }
    public string? ImagePath { get; init; }
    public IReadOnlyList<TourImageItem> Gallery { get; init; } = [];
}

/// <summary>An image in the gallery: an existing one (<see cref="Id"/>) or a new upload (<see cref="Url"/>).</summary>
public sealed record TourGalleryRef(DefaultIdType? Id, string? Url);

/// <summary>
/// Everything the wizard changed, saved together in one transaction when the user presses Save.
/// Prices and gallery are the complete wanted lists (anything missing is removed); dates are
/// changes, because only upcoming departures are ever loaded.
/// </summary>
public sealed record TourChanges
{
    public TourInput Details { get; init; } = new();
    public List<TourPriceEdit> Prices { get; init; } = [];
    public List<TourDateEdit> AddedDates { get; init; } = [];
    public List<TourDateEdit> ChangedDates { get; init; } = [];
    public List<DefaultIdType> DeletedDateIds { get; init; } = [];
    /// <summary>A new banner (URL from <see cref="UploadTourImage"/>), or null to keep the current one.</summary>
    public string? NewBanner { get; init; }
    public bool RemoveBanner { get; init; }
    public List<TourGalleryRef> Gallery { get; init; } = [];
}

[Post("tours/search"), Permission(FuseAction.Search, FuseResource.Tours)]
public sealed record SearchTours : PagedQuery, IQuery<PagedResult<TourSummary>>
{
    public DefaultIdType? TourCategoryId { get; init; }
    public DefaultIdType? LocationId { get; init; }
}

[Get("tours/{Id}"), Permission(FuseAction.View, FuseResource.Tours)]
public sealed record GetTourEditor(DefaultIdType Id) : IQuery<TourEditor>;

[Post("tours"), Permission(FuseAction.Create, FuseResource.Tours)]
public sealed record CreateTour(TourChanges Changes) : ICommand<DefaultIdType>;

[Put("tours/{Id}"), Permission(FuseAction.Update, FuseResource.Tours)]
public sealed record UpdateTour(DefaultIdType Id, TourChanges Changes) : ICommand;

/// <summary>Refused while the tour has upcoming bookings.</summary>
[Delete("tours/{Id}"), Permission(FuseAction.Delete, FuseResource.Tours)]
public sealed record DeleteTour(DefaultIdType Id) : ICommand;

/// <summary>
/// Stores an image the wizard will attach on Save (banner or gallery) and returns its URL.
/// Uploading straight away keeps the Save request small however many images are added.
/// </summary>
[Post("tours/images"), Permission(FuseAction.Update, FuseResource.Tours)]
public sealed record UploadTourImage(FileUpload File) : ICommand<string>;

// Lookups for the editor ----------------------------------------------------------------------

public sealed record LookupItem(string Id, string Name);

/// <summary>Users with the Supplier role (for the tour's supplier).</summary>
[Get("tours/lookups/suppliers"), Permission(FuseAction.View, FuseResource.Tours)]
public sealed record ListSuppliers : IQuery<IReadOnlyList<LookupItem>>;

/// <summary>Properties a tour can pick guests up from.</summary>
[Get("tours/lookups/pickup-properties"), Permission(FuseAction.View, FuseResource.Tours)]
public sealed record ListPickupProperties : IQuery<IReadOnlyList<LookupItem>>;

public sealed class TourInputValidator : AbstractValidator<TourInput>
{
    public TourInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(1024);
        RuleFor(x => x.MinCapacity).NotNull().GreaterThanOrEqualTo(1).WithName("Min Capacity");
        RuleFor(x => x.MaxCapacity).GreaterThanOrEqualTo(x => x.MinCapacity ?? 1).When(x => x.MaxCapacity.HasValue)
            .WithMessage("Max Capacity can't be less than Min Capacity.");
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.DayDuration) || !string.IsNullOrWhiteSpace(x.NightDuration) || !string.IsNullOrWhiteSpace(x.HourDuration))
            .WithName("Duration").WithMessage("Enter a day, night or hour duration.");
        RuleFor(x => x.LocationIds).NotEmpty().WithName("Location").WithMessage("Choose at least one location.");
        RuleFor(x => x.PickupPropertyIds).NotEmpty().WithName("Pick Up Location").WithMessage("Choose at least one pick up location.");
    }
}

public sealed class TourChangesValidator : AbstractValidator<TourChanges>
{
    public TourChangesValidator()
    {
        RuleFor(x => x.Details).SetValidator(new TourInputValidator());
        RuleForEach(x => x.Prices).ChildRules(p => p.RuleFor(x => x.Details).SetValidator(new TourPriceInputValidator()));
        RuleFor(x => x.Prices).Must(p => p.Select(x => x.Key).Distinct().Count() == p.Count).WithMessage("Each price needs its own key.");
        RuleFor(x => x.AddedDates.Count).LessThanOrEqualTo(TourSchedule.MaxNewDates).WithName("Dates")
            .WithMessage($"That's too many new dates for one save (the limit is {TourSchedule.MaxNewDates:N0}).");
        RuleForEach(x => x.AddedDates).Must((changes, d) => changes.Prices.Any(p => p.Key == d.PriceKey)).WithMessage("A new date refers to a price that isn't on the tour.");
        RuleForEach(x => x.ChangedDates).Must((changes, d) => changes.Prices.Any(p => p.Key == d.PriceKey)).WithMessage("A date refers to a price that isn't on the tour.");
    }
}

public sealed class CreateTourValidator : AbstractValidator<CreateTour>
{
    public CreateTourValidator() => RuleFor(x => x.Changes).SetValidator(new TourChangesValidator());
}

public sealed class UpdateTourValidator : AbstractValidator<UpdateTour>
{
    public UpdateTourValidator() => RuleFor(x => x.Changes).SetValidator(new TourChangesValidator());
}
