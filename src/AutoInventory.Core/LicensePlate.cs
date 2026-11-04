using System.Text.RegularExpressions;

namespace AutoInventory.Core;

public static partial class LicensePlate
{
    public static string Normalize(string? input) =>
        string.Concat((input ?? "").Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static bool IsValid(string normalized) => Pattern().IsMatch(normalized);

    [GeneratedRegex("^(?=.*[A-Z])(?=.*[0-9])[A-Z0-9]{6}$")]
    private static partial Regex Pattern();
}
