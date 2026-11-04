using System.Net;
using System.Text;
using AutoInventory.Api.Rdw;
using AutoInventory.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoInventory.Tests;

public class RdwClientTests
{
    private const string VehicleJson = """[{"merk":"SKODA","handelsbenaming":"OCTAVIA","datum_eerste_toelating":"20200612"}]""";
    private const string FuelJson = """[{"brandstof_omschrijving":"Benzine"},{"brandstof_omschrijving":"Elektriciteit"}]""";

    private sealed class StubHandler(Func<string, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref Calls);
            var dataset = request.RequestUri!.AbsolutePath.Contains(RdwClient.FuelDataset) ? "fuel" : "vehicle";
            return Task.FromResult(respond(dataset, n));
        }
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static RdwClient Client(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://rdw.test/") };
        return new RdwClient(http, http, NullLogger<RdwClient>.Instance);
    }

    [Theory]
    [InlineData(FuelType.Petrol, "Benzine")]
    [InlineData(FuelType.Diesel, "Diesel")]
    [InlineData(FuelType.Electric, "Elektriciteit")]
    [InlineData(FuelType.Hybrid, "Benzine", "Elektriciteit")]
    [InlineData(FuelType.Hybrid, "Diesel", "Elektriciteit")]
    [InlineData(FuelType.Lpg, "Benzine", "LPG")]
    [InlineData(null, "Waterstof")]
    [InlineData(null)]
    public void Fuel_rows_are_mapped_to_our_fuel_type(FuelType? expected, params string[] rows) =>
        Assert.Equal(expected, RdwClient.MapFuel(rows));

    [Fact]
    public async Task Both_datasets_are_combined()
    {
        var v = await Client(new StubHandler((ds, _) => Ok(ds == "fuel" ? FuelJson : VehicleJson))).GetByLicensePlateAsync("HX512B");
        Assert.Equal(new RdwVehicle("HX512B", "Škoda", "OCTAVIA", 2020, FuelType.Hybrid), v);
    }

    [Fact]
    public async Task Failing_fuel_dataset_still_returns_the_vehicle()
    {
        var handler = new StubHandler((ds, _) => ds == "fuel" ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Ok(VehicleJson));
        var v = await Client(handler).GetByLicensePlateAsync("HX512B");
        Assert.NotNull(v);
        Assert.Equal("Škoda", v.Brand);
        Assert.Null(v.Fuel);
    }

    [Fact]
    public async Task Unknown_plate_returns_null() =>
        Assert.Null(await Client(new StubHandler((_, _) => Ok("[]"))).GetByLicensePlateAsync("ZZ999Z"));

    [Theory]
    [InlineData("status")]
    [InlineData("network")]
    [InlineData("json")]
    public async Task Failures_of_the_main_dataset_become_RdwUnavailable(string kind)
    {
        var handler = new StubHandler((_, _) => kind switch
        {
            "status" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "json" => Ok("<html>maintenance</html>"),
            _ => throw new HttpRequestException("connection refused"),
        });
        await Assert.ThrowsAsync<RdwUnavailableException>(() => Client(handler).GetByLicensePlateAsync("HX512B"));
    }

    [Fact]
    public async Task Unknown_plate_cancels_the_fuel_request_instead_of_leaving_it_running()
    {
        var fuelCancelled = new TaskCompletionSource<bool>();
        var handler = new DelegatingStub(async (request, ct) =>
        {
            if (!request.RequestUri!.AbsolutePath.Contains(RdwClient.FuelDataset)) return Ok("[]");
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
            catch (OperationCanceledException) { fuelCancelled.SetResult(true); throw; }
            return Ok(FuelJson);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://rdw.test/") };

        Assert.Null(await new RdwClient(http, http, NullLogger<RdwClient>.Instance).GetByLicensePlateAsync("ZZ999Z"));
        Assert.True(fuelCancelled.Task.IsCompletedSuccessfully);
    }

    private sealed class DelegatingStub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }

    [Fact]
    public void Overlong_rdw_values_are_capped_at_the_column_size()
    {
        var v = RdwClient.Map("AB123C", new RdwClient.RdwDto("X", new string('M', 300), null));
        Assert.Equal(Limits.Model, v.Model.Length);
    }
}
