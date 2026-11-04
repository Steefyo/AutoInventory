using Microsoft.Extensions.Caching.Memory;

namespace AutoInventory.Api.Rdw;

public static class RdwRegistration
{
    public const string VehiclesClient = "rdw-vehicles";
    public const string FuelClient = "rdw-fuel";

    public static IServiceCollection AddRdw(this IServiceCollection services, IConfiguration config)
    {
        var baseUrl = new Uri(config["Rdw:BaseUrl"] ?? "https://opendata.rdw.nl/");

        services.AddHttpClient(VehiclesClient, c => c.BaseAddress = baseUrl)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
                o.Retry.MaxRetryAttempts = 2;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30); // must be ≥ 2 × attempt timeout
                o.CircuitBreaker.MinimumThroughput = 5;
                o.CircuitBreaker.FailureRatio = 0.5;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            });

        services.AddHttpClient(FuelClient, c => c.BaseAddress = baseUrl)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.Retry.MaxRetryAttempts = 1;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                o.CircuitBreaker.MinimumThroughput = 5;
                o.CircuitBreaker.FailureRatio = 0.5;
                o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });

        services.AddTransient(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new RdwClient(factory.CreateClient(VehiclesClient), factory.CreateClient(FuelClient),
                sp.GetRequiredService<ILogger<RdwClient>>());
        });

        services.AddMemoryCache(o => o.SizeLimit = 10_000);
        services.AddTransient<IRdwClient>(sp => new CachedRdwClient(
            sp.GetRequiredService<RdwClient>(),
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<ILogger<CachedRdwClient>>()));

        return services;
    }
}
