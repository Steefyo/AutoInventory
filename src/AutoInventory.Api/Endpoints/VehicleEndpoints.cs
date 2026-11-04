using AutoInventory.Api.Rdw;
using AutoInventory.Api.Services;
using AutoInventory.Core;

namespace AutoInventory.Api.Endpoints;

public static class VehicleEndpoints
{
    public static void MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/rdw/{licensePlate}", async (string licensePlate, VehicleService svc, CancellationToken ct) =>
                (await svc.LookupRdwAsync(licensePlate, ct)).ToHttp(Results.Ok))
            .WithTags("RDW").WithSummary("Merk, model, bouwjaar en brandstof opzoeken bij de RDW")
            .Produces<RdwVehicle>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status502BadGateway);

        api.MapGet("/brands", async (VehicleService svc, CancellationToken ct) => Results.Ok(await svc.BrandsAsync(ct)))
            .WithTags("Opties").WithSummary("Merken voor het filter: dealermerken plus overige merken op voorraad")
            .Produces<BrandsDto>();

        api.MapGet("/branches", () => Results.Ok(Branches.All))
            .WithTags("Opties").WithSummary("De vestigingen van Ames").Produces<string[]>();

        var vehicles = api.MapGroup("/vehicles").WithTags("Voertuigen");

        vehicles.MapGet("/", async (string? brand, string? branch, FuelType? fuel, VehicleStatus? status, string? sort,
                int? page, int? pageSize, VehicleService svc, CancellationToken ct) =>
                (await svc.SearchAsync(new VehicleFilter(brand, branch, fuel, status), sort, page ?? 1,
                    pageSize ?? VehicleService.DefaultPageSize, ct)).ToHttp(Results.Ok))
            .WithSummary("Voorraad opvragen per pagina, optioneel gefilterd en gesorteerd")
            .WithDescription($"sort: {string.Join(", ", VehicleSort.All)}; standaard {VehicleSort.Brand}. "
                + $"pageSize: standaard {VehicleService.DefaultPageSize}, maximaal {VehicleService.MaxPageSize}.")
            .Produces<PagedResult<VehicleDto>>().ProducesValidationProblem();

        vehicles.MapGet("/{id:int}", async (int id, VehicleService svc, CancellationToken ct) =>
                (await svc.GetAsync(id, ct)).ToHttp(Results.Ok))
            .WithSummary("Eén voertuig").Produces<VehicleDto>().ProducesProblem(StatusCodes.Status404NotFound);

        vehicles.MapPost("/", async (VehicleInput input, VehicleService svc, CancellationToken ct) =>
                (await svc.AddAsync(input, ct)).ToHttp(v => Results.Created($"/api/vehicles/{v.Id}", v)))
            .WithSummary("Voertuig toevoegen; merk, model en bouwjaar komen van de RDW")
            .Produces<VehicleDto>(StatusCodes.Status201Created).ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        vehicles.MapPut("/{id:int}", async (int id, VehicleInput input, VehicleService svc, CancellationToken ct) =>
                (await svc.UpdateAsync(id, input, ct)).ToHttp(Results.Ok))
            .WithSummary("Voertuig wijzigen (met versie-controle)")
            .Produces<VehicleDto>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        vehicles.MapDelete("/{id:int}", async (int id, VehicleService svc, CancellationToken ct) =>
                (await svc.DeleteAsync(id, ct)).ToHttp(_ => Results.NoContent()))
            .WithSummary("Voertuig verwijderen (niet zolang er proefritten gepland staan)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }
}
