using System.Globalization;
using System.Text.Json.Serialization;
using AutoInventory.Core;

namespace AutoInventory.Api.Rdw;

public record RdwVehicle(string LicensePlate, string Brand, string Model, int? Year, FuelType? Fuel = null);

public class RdwUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IRdwClient
{
    Task<RdwVehicle?> GetByLicensePlateAsync(string normalizedLicensePlate, CancellationToken ct = default);
}

public class RdwClient(HttpClient vehicles, HttpClient fuel, ILogger<RdwClient> logger) : IRdwClient
{
    internal const string VehiclesDataset = "m9d7-ebf2";
    internal const string FuelDataset = "8ys7-d773";

    public async Task<RdwVehicle?> GetByLicensePlateAsync(string licensePlate, CancellationToken ct = default)
    {
        var plate = Uri.EscapeDataString(licensePlate);
        using var fuelCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var fuelTask = GetAsync<RdwFuelDto>(fuel, DatasetUrl(FuelDataset, plate), licensePlate, fuelCts.Token);

        RdwDto? row;
        try
        {
            row = (await GetAsync<RdwDto>(vehicles, DatasetUrl(VehiclesDataset, plate), licensePlate, ct)).FirstOrDefault();
        }
        catch
        {
            await CancelAndObserve(fuelCts, fuelTask);
            throw;
        }

        if (row is null || string.IsNullOrWhiteSpace(row.Brand))
        {
            await CancelAndObserve(fuelCts, fuelTask);
            logger.LogInformation("RDW has no vehicle for {LicensePlate}", licensePlate);
            return null;
        }

        FuelType? fuelType = null;
        try
        {
            fuelType = MapFuel((await fuelTask).Select(f => f.Description));
        }
        catch (RdwUnavailableException)
        {
            logger.LogWarning("Continuing without RDW fuel data for {LicensePlate}", licensePlate);
        }

        return Map(licensePlate, row) with { Fuel = fuelType };
    }

    private static string DatasetUrl(string dataset, string plate) => $"resource/{dataset}.json?kenteken={plate}";

    private static async Task CancelAndObserve(CancellationTokenSource cts, Task task)
    {
        await cts.CancelAsync();
        try { await task; } catch { }
    }

    private async Task<List<T>> GetAsync<T>(HttpClient http, string url, string licensePlate, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("RDW returned {StatusCode} for {Url} ({LicensePlate})", (int)response.StatusCode, url, licensePlate);
                throw new RdwUnavailableException($"RDW antwoordde met status {(int)response.StatusCode}.");
            }
            return await response.Content.ReadFromJsonAsync<List<T>>(ct) ?? [];
        }
        catch (RdwUnavailableException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "RDW request {Url} failed for {LicensePlate}", url, licensePlate);
            throw new RdwUnavailableException("De RDW is op dit moment niet bereikbaar.", ex);
        }
    }

    internal static RdwVehicle Map(string licensePlate, RdwDto row) => new(
        licensePlate,
        Cap(Brands.Display(row.Brand!), Limits.Brand),
        string.IsNullOrWhiteSpace(row.TradeName) ? "Onbekend" : Cap(row.TradeName.Trim(), Limits.Model),
        DateTime.TryParseExact(row.FirstRegistrationDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Year : null);

    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];

    internal static FuelType? MapFuel(IEnumerable<string?> descriptions)
    {
        var fuels = descriptions.Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.Trim().ToUpperInvariant()).ToHashSet();
        var electric = fuels.Contains("ELEKTRICITEIT");
        var combustion = fuels.Contains("BENZINE") || fuels.Contains("DIESEL");

        if (electric && combustion) return FuelType.Hybrid;
        if (electric && fuels.Count == 1) return FuelType.Electric;
        if (fuels.Contains("LPG")) return FuelType.Lpg;
        if (fuels.Contains("DIESEL")) return FuelType.Diesel;
        if (fuels.Contains("BENZINE")) return FuelType.Petrol;
        return null;
    }

    internal record RdwDto(
        [property: JsonPropertyName("merk")] string? Brand,
        [property: JsonPropertyName("handelsbenaming")] string? TradeName,
        [property: JsonPropertyName("datum_eerste_toelating")] string? FirstRegistrationDate);

    internal record RdwFuelDto([property: JsonPropertyName("brandstof_omschrijving")] string? Description);
}
