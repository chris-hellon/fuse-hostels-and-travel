using Fuse.Contracts.Identity;
using Fuse.Domain.Identity;

namespace Fuse.Application.Features.Identity;

/// <summary>Row shape returned by the Search* user procedures.</summary>
internal sealed record UserSearchRow
{
    public string Id { get; init; } = "";
    public string? FullName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? UserName { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsActive { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? Nationality { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public int TotalUsers { get; init; }

    public UserSummary ToSummary() => new()
    {
        Id = Id,
        FullName = PersonNames.Display(FirstName, LastName, FullName),
        UserName = UserName,
        Email = PlaceholderEmails.Visible(Email),
        PhoneNumber = PhoneNumber,
        IsActive = IsActive,
        DateOfBirth = DateOfBirth,
        Gender = Gender,
        Nationality = Nationality,
        Address = Address,
        City = City,
    };
}

internal static class UserSearch
{
    public static PagedResult<UserSummary> ToPage(IReadOnlyList<UserSearchRow> rows, PagedQuery query) =>
        new([.. rows.Select(r => r.ToSummary())], rows.Count > 0 ? rows[0].TotalUsers : 0, query.PageNumber, query.PageSize);
}

/// <summary>Staff = anyone holding a back-office role. Uses the tuned SearchStaffPaginated proc.</summary>
internal sealed class SearchStaffHandler(IAppDbContext db, ITenantContext tenant) : IQueryHandler<SearchStaff, PagedResult<UserSummary>>
{
    public async ValueTask<PagedResult<UserSummary>> Handle(SearchStaff query, CancellationToken cancellationToken)
    {
        var rows = await db.QueryProcedureAsync<UserSearchRow>("SearchStaffPaginated", new
        {
            tenant.TenantId,
            Search = query.Keyword,
            query.PageNumber,
            query.PageSize,
            OrderBy = StoredProcedures.ProcedureOrderBy(query.OrderBy, "FullName"),
        }, cancellationToken);

        return UserSearch.ToPage(rows, query);
    }
}

internal sealed class SearchSuppliersHandler(IAppDbContext db, ITenantContext tenant) : IQueryHandler<SearchSuppliers, PagedResult<UserSummary>>
{
    public async ValueTask<PagedResult<UserSummary>> Handle(SearchSuppliers query, CancellationToken cancellationToken)
    {
        var rows = await db.QueryProcedureAsync<UserSearchRow>("SearchUsers", new
        {
            tenant.TenantId,
            Role = FuseRoles.Supplier,
            Search = query.Keyword,
            query.PageNumber,
            query.PageSize,
            OrderBy = StoredProcedures.ProcedureOrderBy(query.OrderBy, "FullName"),
            BookingItemId = (Guid?)null,
        }, cancellationToken);

        return UserSearch.ToPage(rows, query);
    }
}

internal sealed class SearchGuestsHandler(IAppDbContext db, ITenantContext tenant) : IQueryHandler<SearchGuests, PagedResult<UserSummary>>
{
    public async ValueTask<PagedResult<UserSummary>> Handle(SearchGuests query, CancellationToken cancellationToken)
    {
        var rows = await db.QueryProcedureAsync<UserSearchRow>("SearchUsers", new
        {
            tenant.TenantId,
            Role = FuseRoles.Guest,
            Search = query.Keyword,
            query.PageNumber,
            query.PageSize,
            OrderBy = StoredProcedures.ProcedureOrderBy(query.OrderBy, "FullName"),
            BookingItemId = (Guid?)null,
        }, cancellationToken);

        return UserSearch.ToPage(rows, query);
    }
}

internal sealed class FindGuestsHandler(IAppDbContext db, ITenantContext tenant) : IQueryHandler<FindGuests, IReadOnlyList<UserSummary>>
{
    public async ValueTask<IReadOnlyList<UserSummary>> Handle(FindGuests query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Search))
            return [];

        var rows = await db.QueryProcedureAsync<UserSearchRow>("SearchUsersByName", new
        {
            tenant.TenantId,
            Role = FuseRoles.Guest,
            Search = query.Search.Trim(),
        }, cancellationToken);

        return [.. rows.Select(r => r.ToSummary())];
    }
}

internal static class UserProjection
{
    public static IQueryable<UserDetails> ToDetails(this IQueryable<ApplicationUser> users) => users.Select(u => new UserDetails
    {
        Id = u.Id,
        UserName = u.UserName,
        FullName = PersonNames.Display(u.FirstName, u.LastName, u.FullName),
        ImageUrl = u.ImageUrl,
        IsActive = u.IsActive,
        SignUpDate = u.SignUpDate,
        CloudbedsGuestId = u.CloudbedsGuestId,
        HasPassword = u.PasswordHash != null,
        Profile = new UserProfile
        {
            FirstName = u.FirstName ?? "",
            LastName = u.LastName ?? "",
            Email = u.Email != null && u.Email.EndsWith(PlaceholderEmails.Suffix) ? null : u.Email,
            PhoneNumber = u.PhoneNumber,
            DateOfBirth = u.DateOfBirth,
            Gender = u.Gender,
            Nationality = u.Nationality,
            Address = u.Address,
            City = u.City,
            ZipPostalCode = u.ZipPostalCode,
            PassportNumber = u.PassportNumber,
            PassportIssueDate = u.PassportIssueDate,
            PassportExpiryDate = u.PassportExpiryDate,
            PassportIssuingCountry = u.PassportIssuingCountry,
            VisaIssueDate = u.VisaIssueDate,
            VisaExpiryDate = u.VisaExpiryDate,
        },
    });

    public static void Apply(this ApplicationUser user, UserProfile profile)
    {
        user.FirstName = profile.FirstName.Trim();
        user.LastName = profile.LastName.Trim();
        user.SyncFullName();
        // No email: keep (or make) a placeholder, since accounts need one (PlaceholderEmails).
        user.Email = !string.IsNullOrWhiteSpace(profile.Email) ? profile.Email.Trim()
            : PlaceholderEmails.Is(user.Email) ? user.Email
            : PlaceholderEmails.New();
        user.PhoneNumber = profile.PhoneNumber?.Trim();
        user.DateOfBirth = profile.DateOfBirth;
        user.Gender = profile.Gender;
        user.Nationality = profile.Nationality;
        user.Address = profile.Address;
        user.City = profile.City;
        user.ZipPostalCode = profile.ZipPostalCode;
        user.PassportNumber = profile.PassportNumber;
        user.PassportIssueDate = profile.PassportIssueDate;
        user.PassportExpiryDate = profile.PassportExpiryDate;
        user.PassportIssuingCountry = profile.PassportIssuingCountry;
        user.VisaIssueDate = profile.VisaIssueDate;
        user.VisaExpiryDate = profile.VisaExpiryDate;
    }
}
