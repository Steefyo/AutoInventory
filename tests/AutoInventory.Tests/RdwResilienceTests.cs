using System.Net;
using System.Text;
using AutoInventory.Api.Rdw;
using AutoInventory.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutoInventory.Tests;

public class RdwResilienceTests
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

    private sealed class CountingRdw(Func<RdwVehicle?> answer) : IRdwClient
    {
        public int Calls;
        public Task<RdwVehicle?> GetByLicensePlateAsync(string licensePlate, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(answer());
        }
    }

    private static CachedRdwClient Cached(IRdwClient inner) =>
        new(inner, new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), NullLogger<CachedRdwClient>.Instance);

    [Fact]
    public async Task Second_lookup_of_the_same_plate_comes_from_the_cache()
    {
        var inner = new CountingRdw(() => new RdwVehicle("HX512B", "Škoda", "OCTAVIA", 2020, FuelType.Petrol));
        var cached = Cached(inner);
        await cached.GetByLicensePlateAsync("HX512B");
        var second = await cached.GetByLicensePlateAsync("HX512B");
        Assert.Equal(1, inner.Calls);
        Assert.Equal("OCTAVIA", second!.Model);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        var fail = true;
        var inner = new CountingRdw(() => fail ? throw new RdwUnavailableException("down") : new RdwVehicle("HX512B", "Škoda", "OCTAVIA", 2020));
        var cached = Cached(inner);

        await Assert.ThrowsAsync<RdwUnavailableException>(() => cached.GetByLicensePlateAsync("HX512B"));
        fail = false;
        Assert.NotNull(await cached.GetByLicensePlateAsync("HX512B"));
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Production_pipeline_retries_a_transient_rdw_error()
    {
        var vehicleCalls = 0;
        var handler = new StubHandler((ds, _) =>
            ds == "fuel" ? Ok(FuelJson)
            : Interlocked.Increment(ref vehicleCalls) == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Ok(VehicleJson));

        var services = new ServiceCollection().AddLogging()
            .AddRdw(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rdw:BaseUrl"] = "https://rdw.test/" }).Build());
        services.AddHttpClient(RdwRegistration.VehiclesClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(RdwRegistration.FuelClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var v = await provider.GetRequiredService<IRdwClient>().GetByLicensePlateAsync("HX512B");

        Assert.Equal("Škoda", v!.Brand);
        Assert.Equal(2, vehicleCalls);
    }

    [Fact]
    public async Task Circuit_breaker_stops_calling_an_rdw_that_keeps_failing()
    {
        var vehicleCalls = 0;
        var handler = new StubHandler((ds, _) =>
        {
            if (ds == "fuel") return Ok(FuelJson);
            Interlocked.Increment(ref vehicleCalls);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var services = new ServiceCollection().AddLogging()
            .AddRdw(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rdw:BaseUrl"] = "https://rdw.test/" }).Build());
        services.AddHttpClient(RdwRegistration.VehiclesClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(RdwRegistration.FuelClient).ConfigurePrimaryHttpMessageHandler(() => handler);
        services.PostConfigure<HttpStandardResilienceOptions>($"{RdwRegistration.VehiclesClient}-standard", o => o.Retry.Delay = TimeSpan.Zero);
        using var provider = services.BuildServiceProvider();
        var rdw = provider.GetRequiredService<IRdwClient>();

        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<RdwUnavailableException>(() => rdw.GetByLicensePlateAsync("HX512B"));

        Assert.InRange(vehicleCalls, 5, 6);
    }
}
