using System.Globalization;
using System.Text.RegularExpressions;
using AutoInventory.Core;

namespace AutoInventory.Api.Pages;

public static partial class Format
{
    private static readonly NumberFormatInfo Nl = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static string Euro(decimal amount) => "€ " + amount.ToString(amount % 1 == 0 ? "#,0" : "#,0.00", Nl);

    public static string Km(int km) => km.ToString("#,0", Nl) + " km";

    public static string Plate(string licensePlate) => string.Join('-', PlateGroups().Matches(licensePlate).Select(m => m.Value));

    public static string Year(int? year) => year?.ToString(CultureInfo.InvariantCulture) ?? "Onbekend";

    private static readonly string[] Days = ["zondag", "maandag", "dinsdag", "woensdag", "donderdag", "vrijdag", "zaterdag"];
    private static readonly string[] Months = ["januari", "februari", "maart", "april", "mei", "juni", "juli",
        "augustus", "september", "oktober", "november", "december"];

    public static string Date(DateOnly d) => $"{Days[(int)d.DayOfWeek]} {d.Day} {Months[d.Month - 1]}";

    public static string Fuel(FuelType? f) => f switch
    {
        FuelType.Petrol => "Benzine",
        FuelType.Diesel => "Diesel",
        FuelType.Electric => "Elektrisch",
        FuelType.Hybrid => "Hybride",
        FuelType.Lpg => "LPG",
        null => "",
        _ => f.ToString()!,
    };

    public static string Status(VehicleStatus s) => s switch
    {
        VehicleStatus.Available => "Beschikbaar",
        VehicleStatus.Sold => "Verkocht",
        _ => s.ToString(),
    };

    [GeneratedRegex("[A-Z]+|[0-9]+")]
    private static partial Regex PlateGroups();
}
