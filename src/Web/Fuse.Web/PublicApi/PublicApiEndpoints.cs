using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Fuse.Application.Features.PublicApi;
using Fuse.Contracts.Common;
using Fuse.Shared.Enums;

namespace Fuse.Web.PublicApi;

// Legacy's endpoints (Admin/Api/*Endpoints.cs): the same routes, status codes and error bodies. The work is done by
// the Application's PublicApi requests.

internal static class PublicApiEndpoints
{
    public static void MapTokenEndpoint(this IEndpointRouteBuilder api) =>
        api.MapPost("token", async ([FromBody] TokenRequest request, ApiTokenService tokens) =>
        {
            var (status, token) = await tokens.IssueAsync(request);
            return status switch
            {
                TokenStatus.Success => Results.Ok(token),
                TokenStatus.Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Access denied",
                    detail: "This client's service account is not permitted to use the API."),
                // A bare 401: never reveal which part was wrong.
                _ => Results.Unauthorized(),
            };
        }).AllowAnonymous();

    public static void MapPublicUsers(this IEndpointRouteBuilder api)
    {
        var users = api.MapGroup("users");

        users.MapPost("/search", async ([FromBody] SearchUsersApiRequest request, ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new SearchApiUsers(request), cancellationToken)));

        users.MapGet("/{id}", async (string id, ISender sender, CancellationToken cancellationToken) =>
            await sender.Send(new GetApiUser(id), cancellationToken) is { } user ? Results.Ok(user) : Results.NotFound());

        users.MapPost("/", ([FromBody] CreateUserApiRequest request, ISender sender, CancellationToken cancellationToken) =>
            ApiResults.Run(async () =>
            {
                var id = await sender.Send(new CreateApiUser(request), cancellationToken);
                return Results.Created($"/api/users/{id}", new CreateUserResponse { Id = id });
            }, failedTitle: "The user could not be created."));

        users.MapPut("/{id}", (string id, [FromBody] UpdateUserApiRequest request, ISender sender, CancellationToken cancellationToken) =>
            ApiResults.Run(async () =>
            {
                await sender.Send(new UpdateApiUser(id, request), cancellationToken);
                return Results.NoContent();
            }, failedTitle: "The user could not be updated."));
    }

    public static void MapPublicTours(this IEndpointRouteBuilder api)
    {
        var tours = api.MapGroup("tours");

        tours.MapGet("/categories", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new ListApiTourCategories(), cancellationToken)));

        tours.MapGet("/locations", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new ListApiTourLocations(), cancellationToken)));

        tours.MapPost("/search", async ([FromBody] SearchToursApiRequest request, ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new SearchApiTours(request), cancellationToken)));

        tours.MapGet("/{id:guid}", async (Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? minSpaces, ISender sender,
            CancellationToken cancellationToken) =>
        {
            // The departures window defaults to the next month and may span at most 31 days.
            var fromDate = from ?? DateTime.Today;
            var toDate = to ?? fromDate.AddMonths(1);
            if (ApiDateRange.Validate(fromDate, toDate, "from", "to", "departures") is { } errors)
                return Results.ValidationProblem(errors);
            return await sender.Send(new GetApiTourBookingOptions(id, fromDate, toDate, minSpaces), cancellationToken) is { } tour
                ? Results.Ok(tour)
                : Results.NotFound();
        });
    }

    public static void MapPublicPackages(this IEndpointRouteBuilder api)
    {
        var packages = api.MapGroup("packages");

        packages.MapPost("/search", async ([FromBody] SearchPackagesApiRequest request, ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new SearchApiPackages(request), cancellationToken)));

        packages.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            await sender.Send(new GetApiPackage(id), cancellationToken) is { } package ? Results.Ok(package) : Results.NotFound());

        packages.MapPost("/match", async ([FromBody] MatchPackageApiRequest request, ISender sender, CancellationToken cancellationToken) =>
            await sender.Send(new MatchApiPackage(request.TourIds ?? []), cancellationToken) is { } package ? Results.Ok(package) : Results.NoContent());
    }

    public static void MapPublicPackIncentives(this IEndpointRouteBuilder api)
    {
        var packs = api.MapGroup("pack-incentives");

        packs.MapGet("/", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new ListApiPackIncentives(), cancellationToken)));

        packs.MapPost("/match", async ([FromBody] MatchPackIncentiveApiRequest request, ISender sender, CancellationToken cancellationToken) =>
            await sender.Send(new MatchApiPackIncentive(request.TourIds ?? []), cancellationToken) is { } pack ? Results.Ok(pack) : Results.NoContent());
    }

    public static void MapPublicBookings(this IEndpointRouteBuilder api)
    {
        var bookings = api.MapGroup("bookings");

        bookings.MapPost("/search", async ([FromBody] SearchBookingsApiRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            // Each date range may span at most a month.
            var errors = new Dictionary<string, List<string>>();
            void Add(string key, string message)
            {
                if (!errors.TryGetValue(key, out var list))
                    errors[key] = list = [];
                list.Add(message);
            }
            ApiDateRange.Validate(request.BookingStartDate, request.BookingEndDate, "BookingStartDate", "BookingEndDate", "booking", Add);
            ApiDateRange.Validate(request.TourStartDate, request.TourEndDate, "TourStartDate", "TourEndDate", "tour departure", Add);
            if (errors.Count > 0)
                return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
            return Results.Ok(await sender.Send(new SearchApiBookings(request), cancellationToken));
        });

        bookings.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            await sender.Send(new GetApiBooking(id), cancellationToken) is { } booking ? Results.Ok(booking) : Results.NotFound());

        bookings.MapPost("/", ([FromBody] CreateBookingApiRequest request, ClaimsPrincipal user, IOptions<ApiSettings> settings, ISender sender,
            CancellationToken cancellationToken) => ApiResults.Run(async () =>
        {
            // Credited to the calling client's service user; its test bookings to its dev user, when it has one.
            var staffUserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            if (request.IsDevelopment && settings.Value.Clients.FirstOrDefault(c => c.UserId == staffUserId)?.DevUserId is { Length: > 0 } devUserId)
                staffUserId = devUserId;

            var id = await sender.Send(new CreateApiBooking(request, DefaultIdType.TryParse(staffUserId, out var staff) ? staff : DefaultIdType.Empty),
                cancellationToken);

            // Stripe bookings come back with their checkout page; if it can't be made, the booking still stands.
            string? checkoutUrl = null, checkoutError = null;
            if (request.PaymentMethod == BookingPaymentMethod.Stripe)
            {
                try
                {
                    var checkout = await sender.Send(new CreateApiCheckout(id, request.IsDevelopment, request.SuccessUrl, request.CancelUrl), cancellationToken);
                    checkoutUrl = checkout.Url;
                    checkoutError = checkout.Error == ApiCheckoutError.None ? null : ApiResults.Describe(checkout.Error);
                }
                catch (AppException)
                {
                    checkoutError = ApiResults.Describe(ApiCheckoutError.None);
                }
            }
            return Results.Created($"/api/bookings/{id}", new CreateBookingResponse { Id = id, CheckoutUrl = checkoutUrl, CheckoutError = checkoutError });
        }, failedTitle: "Booking could not be created"));

        bookings.MapPost("/{id:guid}/flag-as-paid", (Guid id, [FromBody] FlagBookingAsPaidApiRequest? request, ISender sender,
            CancellationToken cancellationToken) => ApiResults.Run(async () =>
        {
            await sender.Send(new FlagApiBookingAsPaid(id, request?.StripeSessionId), cancellationToken);
            return Results.Ok(new BookingIdResponse { Id = id });
        }, failedTitle: "The booking could not be marked as paid."));

        bookings.MapPost("/{id:guid}/stripe-checkout", (Guid id, ISender sender, CancellationToken cancellationToken) => ApiResults.Run(async () =>
        {
            var checkout = await sender.Send(new CreateApiCheckout(id), cancellationToken);
            return checkout.Error switch
            {
                ApiCheckoutError.None => Results.Ok(new StripeCheckoutResponse { Url = checkout.Url }),
                ApiCheckoutError.BookingNotFound => Results.NotFound(),
                _ => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Checkout unavailable", detail: ApiResults.Describe(checkout.Error)),
            };
        }, failedTitle: "Checkout unavailable", businessRuleStatus: StatusCodes.Status409Conflict));
    }
}

