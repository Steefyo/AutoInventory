namespace AutoInventory.Core;

public record VehicleInput(string? LicensePlate, int Mileage, FuelType Fuel, decimal Price, string? Branch,
    VehicleStatus Status = VehicleStatus.Available, int? Version = null);

public static class VehicleRules
{
    public const int MaxMileage = 2_000_000;
    public const decimal MaxPrice = 10_000_000m;

    public static Dictionary<string, string[]> Validate(VehicleInput input, bool isUpdate)
    {
        var errors = new Dictionary<string, string[]>();
        if (!isUpdate && !LicensePlate.IsValid(LicensePlate.Normalize(input.LicensePlate)))
            errors["licensePlate"] = ["Ongeldig kenteken (verwacht bijv. AB-123-C)."];
        if (isUpdate && input.Version is null)
            errors["version"] = ["Versie ontbreekt; laad het voertuig opnieuw."];
        if (input.Mileage is < 0 or > MaxMileage)
            errors["mileage"] = ["Kilometerstand moet tussen 0 en 2.000.000 liggen."];
        if (input.Price is <= 0 or > MaxPrice)
            errors["price"] = ["Prijs moet tussen € 1 en € 10.000.000 liggen."];
        else if (decimal.Round(input.Price, 2) != input.Price)
            errors["price"] = ["Prijs mag maximaal 2 decimalen hebben."];
        if (!Enum.IsDefined(input.Fuel))
            errors["fuel"] = ["Onbekend brandstoftype."];
        if (!Branches.IsValid(input.Branch))
            errors["branch"] = ["Kies een vestiging."];
        if (!Enum.IsDefined(input.Status))
            errors["status"] = ["Onbekende status."];
        return errors;
    }
}
