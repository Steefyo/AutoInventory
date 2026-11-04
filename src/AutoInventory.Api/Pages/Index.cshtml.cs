using AutoInventory.Api.Services;
using AutoInventory.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AutoInventory.Api.Pages;

public class IndexModel(VehicleService vehicles) : PageModel
{
    [BindProperty(Name = "merk", SupportsGet = true)] public string? Brand { get; set; }
    [BindProperty(Name = "vestiging", SupportsGet = true)] public string? Branch { get; set; }
    [BindProperty(Name = "brandstof", SupportsGet = true)] public FuelType? Fuel { get; set; }
    [BindProperty(SupportsGet = true)] public VehicleStatus? Status { get; set; }

    public List<VehicleDto> Vehicles { get; private set; } = [];
    public BrandsDto BrandOptions { get; private set; } = new([], []);

    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        BrandOptions = await vehicles.BrandsAsync(ct);
        Vehicles = await vehicles.SearchAsync(new VehicleFilter(Brand, Branch, Fuel, Status), ct);
    }
}
