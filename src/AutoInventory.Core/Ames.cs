namespace AutoInventory.Core;

public static class Branches
{
    public static readonly IReadOnlyList<string> All =
        ["Dordrecht", "Sliedrecht", "Zwijndrecht", "Ridderkerk", "Oud-Beijerland", "'s-Gravendeel"];

    public static bool IsValid(string? branch) => branch is not null && All.Contains(branch);
}

public static class Brands
{
    public static readonly IReadOnlyList<string> DealerBrands = ["Volkswagen", "Audi", "SEAT", "Škoda", "CUPRA"];

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VOLKSWAGEN"] = "Volkswagen", ["VW"] = "Volkswagen",
        ["AUDI"] = "Audi", ["SEAT"] = "SEAT", ["SKODA"] = "Škoda", ["ŠKODA"] = "Škoda", ["CUPRA"] = "CUPRA",
        ["BMW"] = "BMW", ["MINI"] = "MINI", ["KIA"] = "Kia", ["MG"] = "MG", ["DS"] = "DS",
        ["MERCEDES-BENZ"] = "Mercedes-Benz",
    };

    public static string Display(string rdwBrand)
    {
        var brand = rdwBrand.Trim();
        if (Known.TryGetValue(brand, out var known)) return known;
        return string.Join(' ', brand.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length <= 1 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
