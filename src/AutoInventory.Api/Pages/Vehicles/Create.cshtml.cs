using System.ComponentModel.DataAnnotations;
using AutoInventory.Api.Rdw;
using AutoInventory.Api.Services;
using AutoInventory.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AutoInventory.Api.Pages.Vehicles;

public class CreateModel(VehicleService vehicles) : PageModel
{
    [BindProperty] public AddForm Input { get; set; } = new();

    public RdwVehicle? Rdw { get; private set; }

    public class AddForm
    {
        [Display(Name = "Kenteken")] public string? LicensePlate { get; set; }
        [Display(Name = "Kilometerstand")] public int? Mileage { get; set; }
        [Display(Name = "Brandstof")] public FuelType? Fuel { get; set; }
        [Display(Name = "Prijs")] public decimal? Price { get; set; }
        [Display(Name = "Vestiging")] public string? Branch { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostLookupAsync(CancellationToken ct)
    {
        ModelState.Clear();
        await LookupAsync(prefillFuel: true, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (Input.Mileage is null) ModelState.AddModelError("Input.Mileage", "Vul de kilometerstand in.");
        if (Input.Price is null) ModelState.AddModelError("Input.Price", "Vul de prijs in.");
        if (Input.Fuel is null) ModelState.AddModelError("Input.Fuel", "Kies een brandstof.");

        if (ModelState.IsValid)
        {
            var result = await vehicles.AddAsync(new VehicleInput(
                Input.LicensePlate, Input.Mileage!.Value, Input.Fuel!.Value, Input.Price!.Value, Input.Branch), ct);

            if (result.IsOk)
            {
                TempData["Message"] = $"{result.Value!.Brand} {result.Value.Model} is toegevoegd aan de voorraad.";
                return RedirectToPage("Details", new { id = result.Value.Id });
            }

            if (result.Kind == ResultKind.Invalid)
            {
                foreach (var (field, messages) in result.Errors!)
                    foreach (var m in messages)
                        ModelState.AddModelError(field switch
                        {
                            "licensePlate" => "Input.LicensePlate",
                            "mileage" => "Input.Mileage",
                            "fuel" => "Input.Fuel",
                            "price" => "Input.Price",
                            "branch" => "Input.Branch",
                            _ => string.Empty,
                        }, m);
            }
            else
            {
                ModelState.AddModelError("Input.LicensePlate", result.Message!);
            }
        }

        await LookupAsync(prefillFuel: false, ct, reportErrors: false);
        return Page();
    }

    private async Task LookupAsync(bool prefillFuel, CancellationToken ct, bool reportErrors = true)
    {
        var result = await vehicles.LookupRdwAsync(Input.LicensePlate, ct);
        if (result.IsOk)
        {
            Rdw = result.Value;
            Input.LicensePlate = Format.Plate(Rdw!.LicensePlate);
            if (prefillFuel && Rdw.Fuel is { } fuel) Input.Fuel = fuel;
        }
        else if (reportErrors)
        {
            ModelState.AddModelError("Input.LicensePlate",
                result.Kind == ResultKind.Invalid ? result.Errors!["licensePlate"][0] : result.Message!);
        }
    }
}
