using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoInventory.Api.Data;
using AutoInventory.Api.Rdw;
using AutoInventory.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoInventory.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));

            services.RemoveAll<IRdwClient>();
            services.AddSingleton<IRdwClient, FakeRdw>();

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedClock(Now));
        });
    }

    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeRdw : IRdwClient
    {
        public Task<RdwVehicle?> GetByLicensePlateAsync(string licensePlate, CancellationToken ct = default) => licensePlate switch
        {
            "SK123X" => Task.FromResult<RdwVehicle?>(new RdwVehicle(licensePlate, "Škoda", "KAMIQ", 2021)),
            "NB000X" => Task.FromResult<RdwVehicle?>(new RdwVehicle(licensePlate, "Volkswagen", "GOLF", null)),
            "DD001D" => throw new RdwUnavailableException("De RDW is op dit moment niet bereikbaar."),
            _ => Task.FromResult<RdwVehicle?>(null),
        };
    }
}

[CollectionDefinition(Name)]
public class AppCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "app";
}

[Collection(AppCollection.Name)]
public class ApiIntegrationTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTime Wednesday11 = new(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

    private static object Drive(DateTime? utc, string name = "Jan Jansen") =>
        new { name, email = "jan@example.nl", phone = "0612345678", scheduledAt = utc };

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    private static bool HasFieldError(JsonElement problem, string field) =>
        problem.GetProperty("errors").TryGetProperty(field, out _);

    [Fact]
    public async Task Reset_gives_each_test_a_fresh_database()
    {
        await _client.PostAsJsonAsync("/api/vehicles",
            new { licensePlate = "SK123X", mileage = 1, fuel = "Petrol", price = 1000, branch = "Dordrecht" });
        Assert.Equal(8, (await Json(await _client.GetAsync("/api/vehicles"))).GetArrayLength());

        await factory.ResetDatabaseAsync();
        Assert.Equal(7, (await Json(await _client.GetAsync("/api/vehicles"))).GetArrayLength());
    }

    [Fact]
    public async Task Vehicles_can_be_filtered_by_brand_and_branch()
    {
        var byBrand = await Json(await _client.GetAsync("/api/vehicles?brand=Škoda"));
        Assert.Equal("OCTAVIA COMBI", Assert.Single(byBrand.EnumerateArray()).GetProperty("model").GetString());

        var byBranch = (await Json(await _client.GetAsync("/api/vehicles?branch=Dordrecht"))).EnumerateArray().ToList();
        Assert.NotEmpty(byBranch);
        Assert.All(byBranch, v => Assert.Equal("Dordrecht", v.GetProperty("branch").GetString()));
    }

    [Fact]
    public async Task Adding_a_vehicle_takes_brand_and_model_from_rdw()
    {
        var r = await _client.PostAsJsonAsync("/api/vehicles",
            new { licensePlate = "sk-123-x", mileage = 30_000, fuel = "Petrol", price = 21_950, branch = "Ridderkerk" });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var v = await Json(r);
        Assert.Equal("SK123X", v.GetProperty("licensePlate").GetString());
        Assert.Equal("Škoda", v.GetProperty("brand").GetString());
        Assert.Equal("KAMIQ", v.GetProperty("model").GetString());
        Assert.Equal(2021, v.GetProperty("year").GetInt32());
        Assert.Equal($"/api/vehicles/{v.GetProperty("id").GetInt32()}", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Unknown_year_is_null_not_zero()
    {
        var r = await _client.PostAsJsonAsync("/api/vehicles",
            new { licensePlate = "NB000X", mileage = 1, fuel = "Petrol", price = 1000, branch = "Dordrecht" });
        Assert.Equal(JsonValueKind.Null, (await Json(r)).GetProperty("year").ValueKind);
    }

    [Theory]
    [InlineData("ZZ999Z", HttpStatusCode.NotFound)]
    [InlineData("DD001D", HttpStatusCode.BadGateway)]
    [InlineData("GV123X", HttpStatusCode.Conflict)]
    [InlineData("nope", HttpStatusCode.BadRequest)]
    public async Task Adding_a_vehicle_maps_failures_to_clear_status_codes(string licensePlate, HttpStatusCode expected)
    {
        var r = await _client.PostAsJsonAsync("/api/vehicles",
            new { licensePlate, mileage = 1, fuel = "Petrol", price = 1000, branch = "Dordrecht" });
        Assert.Equal(expected, r.StatusCode);
    }

    [Fact]
    public async Task Stale_update_is_refused_instead_of_overwriting()
    {
        object Update(int version, decimal price) =>
            new { mileage = 68_500, fuel = "Petrol", price, branch = "Dordrecht", status = "Available", version };

        var first = await _client.PutAsJsonAsync("/api/vehicles/1", Update(0, 18_950));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, (await Json(first)).GetProperty("version").GetInt32());

        var second = await _client.PutAsJsonAsync("/api/vehicles/1", Update(0, 19_950));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var now = await Json(await _client.GetAsync("/api/vehicles/1"));
        Assert.Equal(18_950m, now.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Vehicle_with_upcoming_test_drive_cannot_be_deleted()
    {
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/vehicles/1/testdrives", Drive(Wednesday11))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync("/api/vehicles/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/vehicles/3")).StatusCode);
    }

    [Fact]
    public async Task Test_drive_is_booked_and_same_slot_is_then_refused()
    {
        var first = await _client.PostAsJsonAsync("/api/vehicles/1/testdrives", Drive(Wednesday11));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Null(first.Headers.Location);

        var second = await _client.PostAsJsonAsync("/api/vehicles/1/testdrives", Drive(Wednesday11, "Piet"));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.True(HasFieldError(await Json(second), "scheduledAt"));
    }

    [Fact]
    public async Task Missing_time_is_a_field_error_not_a_binding_failure()
    {
        var r = await _client.PostAsJsonAsync("/api/vehicles/1/testdrives", Drive(null));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True(HasFieldError(await Json(r), "scheduledAt"));
    }

    [Fact]
    public async Task Test_drive_for_sold_vehicle_returns_validation_problem()
    {
        var r = await _client.PostAsJsonAsync("/api/vehicles/4/testdrives", Drive(Wednesday11));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.True(HasFieldError(await Json(r), "vehicle"));
    }

    [Fact]
    public async Task Test_drive_on_sunday_is_refused()
    {
        var r = await _client.PostAsJsonAsync("/api/vehicles/1/testdrives", Drive(new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Test_drive_for_unknown_vehicle_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync("/api/vehicles/999/testdrives", Drive(Wednesday11))).StatusCode);

    [Fact]
    public async Task Availability_lists_free_and_booked_slots_for_a_dutch_date()
    {
        var thursday14 = new DateTime(2026, 11, 5, 13, 0, 0, DateTimeKind.Utc);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/vehicles/3/testdrives", Drive(thursday14))).StatusCode);

        var slots = (await Json(await _client.GetAsync("/api/vehicles/3/availability?date=2026-11-05"))).EnumerateArray().ToList();

        Assert.Equal(8, slots.Count);
        var booked = Assert.Single(slots, s => !s.GetProperty("free").GetBoolean());
        Assert.Equal("14:00", booked.GetProperty("time").GetString());
    }

    [Fact]
    public async Task Availability_is_empty_for_a_sold_vehicle() =>
        Assert.Empty((await Json(await _client.GetAsync("/api/vehicles/4/availability?date=2026-11-05"))).EnumerateArray());
}
