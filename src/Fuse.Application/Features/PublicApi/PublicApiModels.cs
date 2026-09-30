using Fuse.Domain.Catalog.Tours;

namespace Fuse.Application.Features.PublicApi;

// The legacy public booking API's request and response models (legacy Admin/Api/Models and the legacy DTOs it
// returned as-is), kept field for field and in the same order so the JSON its callers get is unchanged. The
// OpenAPI document served at /api/openapi/v1.json is legacy's own and describes these.

// Paging -----------------------------------------------------------------------------------------------------

/// <summary>Legacy PaginationResponse: a page of results and where it sits.</summary>
public sealed class PaginationResponse<T>
{
    public PaginationResponse(List<T> data, int count, int page, int pageSize)
    {
        Data = data;
        CurrentPage = page;
        PageSize = pageSize;
        TotalPages = (int)Math.Ceiling(count / (double)pageSize);
        TotalCount = count;
    }

    public List<T> Data { get; }
    public int CurrentPage { get; }
    public int TotalPages { get; }
    public int TotalCount { get; }
    public int PageSize { get; }
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
}

// Users --------------------------------------------------------------------------------------------------------

public sealed class SearchUsersApiRequest
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Keyword { get; set; }
    public string? Role { get; set; }
    public string[]? OrderBy { get; set; }
}

public sealed class CreateUserApiRequest
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string Password { get; set; } = null!;
    public string ConfirmPassword { get; set; } = null!;
    public bool IsGuest { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Nationality { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ZipPostalCode { get; set; }
    public string? State { get; set; }
}

public sealed class UpdateUserApiRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsGuest { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Nationality { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ZipPostalCode { get; set; }
    public string? State { get; set; }
}

public sealed class CreateUserResponse
{
    public string? Id { get; set; }
}

/// <summary>Legacy UserDetailsDto.</summary>
public sealed class UserDetailsDto
{
    public string Id { get; set; } = null!;
    public string? UserName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Nationality { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ZipPostalCode { get; set; }
    public string? State { get; set; }
    public string? PassportNumber { get; set; }
    public DateTime? PassportIssueDate { get; set; }
    public DateTime? PassportExpiryDate { get; set; }
    public string? PassportIssuingCountry { get; set; }
    public DateTime? VisaIssueDate { get; set; }
    public DateTime? VisaExpiryDate { get; set; }
    public string? CloudbedsGuestId { get; set; }
    public int TotalUsers { get; set; }
}

// Tours --------------------------------------------------------------------------------------------------------

public sealed class SearchToursApiRequest
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Name { get; set; }
    public Guid? TourCategoryId { get; set; }
    public Guid? LocationId { get; set; }
}

public sealed class TourCategoryApiDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
}

public sealed class TourSearchResultDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public string? ThumbnailImagePath { get; set; }
    public int? MinCapacity { get; set; }
    public int? MaxCapacity { get; set; }
    public Guid? TourCategoryId { get; set; }
    public bool WaiverRequired { get; set; }
    public bool PubliclyBookable { get; set; }
    public List<TourLocationApiDto> Locations { get; set; } = [];
}

public sealed class TourLocationApiDto
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
}

public sealed class TourBookingOptionsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public bool WaiverRequired { get; set; }
    public List<TourDateOptionDto> Dates { get; set; } = [];
    public List<TourPickupLocationOptionDto> PickupLocations { get; set; } = [];
    public List<TourLocationApiDto> Locations { get; set; } = [];
}

public sealed class TourDateOptionDto
{
    public Guid Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int AvailableSpaces { get; set; }
    public decimal? Price { get; set; }
    public string? PriceTitle { get; set; }
}

public sealed class TourPickupLocationOptionDto
{
    public Guid PropertyId { get; set; }
    public string? PropertyName { get; set; }
}

// Packages -----------------------------------------------------------------------------------------------------

public sealed class SearchPackagesApiRequest
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Name { get; set; }
    public Guid? TourCategoryId { get; set; }
}

public sealed class MatchPackageApiRequest
{
    public List<Guid> TourIds { get; set; } = [];
}

public sealed class PackageSearchResultDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public string? ThumbnailImagePath { get; set; }
    public string? PriceLabel { get; set; }
    public Guid? TourCategoryId { get; set; }
    public string? TourCategoryName { get; set; }
    public decimal? PriceFrom { get; set; }
    public decimal? PriceTo { get; set; }
    public List<PackageTierApiDto> Tiers { get; set; } = [];
}

