using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AutoInventory.Tests;

[Collection(AppCollection.Name)]
public partial class PageTests(ApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public Task InitializeAsync() => factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string Wednesday11Slot = "2026-11-04T10:00:00.0000000Z";

    private async Task<HttpResponseMessage> PostFormAsync(string pageUrl, string handler, Dictionary<string, string> fields)
    {
        var html = await (await _client.GetAsync(pageUrl)).Content.ReadAsStringAsync();
        fields["__RequestVerificationToken"] = TokenPattern().Match(html).Groups[1].Value;
        var separator = pageUrl.Contains('?') ? '&' : '?';
        return await _client.PostAsync($"{pageUrl}{separator}handler={handler}", new FormUrlEncodedContent(fields));
    }

    private static Dictionary<string, string> Booking(string email = "jan@example.nl", string slot = Wednesday11Slot) => new()
    {
        ["Booking.Name"] = "Jan Jansen",
        ["Booking.Email"] = email,
        ["Booking.Phone"] = "06 12345678",
        ["Booking.Date"] = "2026-11-04",
        ["Booking.Slot"] = slot,
    };

    private static Dictionary<string, string> EditForm(int version, string price) => new()
    {
        ["Input.Mileage"] = "70000",
        ["Input.Fuel"] = "Petrol",
        ["Input.Price"] = price,
        ["Input.Branch"] = "Sliedrecht",
        ["Input.Status"] = "Available",
        ["Input.Version"] = version.ToString(),
    };

    [Fact]
    public async Task Overview_shows_only_the_filtered_cars()
    {
        var html = await _client.GetStringAsync("/?merk=Škoda");
        Assert.Contains("OCTAVIA COMBI", html);
        Assert.DoesNotContain("GOLF", html);
    }

    [Fact]
    public async Task Detail_lists_the_slots_for_the_chosen_date()
    {
        var html = await _client.GetStringAsync("/voertuig/1?datum=2026-11-04");
        Assert.Contains(Wednesday11Slot, html);
        Assert.Contains("Proefritten: ma–vr 08:30–17:30", html);
    }

    [Fact]
    public async Task Invalid_booking_shows_the_error_next_to_the_field_and_keeps_the_input()
    {
        var r = await PostFormAsync("/voertuig/1", "Book", Booking(email: "Jan <jan@example.nl>"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("Vul een geldig e-mailadres in", html);
        Assert.Contains("value=\"Jan Jansen\"", html);
    }

    [Fact]
    public async Task Valid_booking_redirects_and_shows_the_confirmation_once()
    {
        var r = await PostFormAsync("/voertuig/1", "Book", Booking());

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var page = await _client.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("Bedankt! Je proefrit op woensdag 4 november om 11:00", page);
        Assert.Contains("11:00 (bezet)", WebUtility.HtmlDecode(page).Replace("\n", " ").Replace("  ", " "));

        var again = await _client.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.DoesNotContain("Bedankt!", again);
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_refused()
    {
        var r = await _client.PostAsync("/voertuig/1?handler=Book", new FormUrlEncodedContent(Booking()));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Adding_a_vehicle_through_the_form_takes_brand_and_model_from_rdw()
    {
        var r = await PostFormAsync("/voertuig/toevoegen", "Save", new()
        {
            ["Input.LicensePlate"] = "sk-123-x",
            ["Input.Mileage"] = "30000",
            ["Input.Fuel"] = "Petrol",
            ["Input.Price"] = "21950.50",
            ["Input.Branch"] = "Ridderkerk",
        });

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var page = await _client.GetStringAsync(r.Headers.Location!.OriginalString);
        Assert.Contains("Škoda KAMIQ is toegevoegd", WebUtility.HtmlDecode(page));
        Assert.Contains("€ 21.950,50", WebUtility.HtmlDecode(page));
    }

    [Fact]
    public async Task Unknown_plate_is_reported_on_the_plate_field()
    {
        var r = await PostFormAsync("/voertuig/toevoegen", "Lookup", new() { ["Input.LicensePlate"] = "ZZ-999-Z" });
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("niet bekend bij de RDW", html);
        Assert.DoesNotContain("id=\"save\"", html);
    }

    [Fact]
    public async Task Stale_mark_as_sold_shows_a_message_and_changes_nothing()
    {
        var r = await PostFormAsync("/voertuig/1", "MarkSold", new() { ["version"] = "99" });

        var page = WebUtility.HtmlDecode(await _client.GetStringAsync(r.Headers.Location!.OriginalString));
        Assert.Contains("intussen door iemand anders gewijzigd", page);
        Assert.Contains("Beschikbaar", page);
    }

    [Fact]
    public async Task Editing_a_vehicle_saves_and_redirects_to_the_detail_page()
    {
        var r = await PostFormAsync("/voertuig/1/wijzigen", "Save", EditForm(version: 0, price: "18950"));

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/voertuig/1", r.Headers.Location!.OriginalString);
        var page = WebUtility.HtmlDecode(await _client.GetStringAsync(r.Headers.Location!.OriginalString));
        Assert.Contains("De wijzigingen zijn opgeslagen.", page);
        Assert.Contains("€ 18.950", page);
        Assert.Contains("Ames Sliedrecht", page);
    }

    [Fact]
    public async Task Stale_edit_shows_the_conflict_and_saves_nothing()
    {
        await PostFormAsync("/voertuig/1/wijzigen", "Save", EditForm(version: 0, price: "18950"));

        var r = await PostFormAsync("/voertuig/1/wijzigen", "Save", EditForm(version: 0, price: "17950"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Contains("intussen door iemand anders gewijzigd", html);
        Assert.Contains("value=\"17950\"", html);
        var detail = WebUtility.HtmlDecode(await _client.GetStringAsync("/voertuig/1"));
        Assert.Contains("€ 18.950", detail);
        Assert.DoesNotContain("€ 17.950", detail);
    }

    [Fact]
    public async Task Deleting_a_vehicle_returns_to_the_overview_with_a_message()
    {
        var r = await PostFormAsync("/voertuig/3", "Delete", new());

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/", r.Headers.Location!.OriginalString);
        var overview = WebUtility.HtmlDecode(await _client.GetStringAsync("/"));
        Assert.Contains("Škoda OCTAVIA COMBI is verwijderd uit de voorraad.", overview);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/voertuig/3")).StatusCode);
    }

    [Fact]
    public async Task Vehicle_with_a_planned_test_drive_is_not_deleted()
    {
        await PostFormAsync("/voertuig/1", "Book", Booking());

        var r = await PostFormAsync("/voertuig/1", "Delete", new());

        Assert.Equal("/voertuig/1", r.Headers.Location!.OriginalString);
        var page = WebUtility.HtmlDecode(await _client.GetStringAsync(r.Headers.Location!.OriginalString));
        Assert.Contains("Er staan nog 1 proefrit(ten) gepland voor dit voertuig.", page);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/voertuig/1")).StatusCode);
    }

    [Fact]
    public async Task Unknown_vehicle_gets_a_friendly_404_page()
    {
        var r = await _client.GetAsync("/voertuig/999");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("Niet gevonden", await r.Content.ReadAsStringAsync());
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)" """)]
    private static partial Regex TokenPattern();
}
