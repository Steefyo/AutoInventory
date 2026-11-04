using Microsoft.Extensions.Caching.Memory;

namespace AutoInventory.Api.Rdw;

public class CachedRdwClient(IRdwClient inner, IMemoryCache cache, ILogger<CachedRdwClient> logger) : IRdwClient
{
    internal static readonly TimeSpan FoundTtl = TimeSpan.FromHours(12);
    internal static readonly TimeSpan NotFoundTtl = TimeSpan.FromMinutes(10);

    public async Task<RdwVehicle?> GetByLicensePlateAsync(string licensePlate, CancellationToken ct = default)
    {
        var key = $"rdw:{licensePlate}";
        if (cache.TryGetValue(key, out RdwVehicle? cached))
        {
            logger.LogDebug("RDW cache hit for {LicensePlate}", licensePlate);
            return cached;
        }

        var result = await inner.GetByLicensePlateAsync(licensePlate, ct);

        var ttl = result?.Fuel is null ? NotFoundTtl : FoundTtl;
        cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl, Size = 1 });
        return result;
    }
}
