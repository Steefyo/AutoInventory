using AutoInventory.Api.Services;
using AutoInventory.Core;

namespace AutoInventory.Api.Endpoints;

public record OpeningHoursDto(string Description);

public static class TestDriveEndpoints
{
    public static void MapTestDriveEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Proefritten");

        api.MapGet("/opening-hours", (TestDriveService svc) => Results.Ok(new OpeningHoursDto(svc.OpeningHoursText)))
            .WithSummary("Wanneer proefritten mogelijk zijn").Produces<OpeningHoursDto>();

        api.MapGet("/vehicles/{id:int}/availability", async (int id, DateOnly date, TestDriveService svc, CancellationToken ct) =>
                (await svc.AvailabilityAsync(id, date, ct)).ToHttp(Results.Ok))
            .WithSummary("Vrije en bezette tijdslots voor één voertuig op één dag")
            .Produces<SlotDto[]>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost("/vehicles/{id:int}/testdrives", async (int id, TestDriveRequest request, TestDriveService svc, CancellationToken ct) =>
                (await svc.BookAsync(id, request, ct)).ToHttp(td => Results.Created((string?)null, td)))
            .WithSummary("Proefrit aanvragen")
            .Produces<TestDriveDto>(StatusCodes.Status201Created).ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }
}