internal static class ApiResults
{
    /// <summary>The app's exceptions as legacy's responses.</summary>
    public static async Task<IResult> Run(Func<Task<IResult>> action, string failedTitle, int businessRuleStatus = StatusCodes.Status400BadRequest)
    {
        try
        {
            return await action();
        }
        catch (RequestValidationException validation)
        {
            return Results.ValidationProblem(validation.Errors.ToDictionary(e => e.Key, e => e.Value));
        }
        catch (NotFoundException)
        {
            return Results.NotFound();
        }
        catch (ConflictException conflict)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Insufficient availability", detail: conflict.Message);
        }
        catch (BusinessRuleException rule)
        {
            return Results.Problem(statusCode: businessRuleStatus, title: failedTitle, detail: rule.Message);
        }
    }

    public static string Describe(ApiCheckoutError error) => error switch
    {
        ApiCheckoutError.NotStripe => "This booking's payment method is not Stripe.",
        ApiCheckoutError.MissingEmail => "This booking has no guest email to send the Stripe receipt to.",
        ApiCheckoutError.BookingNotFound => "The booking was not found.",
        _ => "A Stripe Checkout URL could not be generated.",
    };
}

/// <summary>Legacy ApiDateRange: a date filter may span at most a month (31 days).</summary>
internal static class ApiDateRange
{
    public const int MaxRangeDays = 31;

    public static void Validate(DateTime? start, DateTime? end, string startField, string endField, string rangeLabel, Action<string, string> addError)
    {
        if (!start.HasValue || !end.HasValue)
            return;
        if (end.Value.Date < start.Value.Date)
        {
            addError(endField, $"The {rangeLabel} end date must be on or after the start date.");
            return;
        }
        var days = (end.Value.Date - start.Value.Date).TotalDays;
        if (days > MaxRangeDays)
            addError(endField,
                $"The {rangeLabel} date range must be {MaxRangeDays} days or less. You requested " +
                $"{days:0} days ({start.Value:yyyy-MM-dd} to {end.Value:yyyy-MM-dd}). Please narrow the range.");
    }

    public static Dictionary<string, string[]>? Validate(DateTime? start, DateTime? end, string startField, string endField, string rangeLabel)
    {
        var errors = new Dictionary<string, List<string>>();
        Validate(start, end, startField, endField, rangeLabel, (key, message) =>
        {
            if (!errors.TryGetValue(key, out var list))
                errors[key] = list = [];
            list.Add(message);
        });
        return errors.Count == 0 ? null : errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }
}
