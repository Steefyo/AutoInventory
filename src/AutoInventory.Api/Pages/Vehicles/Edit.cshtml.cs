using System.ComponentModel.DataAnnotations;
using AutoInventory.Api.Services;
using AutoInventory.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AutoInventory.Api.Pages.Vehicles;

public class EditModel(VehicleService vehicles) : PageModel
{
    [FromRoute] public int Id { get; set; }

    [BindProperty] public EditForm Input { get; set; } = new();

    public VehicleDto Vehicle { get; private set; } = null!;
    public bool Stale { get; private set; }

    public class EditForm
    {
        [Display(Name = "Kilometerstand")] public int? Mileage { get; set; }
        [Display(Name = "Brandstof")] public FuelType? Fuel { get; set; }
        [Display(Name = "Prijs")] public decimal? Price { get; set; }
        [Display(Name = "Vestiging")] public string? Branch { get; set; }
        [Display(Name = "Status")] public VehicleStatus? Status { get; set; }
        public int? Version { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadVehicleAsync(ct)) return NotFound();
        Input = new EditForm
        {
            Mileage = Vehicle.Mileage,
            Fuel = Vehicle.Fuel,
            Price = Vehicle.Price,
            Branch = Vehicle.Branch,
            Status = Vehicle.Status,
            Version = Vehicle.Version,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!await LoadVehicleAsync(ct)) return NotFound();

        if (Input.Mileage is null) ModelState.AddModelError("Input.Mileage", "Vul de kilometerstand in.");
        if (Input.Price is null) ModelState.AddModelError("Input.Price", "Vul de prijs in.");
        if (Input.Fuel is null) ModelState.AddModelError("Input.Fuel", "Kies een brandstof.");
        if (Input.Status is null) ModelState.AddModelError("Input.Status", "Kies een status.");
        if (!ModelState.IsValid) return Page();

        var result = await vehicles.UpdateAsync(Id, new VehicleInput(Vehicle.LicensePlate, Input.Mileage!.Value,
            Input.Fuel!.Value, Input.Price!.Value, Input.Branch, Input.Status!.Value, Input.Version), ct);

        switch (result.Kind)
        {
            case ResultKind.Ok:
                TempData["Message"] = "De wijzigingen zijn opgeslagen.";
                return RedirectToPage("Details", new { id = Id });
            case ResultKind.NotFound:
                return NotFound();
            case ResultKind.Invalid:
                foreach (var (field, messages) in result.Errors!)
                    foreach (var m in messages)
                        ModelState.AddModelError(field switch
                        {
                            "mileage" => "Input.Mileage",
                            "fuel" => "Input.Fuel",
                            "price" => "Input.Price",
                            "branch" => "Input.Branch",
                            "status" => "Input.Status",
                            _ => string.Empty,
                        }, m);
                break;
            case ResultKind.Conflict:
                Stale = true;
                ModelState.AddModelError(string.Empty, result.Message!);
                break;
        }
        return Page();
    }

    private async Task<bool> LoadVehicleAsync(CancellationToken ct)
    {
        var result = await vehicles.GetAsync(Id, ct);
        if (result.IsOk) Vehicle = result.Value!;
        return result.IsOk;
    }
}
