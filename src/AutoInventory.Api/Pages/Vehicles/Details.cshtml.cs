using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AutoInventory.Api.Services;
using AutoInventory.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AutoInventory.Api.Pages.Vehicles;

public class DetailsModel(VehicleService vehicles, TestDriveService testDrives) : PageModel
{
    [FromRoute] public int Id { get; set; }

    [BindProperty] public BookingForm Booking { get; set; } = new();

    public VehicleDto Vehicle { get; private set; } = null!;
    public List<SlotDto> Slots { get; private set; } = [];
    public string OpeningHours => testDrives.OpeningHoursText;
    public DateOnly FirstDate => testDrives.TodayInNl;

    [TempData] public string? Message { get; set; }
    [TempData] public string? AdminError { get; set; }

    public class BookingForm
    {
        [Display(Name = "Naam")] public string? Name { get; set; }
        public string? Email { get; set; }
        [Display(Name = "Telefoon")] public string? Phone { get; set; }
        [Display(Name = "Datum")] public DateOnly? Date { get; set; }
        public string? Slot { get; set; }
    }

    public async Task<IActionResult> OnGetAsync([FromQuery(Name = "datum")] DateOnly? date, CancellationToken ct)
    {
        if (!await LoadVehicleAsync(ct)) return NotFound();
        Booking.Date = date ?? FirstDate;
        await LoadSlotsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostShowTimesAsync(CancellationToken ct)
    {
        if (!await LoadVehicleAsync(ct)) return NotFound();
        ModelState.Clear();
        Booking.Slot = null;
        await LoadSlotsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostBookAsync(CancellationToken ct)
    {
        if (!await LoadVehicleAsync(ct)) return NotFound();

        DateTime? start = DateTime.TryParse(Booking.Slot, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var s) ? s : null;
        var result = await testDrives.BookAsync(Id, new TestDriveRequest(Booking.Name, Booking.Email, Booking.Phone, start), ct);

        switch (result.Kind)
        {
            case ResultKind.Ok:
                var nl = testDrives.ToDutchTime(result.Value!.ScheduledAt);
                Message = $"Bedankt! Je proefrit op {Format.Date(DateOnly.FromDateTime(nl))} om {nl:HH:mm} "
                        + "is aangevraagd. We nemen snel contact met je op.";
                return RedirectToPage(new { id = Id, datum = DateOnly.FromDateTime(nl) });
            case ResultKind.NotFound:
                return NotFound();
            case ResultKind.Invalid:
                AddErrors(result.Errors!);
                break;
            default:
                ModelState.AddModelError(string.Empty, result.Message!);
                Booking.Slot = null;
                break;
        }

        await LoadSlotsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostMarkSoldAsync(int version, CancellationToken ct)
    {
        var result = await vehicles.MarkSoldAsync(Id, version, ct);
        if (result.Kind == ResultKind.NotFound) return NotFound();
        if (result.IsOk) Message = "Gemarkeerd als verkocht.";
        else AdminError = result.Message ?? "Opslaan is niet gelukt.";
        return RedirectToPage(new { id = Id });
    }

    private async Task<bool> LoadVehicleAsync(CancellationToken ct)
    {
        var result = await vehicles.GetAsync(Id, ct);
        if (result.IsOk) Vehicle = result.Value!;
        return result.IsOk;
    }

    private async Task LoadSlotsAsync(CancellationToken ct)
    {
        Booking.Date ??= FirstDate;
        if (Booking.Date < FirstDate) Booking.Date = FirstDate;
        Slots = (await testDrives.AvailabilityAsync(Id, Booking.Date.Value, ct)).Value ?? [];
    }

    private void AddErrors(IDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            var key = field switch
            {
                "name" => "Booking.Name",
                "email" => "Booking.Email",
                "phone" => "Booking.Phone",
                "scheduledAt" => "Booking.Slot",
                _ => string.Empty,
            };
            foreach (var m in messages) ModelState.AddModelError(key, m);
        }
    }
}
