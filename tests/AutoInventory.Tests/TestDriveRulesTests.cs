using AutoInventory.Core;

namespace AutoInventory.Tests;

public class TestDriveRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static readonly OpeningHours Hours = OpeningHours.Parse("Europe/Amsterdam", new Dictionary<string, string>
    {
        ["Monday"] = "08:30-17:30", ["Tuesday"] = "08:30-17:30", ["Wednesday"] = "08:30-17:30",
        ["Thursday"] = "08:30-17:30", ["Friday"] = "08:30-17:30", ["Saturday"] = "09:00-16:00",
    });

    private static Vehicle Available() => new() { Id = 1, Status = VehicleStatus.Available };
    private static Vehicle Sold() => new() { Id = 2, Status = VehicleStatus.Sold };

    private static TestDriveRequest Request(DateTime? at = null) =>
        new("Jan Jansen", "jan@example.nl", "06 12345678", at ?? Now.AddDays(1));

    private static Dictionary<string, string[]> Validate(TestDriveRequest r, Vehicle v, params DateTime[] booked) =>
        TestDriveRules.Validate(r, v, Now, booked, Hours);

    [Fact]
    public void Valid_request_for_available_vehicle_in_future_has_no_errors() =>
        Assert.Empty(Validate(Request(), Available()));

    [Fact]
    public void Sold_vehicle_is_rejected() =>
        Assert.Contains("vehicle", Validate(Request(), Sold()).Keys);

    [Theory]
    [InlineData(-60)]
    [InlineData(0)]
    public void Date_not_in_future_is_rejected(int minutesFromNow) =>
        Assert.Contains("scheduledAt", Validate(Request(Now.AddMinutes(minutesFromNow)), Available()).Keys);

    [Fact]
    public void Next_whole_hour_is_accepted() =>
        Assert.Empty(Validate(Request(Now.AddHours(1)), Available()));

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void Start_not_on_the_whole_hour_is_rejected(int minutes) =>
        Assert.Contains("scheduledAt", Validate(Request(Now.AddDays(1).AddMinutes(minutes)), Available()).Keys);

    [Fact]
    public void Sold_and_past_reports_both_errors()
    {
        var errors = Validate(Request(Now.AddDays(-1)), Sold());
        Assert.Contains("vehicle", errors.Keys);
        Assert.Contains("scheduledAt", errors.Keys);
    }

    [Fact]
    public void Overlapping_slot_on_same_vehicle_is_rejected()
    {
        var at = Now.AddDays(1);
        Assert.Contains("scheduledAt", Validate(Request(at), Available(), at.AddMinutes(30)).Keys);
    }

    [Fact]
    public void Adjacent_slot_exactly_one_hour_later_is_accepted()
    {
        var at = Now.AddDays(1);
        Assert.Empty(Validate(Request(at), Available(), at.AddHours(1)));
    }

    [Theory]
    [InlineData("", "jan@example.nl", "0612345678", "name")]
    [InlineData("Jan", "geen-email", "0612345678", "email")]
    [InlineData("Jan", "jan@example.nl", "12345", "phone")]
    public void Missing_or_invalid_contact_details_are_rejected(string name, string email, string tel, string field) =>
        Assert.Contains(field, Validate(new TestDriveRequest(name, email, tel, Now.AddDays(1)), Available()).Keys);

    [Fact]
    public void Unspecified_kind_is_treated_as_utc()
    {
        var unspecified = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Unspecified);
        var utc = TestDriveRules.ToUtc(unspecified);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(unspecified.Ticks, utc.Ticks);
    }

    [Fact]
    public void Missing_time_is_a_field_error() =>
        Assert.Contains("scheduledAt", Validate(new TestDriveRequest("Jan", "jan@example.nl", "0612345678", null), Available()).Keys);

    private static Dictionary<string, string[]> Contact(string? name = "Jan", string? email = "jan@example.nl", string? tel = "0612345678") =>
        Validate(new TestDriveRequest(name, email, tel, Now.AddDays(1)), Available());

    [Theory]
    [InlineData("jan@example.nl")]
    [InlineData("jan.de.vries+proefrit@ames-auto.co.uk")]
    [InlineData("  jan@example.nl  ")]
    public void Valid_email_is_accepted(string email) => Assert.Empty(Contact(email: email));

    [Theory]
    [InlineData("Jan <jan@example.nl>")]
    [InlineData("jan@example")]
    [InlineData("jan@@example.nl")]
    [InlineData("jan example@example.nl")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_email_is_rejected(string? email) => Assert.Contains("email", Contact(email: email).Keys);

    [Theory]
    [InlineData("06 12345678")]
    [InlineData("+31 (0)6-12345678")]
    public void Valid_phone_is_accepted(string tel) => Assert.Empty(Contact(tel: tel));

    [Theory]
    [InlineData("abc 0612345678")]
    [InlineData("06123")]
    [InlineData("0612345678901234567")]
    public void Invalid_phone_is_rejected(string tel) => Assert.Contains("phone", Contact(tel: tel).Keys);

    [Fact]
    public void Values_longer_than_the_database_columns_are_rejected()
    {
        Assert.Contains("name", Contact(name: new string('a', Limits.Name + 1)).Keys);
        Assert.Contains("email", Contact(email: new string('a', Limits.Email) + "@example.nl").Keys);
        Assert.Contains("phone", Contact(tel: "06" + new string(' ', Limits.Phone) + "12345678").Keys);
        Assert.Empty(Contact(name: new string('a', Limits.Name)));
    }

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(11, 8, 10, "Sunday")]
    [InlineData(11, 4, 7, "before opening")]
    [InlineData(11, 4, 16, "runs past closing")]
    [InlineData(11, 7, 15, "Saturday after 16:00")]
    public void Slot_outside_opening_hours_is_rejected(int month, int day, int utcHour, string because)
    {
        var errors = Validate(Request(Utc(month, day, utcHour)), Available());
        Assert.True(errors.ContainsKey("scheduledAt"), because);
    }

    [Theory]
    [InlineData(11, 4, 8, 0)]
    [InlineData(11, 4, 15, 0)]
    [InlineData(11, 7, 8, 0)]
    public void Slot_inside_opening_hours_is_accepted(int month, int day, int utcHour, int utcMinute) =>
        Assert.Empty(Validate(Request(Utc(month, day, utcHour, utcMinute)), Available()));

    [Fact]
    public void Opening_hours_use_dutch_time_including_daylight_saving()
    {
        Assert.True(Hours.Contains(Utc(6, 3, 7), TimeSpan.FromHours(1)));
        Assert.False(Hours.Contains(Utc(12, 2, 7), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Describe_groups_days_with_equal_hours() =>
        Assert.Equal("ma–vr 08:30–17:30, za 09:00–16:00", Hours.Describe());

    [Fact]
    public void Slots_on_a_weekday_are_whole_hours_that_fit_before_closing()
    {
        var slots = Hours.SlotsOn(new DateOnly(2026, 11, 4), TestDriveRules.SlotLength)
            .Select(s => TimeZoneInfo.ConvertTimeFromUtc(s, Hours.TimeZone).ToString("HH:mm"));
        Assert.Equal(["09:00", "10:00", "11:00", "12:00", "13:00", "14:00", "15:00", "16:00"], slots);
    }

    [Fact]
    public void Slots_are_in_utc_for_the_dutch_date() =>
        Assert.Equal(new DateTime(2026, 11, 7, 8, 0, 0, DateTimeKind.Utc),
            Hours.SlotsOn(new DateOnly(2026, 11, 7), TestDriveRules.SlotLength).First());

    [Fact]
    public void No_slots_on_sunday() =>
        Assert.Empty(Hours.SlotsOn(new DateOnly(2026, 11, 8), TestDriveRules.SlotLength));

    [Fact]
    public void Every_listed_slot_passes_the_rules()
    {
        foreach (var slot in Hours.SlotsOn(new DateOnly(2026, 11, 4), TestDriveRules.SlotLength))
            Assert.Empty(Validate(Request(slot), Available()));
    }
}
