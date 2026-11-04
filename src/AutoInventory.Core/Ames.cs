namespace AutoInventory.Core;

/// <summary>Ames branches. A car stands at exactly one of them.</summary>
public static class Branches
{
    public static readonly IReadOnlyList<string> All =
        ["Dordrecht", "Sliedrecht", "Zwijndrecht", "Ridderkerk", "Oud-Beijerland", "'s-Gravendeel"];

    public static bool IsValid(string? branch) => branch is not null && All.Contains(branch);
}

/// <summary>
/// Turns RDW's uppercase brand names into how the brands write themselves.
/// Any brand is allowed (trade-ins), the Volkswagen Group brands just get their official spelling.
/// </summary>
public static class Brands
{
    /// <summary>The brands Ames is official dealer for, in the order the site shows them.</summary>
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
        // Fallback: "TOYOTA" → "Toyota", "LAND ROVER" → "Land Rover"
        return string.Join(' ', brand.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length <= 1 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }
}
