using AutoInventory.Api.Services;
using AutoInventory.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AutoInventory.Api.Pages;

public class IndexModel(VehicleService vehicles) : PageModel
{
    public const int PageSize = 6;

    [BindProperty(Name = "merk", SupportsGet = true)] public string? Brand { get; set; }
    [BindProperty(Name = "vestiging", SupportsGet = true)] public string? Branch { get; set; }
    [BindProperty(Name = "brandstof", SupportsGet = true)] public FuelType? Fuel { get; set; }
    [BindProperty(SupportsGet = true)] public VehicleStatus? Status { get; set; }
    [BindProperty(Name = "sorteer", SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(Name = "pagina", SupportsGet = true)] public int PageNumber { get; set; } = 1;

    public PagedResult<VehicleDto> Vehicles { get; private set; } = new([], 1, PageSize, 0);
    public BrandsDto BrandOptions { get; private set; } = new([], []);

    [TempData] public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var result = await vehicles.SearchAsync(new VehicleFilter(Brand, Branch, Fuel, Status), Sort, PageNumber, PageSize, ct);
        if (!result.IsOk) return RedirectToPage(new { merk = Brand, vestiging = Branch, brandstof = Fuel, status = Status });
        if (result.Value!.Items.Count == 0 && PageNumber > 1) return RedirectToPage(RouteFor(1));

        Vehicles = result.Value;
        BrandOptions = await vehicles.BrandsAsync(ct);
        return Page();
    }

    public object RouteFor(int page) => new
    {
        merk = Brand, vestiging = Branch, brandstof = Fuel, status = Status, sorteer = Sort,
        pagina = page > 1 ? page : (int?)null,
    };
}
