using AutoInventory.Api.Data;
using AutoInventory.Api.Rdw;
using AutoInventory.Core;
using Microsoft.EntityFrameworkCore;

namespace AutoInventory.Api.Services;

public record VehicleDto(int Id, string LicensePlate, string Brand, string Model, int? Year, int Mileage,
    FuelType Fuel, decimal Price, VehicleStatus Status, string Branch, int Version)
{
    public static VehicleDto From(Vehicle v) =>
        new(v.Id, v.LicensePlate, v.Brand, v.Model, v.Year, v.Mileage, v.Fuel, v.Price, v.Status, v.Branch, v.Version);
}

public record VehicleFilter(string? Brand = null, string? Branch = null, FuelType? Fuel = null, VehicleStatus? Status = null);

public static class VehicleSort
{
    public const string Brand = "brand";
    public const string Price = "price";
    public const string PriceDesc = "price_desc";
    public const string Mileage = "mileage";
    public const string YearDesc = "year_desc";

    public static readonly IReadOnlyList<string> All = [Brand, Price, PriceDesc, Mileage, YearDesc];
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
}

public record BrandsDto(IReadOnlyList<string> DealerBrands, IReadOnlyList<string> Others);

public class VehicleService(AppDbContext db, IRdwClient rdw, TimeProvider clock, ILogger<VehicleService> log)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public async Task<ServiceResult<PagedResult<VehicleDto>>> SearchAsync(VehicleFilter f, string? sort, int page, int pageSize,
        CancellationToken ct = default)
    {
        sort = string.IsNullOrWhiteSpace(sort) ? VehicleSort.Brand : sort;
        var errors = new Dictionary<string, string[]>();
        if (!VehicleSort.All.Contains(sort))
            errors["sort"] = [$"Onbekende sortering. Kies uit: {string.Join(", ", VehicleSort.All)}."];
        if (page < 1)
            errors["page"] = ["Pagina moet 1 of hoger zijn."];
        if (pageSize is < 1 or > MaxPageSize)
            errors["pageSize"] = [$"Aantal per pagina moet tussen 1 en {MaxPageSize} liggen."];
        if (errors.Count > 0) return ServiceResult<PagedResult<VehicleDto>>.Invalid(errors);

        var q = db.Vehicles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(f.Brand)) q = q.Where(v => v.Brand == f.Brand.Trim());
        if (!string.IsNullOrWhiteSpace(f.Branch)) q = q.Where(v => v.Branch == f.Branch);
        if (f.Fuel is not null) q = q.Where(v => v.Fuel == f.Fuel);
        if (f.Status is not null) q = q.Where(v => v.Status == f.Status);

        var total = await q.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        var list = skip >= total ? [] : await Sorted(q, sort).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return ServiceResult<PagedResult<VehicleDto>>.Ok(new(list.Select(VehicleDto.From).ToList(), page, pageSize, total));
    }

    private static IQueryable<Vehicle> Sorted(IQueryable<Vehicle> q, string sort) => sort switch
    {
        VehicleSort.Price => q.OrderBy(v => v.Price).ThenBy(v => v.Id),
        VehicleSort.PriceDesc => q.OrderByDescending(v => v.Price).ThenBy(v => v.Id),
        VehicleSort.Mileage => q.OrderBy(v => v.Mileage).ThenBy(v => v.Id),
        VehicleSort.YearDesc => q.OrderByDescending(v => v.Year).ThenBy(v => v.Id),
        _ => q.OrderBy(v => v.Brand).ThenBy(v => v.Model).ThenBy(v => v.Id),
    };

    public async Task<ServiceResult<VehicleDto>> GetAsync(int id, CancellationToken ct = default) =>
        await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct) is { } v
            ? ServiceResult<VehicleDto>.Ok(VehicleDto.From(v))
            : NotFound<VehicleDto>(id);

    public async Task<BrandsDto> BrandsAsync(CancellationToken ct = default)
    {
        var inStock = await db.Vehicles.Select(v => v.Brand).Distinct().ToListAsync(ct);
        var others = inStock.Except(Brands.DealerBrands, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new BrandsDto(Brands.DealerBrands, others);
    }

    public async Task<ServiceResult<RdwVehicle>> LookupRdwAsync(string? licensePlate, CancellationToken ct = default)
    {
        var plate = LicensePlate.Normalize(licensePlate);
        if (!LicensePlate.IsValid(plate))
            return ServiceResult<RdwVehicle>.Invalid(new Dictionary<string, string[]> { ["licensePlate"] = ["Ongeldig kenteken (verwacht bijv. AB-123-C)."] });

        try
        {
            return await rdw.GetByLicensePlateAsync(plate, ct) is { } v
                ? ServiceResult<RdwVehicle>.Ok(v)
                : ServiceResult<RdwVehicle>.NotFound($"Kenteken {plate} is niet bekend bij de RDW.");
        }
        catch (RdwUnavailableException ex)
        {
            return ServiceResult<RdwVehicle>.Unavailable(ex.Message);
        }
    }

    public async Task<ServiceResult<VehicleDto>> AddAsync(VehicleInput input, CancellationToken ct = default)
    {
        var errors = VehicleRules.Validate(input, isUpdate: false);
        if (errors.Count > 0) return ServiceResult<VehicleDto>.Invalid(errors);

        var plate = LicensePlate.Normalize(input.LicensePlate);
        if (await db.Vehicles.AnyAsync(v => v.LicensePlate == plate, ct)) return AlreadyInStock(plate);

        var lookup = await LookupRdwAsync(plate, ct);
        if (!lookup.IsOk) return lookup.As<VehicleDto>();
        var rdwVehicle = lookup.Value!;

        var vehicle = new Vehicle
        {
            LicensePlate = plate,
            Brand = rdwVehicle.Brand,
            Model = rdwVehicle.Model,
            Year = rdwVehicle.Year,
            Mileage = input.Mileage,
            Fuel = input.Fuel,
            Price = input.Price,
            Status = input.Status,
            Branch = input.Branch!,
        };
        db.Vehicles.Add(vehicle);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            if (!await db.Vehicles.AsNoTracking().AnyAsync(v => v.LicensePlate == plate, ct)) throw;
            log.LogWarning(ex, "Concurrent add of {LicensePlate} refused by the database", plate);
            return AlreadyInStock(plate);
        }

        log.LogInformation("Vehicle {VehicleId} ({LicensePlate}) added", vehicle.Id, plate);
        return ServiceResult<VehicleDto>.Ok(VehicleDto.From(vehicle));
    }

    public async Task<ServiceResult<VehicleDto>> UpdateAsync(int id, VehicleInput input, CancellationToken ct = default)
    {
        var errors = VehicleRules.Validate(input, isUpdate: true);
        if (errors.Count > 0) return ServiceResult<VehicleDto>.Invalid(errors);

        var v = await db.Vehicles.FindAsync([id], ct);
        if (v is null) return NotFound<VehicleDto>(id);

        db.Entry(v).Property(x => x.Version).OriginalValue = input.Version!.Value;
        v.Mileage = input.Mileage;
        v.Fuel = input.Fuel;
        v.Price = input.Price;
        v.Status = input.Status;
        v.Branch = input.Branch!;
        v.Version = input.Version.Value + 1;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            log.LogInformation("Stale update of vehicle {VehicleId} refused (client had version {Version})", id, input.Version);
            return ServiceResult<VehicleDto>.Conflict("Dit voertuig is intussen door iemand anders gewijzigd. Laad de pagina opnieuw.");
        }
        return ServiceResult<VehicleDto>.Ok(VehicleDto.From(v));
    }

    public async Task<ServiceResult<VehicleDto>> MarkSoldAsync(int id, int version, CancellationToken ct = default)
    {
        if (await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct) is not { } v) return NotFound<VehicleDto>(id);
        return await UpdateAsync(id,
            new VehicleInput(v.LicensePlate, v.Mileage, v.Fuel, v.Price, v.Branch, VehicleStatus.Sold, version), ct);
    }

    public async Task<ServiceResult<Done>> DeleteAsync(int id, CancellationToken ct = default)
    {
        if (!await db.Vehicles.AnyAsync(v => v.Id == id, ct)) return NotFound<Done>(id);

        var now = clock.GetUtcNow().UtcDateTime;
        var upcoming = await db.TestDrives.CountAsync(t => t.VehicleId == id && t.ScheduledAt > now, ct);
        if (upcoming > 0)
            return ServiceResult<Done>.Conflict($"Er staan nog {upcoming} proefrit(ten) gepland voor dit voertuig.");

        await db.Vehicles.Where(v => v.Id == id).ExecuteDeleteAsync(ct);
        return ServiceResult<Done>.Ok(default);
    }

    internal static ServiceResult<T> NotFound<T>(int id) => ServiceResult<T>.NotFound($"Voertuig {id} bestaat niet.");

    private static ServiceResult<VehicleDto> AlreadyInStock(string plate) =>
        ServiceResult<VehicleDto>.Conflict($"Kenteken {plate} staat al in de voorraad.");
}
