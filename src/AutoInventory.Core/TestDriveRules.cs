using System.Text.RegularExpressions;

namespace AutoInventory.Core;

public record TestDriveRequest(string? Name, string? Email, string? Phone, DateTime? ScheduledAt);

public static partial class TestDriveRules
{
    public static readonly TimeSpan SlotLength = TimeSpan.FromHours(1);

    public static Dictionary<string, string[]> Validate(
        TestDriveRequest request,
        Vehicle vehicle,
        DateTime nowUtc,
        IEnumerable<DateTime> existingSlotsUtc,
        OpeningHours openingHours)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
            list.Add(message);
        }

        if (vehicle.Status == VehicleStatus.Sold)
            Add("vehicle", "Dit voertuig is verkocht; een proefrit is niet meer mogelijk.");

        if (request.ScheduledAt is not { } requested)
        {
            Add("scheduledAt", "Kies een datum en tijd.");
        }
        else
        {
            var scheduled = ToUtc(requested);

            if (scheduled <= nowUtc)
                Add("scheduledAt", "De datum/tijd moet in de toekomst liggen.");

            else if (!openingHours.IsSlotStart(scheduled))
                Add("scheduledAt", "Kies een starttijd op het hele uur (bijv. 10:00).");

            else if (!openingHours.Contains(scheduled, SlotLength))
                Add("scheduledAt", $"Proefritten zijn mogelijk tijdens openingstijden ({openingHours.Describe()}); een proefrit duurt een uur.");

            else if (existingSlotsUtc.Any(s => Overlaps(s, scheduled)))
                Add("scheduledAt", "Dit tijdslot is al geboekt voor dit voertuig.");
        }

        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0) Add("name", "Naam is verplicht.");
        else if (name.Length > Limits.Name) Add("name", $"Naam mag maximaal {Limits.Name} tekens zijn.");

        var email = request.Email?.Trim() ?? "";
        if (email.Length > Limits.Email) Add("email", $"E-mailadres mag maximaal {Limits.Email} tekens zijn.");
        else if (!EmailPattern().IsMatch(email)) Add("email", "Vul een geldig e-mailadres in, bijv. naam@voorbeeld.nl.");

        var phone = request.Phone?.Trim() ?? "";
        var digits = phone.Count(char.IsAsciiDigit);
        if (phone.Length > Limits.Phone || !PhonePattern().IsMatch(phone) || digits is < 10 or > 15)
            Add("phone", "Vul een geldig telefoonnummer in (10 tot 15 cijfers; alleen cijfers, spaties, +, - en haakjes).");

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    public static bool Overlaps(DateTime existingUtc, DateTime candidateUtc) =>
        (existingUtc - candidateUtc).Duration() < SlotLength;

    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    [GeneratedRegex(@"^[A-Za-z0-9._%+'-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9 ()-]+$")]
    private static partial Regex PhonePattern();
}
