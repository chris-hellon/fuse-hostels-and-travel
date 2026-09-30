using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuse.Application.Common.Cloudbeds;

namespace Fuse.Infrastructure.Cloudbeds;

/// <summary>Cloudbeds API v1.1 over a typed HttpClient (base address set at registration). GETs only.</summary>
internal sealed class CloudbedsClient(HttpClient http) : ICloudbedsClient
{
    public async Task<CloudbedsGuest?> GetGuestAsync(string propertyId, string apiKey, string guestId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"getGuest?propertyID={Uri.EscapeDataString(propertyId)}&guestID={Uri.EscapeDataString(guestId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            return null;
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<Envelope>(cancellationToken);
        if (body is not { Success: true, Data: { } data })
            return null;

        return new CloudbedsGuest
        {
            FirstName = data.FirstName,
            LastName = data.LastName,
            Gender = data.Gender,
            Phone = data.Phone,
            Country = data.Country,
            BirthDate = data.BirthDate,
            DocumentType = data.DocumentType,
            DocumentNumber = data.DocumentNumber,
            DocumentIssueDate = data.DocumentIssueDate,
            DocumentIssuingCountry = data.DocumentIssuingCountry,
            DocumentExpirationDate = data.DocumentExpirationDate,
            CustomFields = data.CustomFields?.Select(f => new CloudbedsCustomField(f.CustomFieldName, f.CustomFieldValue)).ToList() ?? [],
        };
    }

