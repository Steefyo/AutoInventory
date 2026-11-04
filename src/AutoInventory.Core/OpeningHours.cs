using System.Globalization;

namespace AutoInventory.Core;

public sealed class OpeningHours(TimeZoneInfo timeZone, IReadOnlyDictionary<DayOfWeek, (TimeOnly Open, TimeOnly Close)> days)
{
    public TimeZoneInfo TimeZone { get; } = timeZone;
    public IReadOnlyDictionary<DayOfWeek, (TimeOnly Open, TimeOnly Close)> Days { get; } = days;

    public bool Contains(DateTime startUtc, TimeSpan length)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(startUtc, TimeZone);
        if (!Days.TryGetValue(local.DayOfWeek, out var hours)) return false;

        var start = TimeOnly.FromDateTime(local);
        var end = start.Add(length, out var wrappedDays);
        return wrappedDays == 0 && start >= hours.Open && end <= hours.Close;
    }

    public bool IsSlotStart(DateTime startUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(startUtc, TimeZone);
        return local is { Minute: 0, Second: 0, Millisecond: 0 };
    }

    public IEnumerable<DateTime> SlotsOn(DateOnly date, TimeSpan length)
    {
        if (!Days.TryGetValue(date.DayOfWeek, out var hours)) yield break;

        var hour = hours.Open.Minute == 0 ? hours.Open.Hour : hours.Open.Hour + 1;
        for (; hour < 24; hour++)
        {
            var start = new TimeOnly(hour, 0);
            var end = start.Add(length, out var wrapped);
            if (wrapped > 0 || end > hours.Close) yield break;
            yield return TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(start), TimeZone);
        }
    }

    public string Describe()
    {
        var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        string Day(DayOfWeek d) => new[] { "zo", "ma", "di", "wo", "do", "vr", "za" }[(int)d];
        string Hours((TimeOnly Open, TimeOnly Close) h) => $"{h.Open:HH\\:mm}–{h.Close:HH\\:mm}";

        var parts = new List<string>();
        for (var i = 0; i < order.Length; i++)
        {
            if (!Days.TryGetValue(order[i], out var h)) continue;
            var j = i;
            while (j + 1 < order.Length && Days.TryGetValue(order[j + 1], out var next) && next == h) j++;
            parts.Add($"{Day(order[i])}{(j > i ? "–" + Day(order[j]) : "")} {Hours(h)}");
            i = j;
        }
        return parts.Count == 0 ? "gesloten" : string.Join(", ", parts);
    }

    public static OpeningHours Parse(string timeZoneId, IDictionary<string, string> days) => new(
        TimeZoneInfo.FindSystemTimeZoneById(timeZoneId),
        days.ToDictionary(
            d => Enum.Parse<DayOfWeek>(d.Key, ignoreCase: true),
            d =>
            {
                var p = d.Value.Split('-', 2, StringSplitOptions.TrimEntries);
                return (TimeOnly.Parse(p[0], CultureInfo.InvariantCulture), TimeOnly.Parse(p[1], CultureInfo.InvariantCulture));
            }));
}
