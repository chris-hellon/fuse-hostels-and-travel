using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Shouldly;
using Fuse.Infrastructure.Cloudbeds;

namespace Fuse.Web.Tests;

/// <summary>The Cloudbeds client's getReservations paging, against a stand-in Cloudbeds (no network).</summary>
public sealed class CloudbedsClientTests
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Serves <c>total</c> reservations a page at a time (later pages answer sooner, so order is tested), and
    /// counts how many pages are asked at once.</summary>
    private sealed class PagedCloudbeds(int total, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        private int _inFlight;
        public int MostAtOnce;
        public ConcurrentBag<string> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!.Query);
            var now = Interlocked.Increment(ref _inFlight);
            InterlockedMax(ref MostAtOnce, now);
            try
            {
                var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
                var page = int.Parse(query["pageNumber"]!);
                var size = int.Parse(query["pageSize"]!);
                await Task.Delay(Math.Max(1, 60 - page * 5), cancellationToken);
                if (status != HttpStatusCode.OK)
                    return new HttpResponseMessage(status);
                var data = Enumerable.Range((page - 1) * size + 1, Math.Max(0, Math.Min(size, total - (page - 1) * size)))
                    .Select(n => new { reservationID = $"R{n}", status = "checked_out", startDate = "2026-09-01", endDate = "2026-09-03", guestID = $"G{n}",
                        rooms = new[] { new { roomTypeName = "Dorm", roomName = $"Bed {n}", roomStatus = "checked_out" } } });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { success = true, total, data }), Encoding.UTF8, "application/json"),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            while (value > (seen = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, seen) != seen)
            {
            }
        }
    }

    private static CloudbedsClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://cloudbeds.invalid/api/v1.1/") });

    [Fact]
    public async Task Reservations_pages_after_the_first_are_asked_at_once_and_kept_in_order()
    {
        var cloudbeds = new PagedCloudbeds(total: 950);

        var reservations = await Client(cloudbeds).ListReservationsAsync("p1", "key", null, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), Ct);

        reservations.Select(r => r.ReservationId).ShouldBe(Enumerable.Range(1, 950).Select(n => $"R{n}"));
        reservations[0].Rooms.ShouldHaveSingleItem().RoomName.ShouldBe("Bed 1");
        cloudbeds.Asked.Count.ShouldBe(10);
        cloudbeds.MostAtOnce.ShouldBeGreaterThan(1);
        cloudbeds.MostAtOnce.ShouldBeLessThanOrEqualTo(CloudbedsClient.ReservationPagesAtOnce);
        cloudbeds.Asked.ShouldAllBe(q => q.Contains("includeAllRooms=true"));
    }

    [Fact]
    public async Task Rooms_are_only_asked_for_when_wanted()
    {
        var cloudbeds = new PagedCloudbeds(total: 150);

        var reservations = await Client(cloudbeds).ListReservationsAsync("p1", "key", null, null, null, Ct, includeRooms: false);

        reservations.Count.ShouldBe(150);
        cloudbeds.Asked.Count.ShouldBe(2);
        cloudbeds.Asked.ShouldAllBe(q => !q.Contains("includeAllRooms"));
    }

    [Fact]
    public async Task Reservations_stop_at_the_page_bound_and_after_one_page_when_that_is_all()
    {
        var huge = new PagedCloudbeds(total: 7000);
        (await Client(huge).ListReservationsAsync("p1", "key", "checked_in", null, null, Ct)).Count
            .ShouldBe(CloudbedsClient.MaxReservationPages * CloudbedsClient.ReservationPageSize);
        huge.Asked.Count.ShouldBe(CloudbedsClient.MaxReservationPages);

        var small = new PagedCloudbeds(total: 40);
        (await Client(small).ListReservationsAsync("p1", "key", "checked_in", null, null, Ct)).Count.ShouldBe(40);
        small.Asked.Count.ShouldBe(1);

        var unknown = new PagedCloudbeds(total: 300, HttpStatusCode.NotFound);
        (await Client(unknown).ListGuestReservationsAsync("p1", "key", "g1", Ct)).ShouldBeEmpty();
        unknown.Asked.Count.ShouldBe(1);
    }
}