    public async Task<IReadOnlyList<CloudbedsGuest>> ListGuestsByStatusAsync(string propertyId, string apiKey, string status, CancellationToken cancellationToken)
    {
        const int PageSize = 100;
        var guests = new List<CloudbedsGuest>();
        for (var page = 1; page <= 100; page++) // a bound: 10,000 guests
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"getGuestsByStatus?propertyID={Uri.EscapeDataString(propertyId)}&status={Uri.EscapeDataString(status)}&pageNumber={page}&pageSize={PageSize}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<ListEnvelope>(cancellationToken);
            if (body is not { Success: true, Data: { Count: > 0 } data })
                break;
            guests.AddRange(data.Select(g => new CloudbedsGuest
            {
                GuestId = g.GuestId,
                Email = g.Email,
                FirstName = g.FirstName,
                LastName = g.LastName,
                Gender = g.Gender,
                Phone = g.Phone,
                Country = g.Country,
                BirthDate = g.BirthDate,
                DocumentType = g.DocumentType,
                DocumentNumber = g.DocumentNumber,
                DocumentIssueDate = g.DocumentIssueDate,
                DocumentIssuingCountry = g.DocumentIssuingCountry,
                DocumentExpirationDate = g.DocumentExpirationDate,
                CustomFields = g.CustomFields?.Select(f => new CloudbedsCustomField(f.CustomFieldName, f.CustomFieldValue)).ToList() ?? [],
            }));
            if (guests.Count >= body.Total || data.Count < PageSize)
                break;
        }
        return guests;
    }

    public Task<IReadOnlyList<CloudbedsReservation>> ListGuestReservationsAsync(string propertyId, string apiKey, string guestId, CancellationToken cancellationToken) =>
        ReservationsAsync(propertyId, apiKey, $"guestID={Uri.EscapeDataString(guestId)}&sortByRecent=true", cancellationToken);

    public Task<IReadOnlyList<CloudbedsReservation>> ListReservationsAsync(string propertyId, string apiKey, string? status, DateOnly? checkInFrom,
        DateOnly? checkInTo, CancellationToken cancellationToken) =>
        ReservationsAsync(propertyId, apiKey, string.Join("&", new[]
        {
            status is null ? null : $"status={Uri.EscapeDataString(status)}",
            checkInFrom is { } from ? $"checkInFrom={from:yyyy-MM-dd}" : null,
            checkInTo is { } to ? $"checkInTo={to:yyyy-MM-dd}" : null,
            "sortByRecent=false",
        }.OfType<string>()), cancellationToken);

    private async Task<IReadOnlyList<CloudbedsReservation>> ReservationsAsync(string propertyId, string apiKey, string filter, CancellationToken cancellationToken)
    {
        const int PageSize = 100;
        var reservations = new List<CloudbedsReservation>();
        for (var page = 1; page <= 20; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"getReservations?propertyID={Uri.EscapeDataString(propertyId)}&{filter}&includeAllRooms=true&pageNumber={page}&pageSize={PageSize}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                break; // not this property's guest
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<ReservationEnvelope>(cancellationToken);
            if (body is not { Success: true, Data: { Count: > 0 } data })
                break;
            reservations.AddRange(data.Where(r => !string.IsNullOrEmpty(r.ReservationId)).Select(r => new CloudbedsReservation(
                r.ReservationId!, r.Status, Date(r.StartDate), Date(r.EndDate), r.Balance ?? 0, r.SourceName,
                [.. (r.Rooms ?? []).Where(room => room.RoomStatus != "cancelled" && !string.IsNullOrWhiteSpace(room.RoomTypeName))
                    .Select(room => new CloudbedsRoom(room.RoomTypeName!.Trim(), string.IsNullOrWhiteSpace(room.RoomName) ? null : room.RoomName.Trim()))],
                r.GuestId, r.GuestName)));
            if (reservations.Count >= body.Total || data.Count < PageSize)
                break;
        }
        return reservations;
    }

    private static DateOnly? Date(string? value) =>
        DateOnly.TryParse(value?.Length > 10 ? value[..10] : value, System.Globalization.CultureInfo.InvariantCulture, out var date) ? date : null;

    private sealed record ReservationEnvelope(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("data")] IReadOnlyList<ReservationData>? Data);

    private sealed record ReservationData
    {
        [JsonPropertyName("reservationID"), JsonConverter(typeof(LooseString))] public string? ReservationId { get; init; }
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("startDate"), JsonConverter(typeof(LooseString))] public string? StartDate { get; init; }
        [JsonPropertyName("endDate"), JsonConverter(typeof(LooseString))] public string? EndDate { get; init; }
        [JsonPropertyName("balance"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public decimal? Balance { get; init; }
        [JsonPropertyName("sourceName")] public string? SourceName { get; init; }
        [JsonPropertyName("guestID"), JsonConverter(typeof(LooseString))] public string? GuestId { get; init; }
        [JsonPropertyName("guestName")] public string? GuestName { get; init; }
        [JsonPropertyName("rooms")] public IReadOnlyList<ReservationRoom>? Rooms { get; init; }
    }

    private sealed record ReservationRoom
    {
        [JsonPropertyName("roomTypeName")] public string? RoomTypeName { get; init; }
        [JsonPropertyName("roomName"), JsonConverter(typeof(LooseString))] public string? RoomName { get; init; }
        [JsonPropertyName("roomStatus")] public string? RoomStatus { get; init; }
    }

    private sealed record ListEnvelope(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("data")] IReadOnlyList<ListedGuest>? Data);

    /// <summary>getGuestsByStatus names every field "guest…" (getGuest doesn't).</summary>
    private sealed record ListedGuest
    {
        [JsonPropertyName("guestID"), JsonConverter(typeof(LooseString))] public string? GuestId { get; init; }
        [JsonPropertyName("guestEmail")] public string? Email { get; init; }
        [JsonPropertyName("guestFirstName")] public string? FirstName { get; init; }
        [JsonPropertyName("guestLastName")] public string? LastName { get; init; }
        [JsonPropertyName("guestGender")] public string? Gender { get; init; }
        [JsonPropertyName("guestPhone"), JsonConverter(typeof(LooseString))] public string? Phone { get; init; }
        [JsonPropertyName("guestCountry")] public string? Country { get; init; }
        [JsonPropertyName("guestBirthDate"), JsonConverter(typeof(LooseString))] public string? BirthDate { get; init; }
        [JsonPropertyName("guestDocumentType")] public string? DocumentType { get; init; }
        [JsonPropertyName("guestDocumentNumber"), JsonConverter(typeof(LooseString))] public string? DocumentNumber { get; init; }
        [JsonPropertyName("guestDocumentIssueDate"), JsonConverter(typeof(LooseString))] public string? DocumentIssueDate { get; init; }
        [JsonPropertyName("guestDocumentIssuingCountry")] public string? DocumentIssuingCountry { get; init; }
        [JsonPropertyName("guestDocumentExpirationDate"), JsonConverter(typeof(LooseString))] public string? DocumentExpirationDate { get; init; }
        [JsonPropertyName("customFields")] public IReadOnlyList<CustomField>? CustomFields { get; init; }
    }

    private sealed record Envelope([property: JsonPropertyName("success")] bool Success, [property: JsonPropertyName("data")] GuestData? Data);

    private sealed record GuestData
    {
        [JsonPropertyName("firstName")] public string? FirstName { get; init; }
        [JsonPropertyName("lastName")] public string? LastName { get; init; }
        [JsonPropertyName("gender")] public string? Gender { get; init; }
        [JsonPropertyName("phone")] public string? Phone { get; init; }
        [JsonPropertyName("country")] public string? Country { get; init; }
        [JsonPropertyName("birthDate"), JsonConverter(typeof(LooseString))] public string? BirthDate { get; init; }
        [JsonPropertyName("documentType")] public string? DocumentType { get; init; }
        [JsonPropertyName("documentNumber")] public string? DocumentNumber { get; init; }
        [JsonPropertyName("documentIssueDate"), JsonConverter(typeof(LooseString))] public string? DocumentIssueDate { get; init; }
        [JsonPropertyName("documentIssuingCountry")] public string? DocumentIssuingCountry { get; init; }
        [JsonPropertyName("documentExpirationDate"), JsonConverter(typeof(LooseString))] public string? DocumentExpirationDate { get; init; }
        [JsonPropertyName("customFields")] public IReadOnlyList<CustomField>? CustomFields { get; init; }
    }

    private sealed record CustomField(
        [property: JsonPropertyName("customFieldName"), JsonConverter(typeof(LooseString))] string? CustomFieldName,
        [property: JsonPropertyName("customFieldValue"), JsonConverter(typeof(LooseString))] string? CustomFieldValue);

    /// <summary>Cloudbeds sometimes sends dates and custom values as numbers, false or null: read anything as text.</summary>
    private sealed class LooseString : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => SkipAndNull(ref reader),
        };

        private static string? SkipAndNull(ref Utf8JsonReader reader)
        {
            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}
