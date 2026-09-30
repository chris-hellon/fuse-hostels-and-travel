using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Infrastructure.Persistence;

namespace Fuse.Web.Tests;

/// <summary>
/// The public booking API legacy served on /api (token, users, tours, packages, pack incentives, bookings, docs):
/// same routes, statuses, errors and JSON. The field lists are legacy's, recorded from its responses.
/// </summary>
public sealed class PublicApiTests(TestApp app)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] PageFields = ["data", "currentPage", "totalPages", "totalCount", "pageSize", "hasPreviousPage", "hasNextPage"];

    private static readonly string[] BookingListFields =
    [
        "id", "description", "totalAmount", "currencyCode", "itemQuantity", "isPaid", "bookingDate", "guestId", "guestName", "guestEmail",
        "cloudbedsGuestId", "guestNationality", "guestGender", "guestDateOfBirth", "invoiceId", "showDetails", "isLoadingDetails", "concurrencyVersion",
        "additionalNotes", "createdBy", "staffName", "waiverSigned", "bookingSource", "confirmationEmailSent", "refunded", "amountOutstanding",
        "cancelled", "noShow", "partialRefund", "checkedIn", "containsPackage", "packIncentiveId", "items", "payments", "primaryGuest",
    ];

    private static readonly string[] BookingFields =
    [
        "id", "description", "totalAmount", "currencyCode", "itemQuantity", "isPaid", "bookingDate", "guestId", "guestName", "guestEmail",
        "cloudbedsGuestId", "invoiceId", "concurrencyVersion", "stripeSessionId", "additionalNotes", "waiverSigned", "bookingSource",
        "confirmationEmailSent", "refunded", "amountOutstanding", "createdBy", "items", "bookingPackages", "payments", "cancelled", "paymentMethod",
        "packIncentiveId", "packIncentiveTierId",
    ];

    private static readonly string[] BookingItemFields =
    [
        "id", "isCreate", "bookingId", "startDate", "endDate", "createdOn", "amount", "totalAmount", "status", "roomQuantity", "propertyId", "tourId",
        "tourName", "tourDateId", "tourPriceId", "cloudbedsReservationId", "cloudbedsPropertyId", "showDetails", "showGuestDetails", "concurrencyVersion",
        "pickupLocation", "waiverSigned", "tourCategoryId", "bookingPackageId", "noShow", "cancelled", "checkedIn", "refunded", "partialRefund", "tour",
        "tourDate", "tourPrice", "tourCategory", "property", "rooms", "guests",
    ];

    private static readonly string[] TourFields =
    [
        "id", "name", "description", "shortDescription", "price", "priceLabel", "imagePath", "thumbnailImagePath", "maxCapacity", "minCapacity",
        "dayDuration", "nightDuration", "hourDuration", "address", "telephoneNumber", "whatsIncluded", "whatsNotIncluded", "additionalInformation",
        "metaKeywords", "metaDescription", "bookingConfirmationEmailDetails", "termsAndConditions", "cancellationPolicy", "waiverRequired",
        "publishToSite", "friendlyUrl", "supplierEmailText", "showBookingQRCode", "excludeFromStaffSales", "urlSlug", "customSeoScripts",
        "seoPageTitle", "lastModifiedOn", "createdOn", "supplierId", "tourCategoryId", "tourCategory",
    ];

    private static readonly string[] TourDateFields = ["id", "startDate", "tourId", "endDate", "availableSpaces", "tourPriceId", "concurrencyVersion", "tourPrice"];

    private static readonly string[] TourPriceFields =
    [
        "id", "price", "tourId", "title", "description", "monthFrom", "monthTo", "dayDuration", "nightDuration", "hourDuration", "comissionAmount",
        "commissionMinimumPax", "commissionMinimumPaxScope", "publishToWebsite", "inHouseOnly",
    ];

    private static readonly string[] UserFields =
    [
        "id", "userName", "firstName", "lastName", "fullName", "email", "isActive", "emailConfirmed", "phoneNumber", "imageUrl", "dateOfBirth",
        "gender", "nationality", "address", "city", "zipPostalCode", "state", "passportNumber", "passportIssueDate", "passportExpiryDate",
        "passportIssuingCountry", "visaIssueDate", "visaExpiryDate", "cloudbedsGuestId", "totalUsers",
    ];

    private static string[] Fields(JsonElement element) => [.. element.EnumerateObject().Select(p => p.Name)];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<string> GuestIdAsync()
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Email == TestApp.GuestEmail).Select(u => u.Id).SingleAsync(Ct);
    }

    // Token and access ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_client_swaps_its_credentials_for_a_bearer_token()
    {
        var response = await app.Anonymous().PostAsJsonAsync("/api/token", new { clientId = TestApp.ApiClientId, clientSecret = TestApp.ApiClientSecret }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await Json(response);
        Fields(token).ShouldBe(["accessToken", "tokenType", "expiresIn"]);
        token.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        token.GetProperty("expiresIn").GetInt32().ShouldBe(3600);
    }

    [Fact]
    public async Task Wrong_credentials_get_a_bare_401_and_a_guests_client_a_403()
    {
        var wrong = await app.Anonymous().PostAsJsonAsync("/api/token", new { clientId = TestApp.ApiClientId, clientSecret = "nope" }, Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await wrong.Content.ReadAsStringAsync(Ct)).ShouldBeEmpty();

        var guest = await app.Anonymous().PostAsJsonAsync("/api/token", new { clientId = TestApp.GuestApiClientId, clientSecret = TestApp.ApiClientSecret }, Ct);
        guest.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Json(guest)).GetProperty("title").GetString().ShouldBe("Access denied");
    }

    [Fact]
    public async Task Everything_else_needs_a_token()
    {
        (await app.Anonymous().GetAsync("/api/tours/categories", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A signed-in admin's cookie isn't a token.
        var admin = await app.SignedInAsync(TestApp.AdminEmail, staffOnly: true);
        (await admin.GetAsync("/api/tours/categories", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_docs_are_legacys_reference_over_legacys_document()
    {
        var docs = await app.Anonymous().GetAsync("/api/docs", Ct);
        docs.StatusCode.ShouldBe(HttpStatusCode.OK);
        docs.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        (await docs.Content.ReadAsStringAsync(Ct)).ShouldContain("data-url=\"/api/openapi/v1.json\"");
        (await app.Anonymous().GetAsync("/api/docs/index.html", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var spec = await Json(await app.Anonymous().GetAsync("/api/openapi/v1.json", Ct));
        spec.GetProperty("info").GetProperty("title").GetString().ShouldBe("Travaloud Booking API");
        Fields(spec.GetProperty("paths")).Length.ShouldBe(18);
    }

    // Tours ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Tours_are_searched_and_their_departures_listed_a_month_at_a_time()
    {
        var api = await app.PublicApiAsync();
        var start = TestBookings.BusinessNow.Date.AddDays(3).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, await app.AdminApiAsync(), start, Ct);
        var name = await TourNameAsync(tourId);

        (await Json(await api.GetAsync("/api/tours/categories", Ct))).GetArrayLength().ShouldBeGreaterThanOrEqualTo(0);
        var locations = await Json(await api.GetAsync("/api/tours/locations", Ct));
        Fields(locations[0]).ShouldBe(["id", "name"]);

        var search = await Json(await api.PostAsJsonAsync("/api/tours/search", new { name }, Ct));
        Fields(search).ShouldBe(PageFields);
        var tour = search.GetProperty("data").EnumerateArray().Single();
        Fields(tour).ShouldBe(["id", "name", "shortDescription", "thumbnailImagePath", "minCapacity", "maxCapacity", "tourCategoryId", "waiverRequired",
            "publiclyBookable", "locations"]);
        tour.GetProperty("locations").GetArrayLength().ShouldBe(1);

        var options = await Json(await api.GetAsync($"/api/tours/{tourId}", Ct));
        Fields(options).ShouldBe(["id", "name", "shortDescription", "waiverRequired", "dates", "pickupLocations", "locations"]);
        var date = options.GetProperty("dates").EnumerateArray().Single();
        date.GetProperty("id").GetGuid().ShouldBe(departureId);
        date.GetProperty("price").GetDecimal().ShouldBe(25m);
        Fields(options.GetProperty("pickupLocations")[0]).ShouldBe(["propertyId", "propertyName"]);

        // A window past the departure has none; one wider than 31 days is refused.
        (await Json(await api.GetAsync($"/api/tours/{tourId}?from={start.AddDays(1):yyyy-MM-dd}", Ct))).GetProperty("dates").GetArrayLength().ShouldBe(0);
        var wide = await api.GetAsync($"/api/tours/{tourId}?from={start:yyyy-MM-dd}&to={start.AddDays(40):yyyy-MM-dd}", Ct);
        wide.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(wide)).GetProperty("errors").GetProperty("to")[0].GetString()!.ShouldStartWith("The departures date range must be 31 days or less.");
        (await api.GetAsync($"/api/tours/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<string> TourNameAsync(DefaultIdType tourId)
    {
        await using var scope = TestBookings.TenantScope(app);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tours.Where(t => t.Id == tourId).Select(t => t.Name).SingleAsync(Ct);
    }

    // Packages and packs -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_basket_that_isnt_a_package_or_a_pack_is_204()
    {
        var api = await app.PublicApiAsync();

        (await api.PostAsJsonAsync("/api/packages/match", new { tourIds = new[] { Guid.NewGuid() } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await api.PostAsJsonAsync("/api/pack-incentives/match", new { tourIds = new[] { Guid.NewGuid(), Guid.NewGuid() } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Fields(await Json(await api.PostAsJsonAsync("/api/packages/search", new { }, Ct))).ShouldBe(PageFields);
        (await Json(await api.GetAsync("/api/pack-incentives", Ct))).ValueKind.ShouldBe(JsonValueKind.Array);
        (await api.GetAsync($"/api/packages/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Users --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Guests_are_created_found_updated_and_read_in_legacys_shape()
    {
        var api = await app.PublicApiAsync();
        var email = $"api-{Guid.NewGuid():N}@tests.invalid";

        var created = await api.PostAsJsonAsync("/api/users", new { firstName = "Api", lastName = "Guest", email, isGuest = true }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await Json(created)).GetProperty("id").GetString()!;
        created.Headers.Location!.ToString().ShouldBe($"/api/users/{id}");

        var search = await Json(await api.PostAsJsonAsync("/api/users/search", new { keyword = email, role = "Guest" }, Ct));
        Fields(search).ShouldBe(PageFields);
        var found = search.GetProperty("data").EnumerateArray().Single();
        Fields(found).ShouldBe(UserFields);
        found.GetProperty("id").GetString().ShouldBe(id);
        found.GetProperty("totalUsers").GetInt32().ShouldBe(1);

        (await api.PutAsJsonAsync($"/api/users/{id}", new { firstName = "Renamed", lastName = "Guest", email, isGuest = true, state = "Quang Nam" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var user = await Json(await api.GetAsync($"/api/users/{id}", Ct));
        Fields(user).ShouldBe(UserFields);
        (user.GetProperty("firstName").GetString(), user.GetProperty("state").GetString(), user.GetProperty("totalUsers").GetInt32())
            .ShouldBe(("Renamed", "Quang Nam", 0));
        (await api.GetAsync("/api/users/no-such-user", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_need_a_confirmed_password_and_emails_must_be_new()
    {
        var api = await app.PublicApiAsync();

        var weak = await api.PostAsJsonAsync("/api/users", new { firstName = "Staff", lastName = "Member", password = "123", confirmPassword = "124" }, Ct);
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Json(weak)).GetProperty("errors");
        errors.GetProperty("Password")[0].GetString().ShouldBe("The length of 'Password' must be at least 6 characters. You entered 3 characters.");
        errors.GetProperty("ConfirmPassword")[0].GetString().ShouldBe("'Confirm Password' must be equal to 'Password'.");

        var taken = await api.PostAsJsonAsync("/api/users", new { firstName = "A", lastName = "B", email = TestApp.GuestEmail, isGuest = true }, Ct);
        taken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(taken)).GetProperty("errors").GetProperty("Email")[0].GetString().ShouldBe($"Email {TestApp.GuestEmail} is already registered.");
    }

    // Bookings -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_cash_booking_is_paid_at_once_and_reads_back_in_legacys_shape()
    {
        var api = await app.PublicApiAsync();
        var start = TestBookings.BusinessNow.Date.AddDays(5).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, await app.AdminApiAsync(), start, Ct);
        var guestId = await GuestIdAsync();

        var created = await api.PostAsJsonAsync("/api/bookings", new
        {
            guestId,
            paymentMethod = "Cash",
            items = new[] { new { tourId, tourDateId = departureId, guestIds = Array.Empty<string>(), pickupLocation = TestApp.SeedPropertyName } },
        }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await Json(created);
        Fields(body).ShouldBe(["id", "checkoutUrl", "checkoutError"]);
        body.GetProperty("checkoutUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        var id = body.GetProperty("id").GetGuid();

        var booking = await Json(await api.GetAsync($"/api/bookings/{id}", Ct));
        Fields(booking).ShouldBe(BookingFields);
        booking.GetProperty("description").GetString().ShouldBe("App Booking: 1 Tour");
        booking.GetProperty("bookingSource").GetString().ShouldBe("Booking App");
        booking.GetProperty("isPaid").GetBoolean().ShouldBeTrue();
        booking.GetProperty("paymentMethod").GetString().ShouldBe("Cash");
        booking.GetProperty("totalAmount").GetDecimal().ShouldBe(25m);
        var item = booking.GetProperty("items").EnumerateArray().Single();
        Fields(item).ShouldBe(BookingItemFields);
        item.GetProperty("status").GetString().ShouldBe("Paid");
        item.GetProperty("rooms").GetArrayLength().ShouldBe(0);
        Fields(item.GetProperty("tour")).ShouldBe(TourFields);
        Fields(item.GetProperty("tourDate")).ShouldBe(TourDateFields);
        Fields(item.GetProperty("tourPrice")).ShouldBe(TourPriceFields);

        // Found by its tour, with legacy's list shape (items and payments never filled in).
        var search = await Json(await api.PostAsJsonAsync("/api/bookings/search", new { tourId }, Ct));
        var row = search.GetProperty("data").EnumerateArray().Single();
        Fields(row).ShouldBe(BookingListFields);
        (row.GetProperty("id").GetGuid(), row.GetProperty("itemQuantity").GetInt32(), row.GetProperty("items").ValueKind).ShouldBe((id, 0, JsonValueKind.Null));

        // Cash bookings have no card checkout.
        var checkout = await api.PostAsync($"/api/bookings/{id}/stripe-checkout", null, Ct);
        checkout.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(checkout)).GetProperty("detail").GetString().ShouldBe("This booking's payment method is not Stripe.");
    }

    [Fact]
    public async Task A_stripe_booking_comes_back_with_its_checkout_and_flag_as_paid_settles_it()
    {
        var api = await app.PublicApiAsync();
        var start = TestBookings.BusinessNow.Date.AddDays(6).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, await app.AdminApiAsync(), start, Ct);

        var created = await Json(await api.PostAsJsonAsync("/api/bookings", new
        {
            guestId = await GuestIdAsync(),
            paymentMethod = "Stripe",
            isDevelopment = true,
            successUrl = "https://app.example/paid",
            items = new[] { new { tourId, tourDateId = departureId } },
        }, Ct));
        var id = created.GetProperty("id").GetGuid();
        var url = created.GetProperty("checkoutUrl").GetString()!;
        var session = app.Payments.Sessions[url.Split('/')[^1]];
        (session.Request.TestMode, session.Request.SuccessUrl, session.Request.BookingId).ShouldBe((true, "https://app.example/paid", id));
        // Legacy's checkout: a line per tour at its total, legacy's description and metadata; test bookings skip the customer lookup.
        var name = await TourNameAsync(tourId);
        var line = session.Request.Lines.ShouldHaveSingleItem();
        (line.Name, line.UnitAmount, line.Quantity).ShouldBe((name, 25m, 1));
        line.Description.ShouldBe($"1 guest at {name} on {start.ToString("D", System.Globalization.CultureInfo.GetCultureInfo("en-US"))}");
        session.Request.Description.ShouldEndWith($"{name} - Booking: {(await Json(await api.GetAsync($"/api/bookings/{id}", Ct))).GetProperty("invoiceId").GetInt32()}");
        session.Request.Metadata["Tours"].ShouldBe(name);
        session.Request.FindExistingCustomer.ShouldBeFalse();

        var booking = await Json(await api.GetAsync($"/api/bookings/{id}", Ct));
        booking.GetProperty("description").GetString().ShouldBe("App Booking: 1 Tour [TEST]");
        booking.GetProperty("additionalNotes").GetString()!.ShouldStartWith("*** TEST / DEVELOPMENT BOOKING — DO NOT ACTION ***");
        booking.GetProperty("isPaid").GetBoolean().ShouldBeFalse();

        var flagged = await api.PostAsJsonAsync($"/api/bookings/{id}/flag-as-paid", new { stripeSessionId = "cs_test_manual" }, Ct);
        flagged.StatusCode.ShouldBe(HttpStatusCode.OK);
        Fields(await Json(flagged)).ShouldBe(["id"]);
        var paid = await Json(await api.GetAsync($"/api/bookings/{id}", Ct));
        (paid.GetProperty("isPaid").GetBoolean(), paid.GetProperty("amountOutstanding").GetDecimal(), paid.GetProperty("stripeSessionId").GetString())
            .ShouldBe((true, 0m, "cs_test_manual"));
    }

    [Fact]
    public async Task Booking_mistakes_get_legacys_400s_and_a_full_departure_a_409()
    {
        var api = await app.PublicApiAsync();
        var start = TestBookings.BusinessNow.Date.AddDays(7).AddHours(9);
        var (tourId, departureId) = await TestBookings.TourWithDepartureAsync(app, await app.AdminApiAsync(), start, Ct);

        var empty = await api.PostAsJsonAsync("/api/bookings", new { paymentMethod = "Cash" }, Ct);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Json(empty)).GetProperty("errors");
        errors.GetProperty("GuestId")[0].GetString().ShouldBe("A guest is required. Find or create the guest first, then send their id.");
        errors.GetProperty("Items")[0].GetString().ShouldBe("At least one tour item or package is required.");

        var nobody = await api.PostAsJsonAsync("/api/bookings", new { guestId = "no-such-guest", items = new[] { new { tourId, tourDateId = departureId } } }, Ct);
        (await Json(nobody)).GetProperty("errors").GetProperty("GuestId")[0].GetString().ShouldBe("No guest was found with id 'no-such-guest'.");

        var wrongDate = await api.PostAsJsonAsync("/api/bookings", new { guestId = await GuestIdAsync(), items = new[] { new { tourId, tourDateId = Guid.NewGuid() } } }, Ct);
        (await Json(wrongDate)).GetProperty("errors").GetProperty("Items[0].TourDateId")[0].GetString()
            .ShouldBe("The tour date was not found, is in the past, or does not belong to this tour.");

        // Two spaces left; the booker and two travellers want three.
        const int spaces = 2;
        await using (var scope = TestBookings.TenantScope(app))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().TourDates.Where(d => d.Id == departureId)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.AvailableSpaces, spaces), Ct);
        var travellers = await CreateGuestsAsync(api, spaces);
        var full = await api.PostAsJsonAsync("/api/bookings", new
        {
            guestId = await GuestIdAsync(),
            paymentMethod = "Cash",
            items = new[] { new { tourId, tourDateId = departureId, guestIds = travellers } },
        }, Ct);
        full.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await Json(full);
        problem.GetProperty("title").GetString().ShouldBe("Insufficient availability");
        problem.GetProperty("detail").GetString().ShouldBe($"{start:dd/MM/yyyy}: {spaces + 1} guest(s) requested but only {spaces} space(s) remain.");
    }

    private async Task<string[]> CreateGuestsAsync(HttpClient api, int count)
    {
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var created = await api.PostAsJsonAsync("/api/users", new { firstName = "Traveller", lastName = $"{i}", isGuest = true }, Ct);
            ids.Add((await Json(created)).GetProperty("id").GetString()!);
        }
        return [.. ids];
    }

    [Fact]
    public async Task Booking_searches_span_a_month_at_most()
    {
        var api = await app.PublicApiAsync();

        var wide = await api.PostAsJsonAsync("/api/bookings/search", new { bookingStartDate = "2026-01-01", bookingEndDate = "2026-03-01" }, Ct);
        wide.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(wide)).GetProperty("errors").GetProperty("BookingEndDate")[0].GetString()
            .ShouldBe("The booking date range must be 31 days or less. You requested 59 days (2026-01-01 to 2026-03-01). Please narrow the range.");
        (await Json(await api.PostAsJsonAsync("/api/bookings/search", new { cloudbedsGuestId = "nobody-has-this" }, Ct)))
            .GetProperty("totalCount").GetInt32().ShouldBe(0);
        (await api.GetAsync($"/api/bookings/{Guid.NewGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stripe_can_still_call_legacys_webhook_address()
    {
        // Mapped (the fake gateway has no webhook secret configured, so it answers as the new address does).
        var legacy = await app.Anonymous().PostAsync("/webhooks/stripe-payment", new StringContent("{}"), Ct);
        var current = await app.Anonymous().PostAsync("/api/stripe/webhook", new StringContent("{}"), Ct);
        legacy.StatusCode.ShouldBe(current.StatusCode);
    }
}
