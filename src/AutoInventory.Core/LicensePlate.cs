using System.Text.RegularExpressions;

namespace AutoInventory.Core;

/// <summary>Normalises a Dutch licence plate ("ab-123-c" → "AB123C") the way the RDW API expects it.</summary>
public static partial class LicensePlate
{
    public static string Normalize(string? input) =>
        string.Concat((input ?? "").Where(char.IsLetterOrDigit)).ToUpperInvariant();

    /// <summary>Format check only (6 letters/digits, at least one of each). Whether the plate exists is RDW's call.</summary>
    public static bool IsValid(string normalized) => Pattern().IsMatch(normalized);

    [GeneratedRegex("^(?=.*[A-Z])(?=.*[0-9])[A-Z0-9]{6}$")]
    private static partial Regex Pattern();
}