public sealed class PackageDetailsApiDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? ImagePath { get; set; }
    public string? PriceLabel { get; set; }
    public Guid? TourCategoryId { get; set; }
    public string? TourCategoryName { get; set; }
    public List<PackageTierApiDto> Tiers { get; set; } = [];
    public List<PackageTourApiDto> Tours { get; set; } = [];
}

public sealed class PackageTierApiDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public decimal Total { get; set; }
}

public sealed class PackageTourApiDto
{
    public Guid TourId { get; set; }
    public string? TourName { get; set; }
    public int Quantity { get; set; }
    public List<PackageTourTierPriceApiDto> Prices { get; set; } = [];
}

public sealed class PackageTourTierPriceApiDto
{
    public Guid TierId { get; set; }
    public decimal Price { get; set; }
}

// Pack incentives ----------------------------------------------------------------------------------------------

public sealed class PackIncentiveApiDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public int MinimumProductCount { get; set; }
    public decimal CommissionAmount { get; set; }
    public bool IgnoresMinimumPax { get; set; }
    public List<PackIncentiveTourApiDto> Tours { get; set; } = [];
    public List<PackIncentiveTierApiDto> Tiers { get; set; } = [];
}

public sealed class MatchPackIncentiveApiRequest
{
    public List<Guid> TourIds { get; set; } = [];
}

public sealed class PackIncentiveTourApiDto
{
    public Guid TourId { get; set; }
    public string? TourName { get; set; }
}

public sealed class PackIncentiveTierApiDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public decimal Price { get; set; }
}

// Bookings: requests ---------------------------------------------------------------------------------------------

public sealed class SearchBookingsApiRequest
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Keyword { get; set; }
    public string? Description { get; set; }
    public DateTime? BookingStartDate { get; set; }
    public DateTime? BookingEndDate { get; set; }
    public DateTime? TourStartDate { get; set; }
    public DateTime? TourEndDate { get; set; }
    public Guid? TourId { get; set; }
    public string? CloudbedsGuestId { get; set; }
    public string[]? OrderBy { get; set; }
}

public sealed class CreateBookingApiRequest
{
    public string? GuestId { get; set; }
    public BookingPaymentMethod PaymentMethod { get; set; } = BookingPaymentMethod.Stripe;
    public string? AdditionalNotes { get; set; }
    public Guid? PackIncentiveId { get; set; }
    public Guid? PackIncentiveTierId { get; set; }
    public List<CreateBookingItemApiRequest> Items { get; set; } = [];
    public List<BookingPackageApiRequest> Packages { get; set; } = [];
    public bool IsDevelopment { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}

public sealed class CreateBookingItemApiRequest
{
    public Guid? TourId { get; set; }
    public Guid? TourDateId { get; set; }
    public List<string>? GuestIds { get; set; }
    public string? PickupLocation { get; set; }
    public string? OtherPickupLocation { get; set; }
    public bool WaiverSigned { get; set; }
}

public sealed class BookingPackageApiRequest
{
    public Guid? PackageId { get; set; }
    public Guid? TierId { get; set; }
    public List<string>? GuestIds { get; set; }
    public List<BookPackageTourApiRequest> Tours { get; set; } = [];
}

public sealed class BookPackageTourApiRequest
{
    public Guid? TourId { get; set; }
    public Guid? TourDateId { get; set; }
    public string? PickupLocation { get; set; }
    public string? OtherPickupLocation { get; set; }
    public bool WaiverSigned { get; set; }
}

public sealed class FlagBookingAsPaidApiRequest
{
    public string? StripeSessionId { get; set; }
}

public sealed class CreateBookingResponse
{
    public Guid Id { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? CheckoutError { get; set; }
}

public sealed class BookingIdResponse
{
    public Guid Id { get; set; }
}

public sealed class StripeCheckoutResponse
{
    public string? Url { get; set; }
}

// Bookings: legacy DTOs returned as they were ----------------------------------------------------------------

/// <summary>Legacy BookingListDto (the search's rows). Items, payments and the primary guest were never filled in.</summary>
public sealed class BookingListDto
{
    public DefaultIdType Id { get; set; }
    public string Description { get; set; } = null!;
    public decimal TotalAmount { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public int ItemQuantity => 0; // legacy: the count of Items, which the search never loaded
    public bool IsPaid { get; set; }
    public DateTime BookingDate { get; set; }
    public string? GuestId { get; set; }
    public string? GuestName { get; set; }
    public string? GuestEmail { get; set; }
    public string? CloudbedsGuestId { get; set; }
    public string? GuestNationality { get; set; }
    public string? GuestGender { get; set; }
    public DateTime? GuestDateOfBirth { get; set; }
    public int InvoiceId { get; set; }
    public bool ShowDetails { get; set; }
    public bool IsLoadingDetails { get; set; }
    public int ConcurrencyVersion { get; set; }
    public string? AdditionalNotes { get; set; }
    public DefaultIdType CreatedBy { get; set; }
    public string? StaffName { get; set; }
    public bool? WaiverSigned { get; set; }
    public string? BookingSource { get; set; }
    public bool? ConfirmationEmailSent { get; set; }
    public bool? Refunded { get; set; }
    public decimal? AmountOutstanding { get; set; }
    public bool? Cancelled { get; set; }
    public bool? NoShow { get; set; }
    public bool? PartialRefund { get; set; }
    public bool? CheckedIn { get; set; }
    public bool ContainsPackage { get; set; }
    public DefaultIdType? PackIncentiveId { get; set; }
    public object? Items { get; set; }
    public object? Payments { get; set; }
    public object? PrimaryGuest { get; set; }
}

/// <summary>Legacy BookingDetailsDto.</summary>
public sealed class BookingDetailsDto
{
    public DefaultIdType Id { get; set; }
    public string Description { get; set; } = null!;
    public decimal TotalAmount { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public int ItemQuantity { get; set; }
    public bool IsPaid { get; set; }
    public DateTime BookingDate { get; set; }
    public string? GuestId { get; set; }
    public string? GuestName { get; set; }
    public string? GuestEmail { get; set; }
    public string? CloudbedsGuestId { get; set; }
    public int InvoiceId { get; set; }
    public int ConcurrencyVersion { get; set; }
    public string? StripeSessionId { get; set; }
    public string? AdditionalNotes { get; set; }
    public bool? WaiverSigned { get; set; }
    public string? BookingSource { get; set; }
    public bool? ConfirmationEmailSent { get; set; }
    public bool? Refunded { get; set; }
    public decimal? AmountOutstanding { get; set; }
    public DefaultIdType CreatedBy { get; set; }
    public List<BookingItemDetailsDto>? Items { get; set; }
    public List<BookingPackageDto>? BookingPackages { get; set; }
    public List<BookingPaymentDto>? Payments { get; set; }
    public bool? Cancelled { get; set; }
    /// <summary>Legacy: the first payment's method, else the default (Stripe).</summary>
    public BookingPaymentMethod PaymentMethod { get; set; }
    public DefaultIdType? PackIncentiveId { get; set; }
    public DefaultIdType? PackIncentiveTierId { get; set; }
}

public sealed class BookingItemDetailsDto
{
    public DefaultIdType Id { get; set; }
    public bool IsCreate { get; set; }
    public DefaultIdType BookingId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedOn { get; set; }
    public decimal Amount { get; set; }
    public decimal TotalAmount => Guests is null ? Amount : Amount * (Guests.Count + 1);
    public string Status =>
        Cancelled == true ? "Cancelled"
        : NoShow == true ? "No Show"
        : CheckedIn == true ? "Checked In"
        : Refunded == true ? "Refunded"
        : PartialRefund == true ? "Partial Refund"
        : "Paid";
    public int? RoomQuantity { get; set; }
    public DefaultIdType? PropertyId { get; set; }
    public DefaultIdType? TourId { get; set; }
    public string? TourName { get; set; }
    public DefaultIdType? TourDateId { get; set; }
    public DefaultIdType? TourPriceId { get; set; }
    public string? CloudbedsReservationId { get; set; }
    public int? CloudbedsPropertyId { get; set; }
    public bool ShowDetails { get; set; }
    public bool ShowGuestDetails { get; set; }
    public int ConcurrencyVersion { get; set; }
    public string? PickupLocation { get; set; }
    public bool? WaiverSigned { get; set; }
    public DefaultIdType? TourCategoryId { get; set; }
    public DefaultIdType? BookingPackageId { get; set; }
    public bool? NoShow { get; set; }
    public bool? Cancelled { get; set; }
    public bool? CheckedIn { get; set; }
    public bool? Refunded { get; set; }
    public bool? PartialRefund { get; set; }
    public TourDto? Tour { get; set; }
    public TourDateDto? TourDate { get; set; }
    public TourPriceDto? TourPrice { get; set; }
    public TourCategoryDto? TourCategory { get; set; }
    /// <summary>Hostel bookings are retired: always null.</summary>
    public object? Property { get; set; }
    /// <summary>Hostel bookings are retired: always empty.</summary>
    public List<object> Rooms { get; set; } = [];
    public List<BookingItemGuestDto>? Guests { get; set; }
}

public sealed class BookingItemGuestDto
{
    public DefaultIdType Id { get; set; }
    public DefaultIdType BookingItemId { get; set; }
    public DefaultIdType GuestId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? CloudbedsGuestId { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Nationality { get; set; }
    public string? Gender { get; set; }
    public bool? NoShow { get; set; }
    public bool? Cancelled { get; set; }
    public bool? CheckedIn { get; set; }
    public string Status =>
        Cancelled == true ? "Cancelled"
        : NoShow == true ? "No Show"
        : CheckedIn == true ? "Checked In"
        : "Paid";
}

public sealed class BookingPaymentDto
{
    public DefaultIdType Id { get; set; }
    public DefaultIdType BookingId { get; set; }
    public int BookingInvoiceId { get; set; }
    public decimal Amount { get; set; }
    public bool Refunded { get; set; }
    public string? StripeSessionId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? Currency { get; set; }
    public DateTime? PaymentDate { get; set; }
    public BookingPaymentMethod? PaymentMethod { get; set; }
    public int TotalCount { get; set; }
}

public sealed class BookingPackageDto
{
    public DefaultIdType Id { get; set; }
    public DefaultIdType BookingId { get; set; }
    public DefaultIdType PackageId { get; set; }
    public string PackageName { get; set; } = null!;
    public decimal PackagePrice { get; set; }
    public string? TierTitle { get; set; }
    public decimal? CommissionAmount { get; set; }
    public bool? CommissionIgnoresMinimumPax { get; set; }
}

public sealed class TourDto
{
    public DefaultIdType Id { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public decimal? Price { get; set; }
    public string? PriceLabel { get; set; }
    public string? ImagePath { get; set; }
    public string? ThumbnailImagePath { get; set; }
    public int? MaxCapacity { get; set; }
    public int? MinCapacity { get; set; }
    public string? DayDuration { get; set; }
    public string? NightDuration { get; set; }
    public string? HourDuration { get; set; }
    public string? Address { get; set; }
    public string? TelephoneNumber { get; set; }
    public string? WhatsIncluded { get; set; }
    public string? WhatsNotIncluded { get; set; }
    public string? AdditionalInformation { get; set; }
    public string? MetaKeywords { get; set; }
    public string? MetaDescription { get; set; }
    public string? BookingConfirmationEmailDetails { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? CancellationPolicy { get; set; }
    public bool? WaiverRequired { get; set; }
    public bool? PublishToSite { get; set; }
    public string FriendlyUrl => !string.IsNullOrEmpty(UrlSlug) ? UrlSlug : Fuse.Shared.Text.Slugs.From(Name);
    public string? SupplierEmailText { get; set; }
    public bool? ShowBookingQRCode { get; set; }
    public bool? ExcludeFromStaffSales { get; set; }
    public string? UrlSlug { get; set; }
    public string? CustomSeoScripts { get; set; }
    public string? SeoPageTitle { get; set; }
    public DateTime? LastModifiedOn { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? SupplierId { get; set; }
    public DefaultIdType? TourCategoryId { get; set; }
    public TourCategoryDto? TourCategory { get; set; }
}

public sealed class TourCategoryDto
{
    public DefaultIdType Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconClass { get; set; }
    public string? ShortDescription { get; set; }
    public string? ImagePath { get; set; }
    public string? ThumbnailImagePath { get; set; }
    public string? MetaKeywords { get; set; }
    public string? MetaDescription { get; set; }
    public bool? TopLevelCategory { get; set; }
    public DefaultIdType? ParentTourCategoryId { get; set; }
    public object? ParentTourCategories { get; set; }
}

public sealed class TourDateDto
{
    public DefaultIdType Id { get; set; }
    public DateTime StartDate { get; set; }
    public DefaultIdType TourId { get; set; }
    public DateTime EndDate { get; set; }
    public int AvailableSpaces { get; set; }
    public DefaultIdType? TourPriceId { get; set; }
    public int ConcurrencyVersion { get; set; }
    public TourPriceDto? TourPrice { get; set; }
}

public sealed class TourPriceDto
{
    public DefaultIdType Id { get; set; }
    public decimal Price { get; set; }
    public DefaultIdType TourId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? MonthFrom { get; set; }
    public string? MonthTo { get; set; }
    public decimal? DayDuration { get; set; }
    public decimal? NightDuration { get; set; }
    public decimal? HourDuration { get; set; }
    public decimal? ComissionAmount { get; set; }
    public int? CommissionMinimumPax { get; set; }
    public CommissionMinimumPaxScope? CommissionMinimumPaxScope { get; set; }
    public bool? PublishToWebsite { get; set; }
    public bool? InHouseOnly { get; set; }
}
