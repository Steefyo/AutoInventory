using AutoInventory.Api.Data;
using AutoInventory.Core;
using Microsoft.EntityFrameworkCore;

namespace AutoInventory.Api.Services;

public record SlotDto(DateTime Start, string Time, bool Free);

public record TestDriveDto(int Id, int VehicleId, string Name, string Email, string Phone, DateTime ScheduledAt);

public class TestDriveService(AppDbContext db, OpeningHours hours, TimeProvider clock, ILogger<TestDriveService> log)
{
    public string OpeningHoursText => hours.Describe();

    public DateOnly TodayInNl => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, hours.TimeZone));

    public async Task<ServiceResult<List<SlotDto>>> AvailabilityAsync(int vehicleId, DateOnly date, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is null) return VehicleService.NotFound<List<SlotDto>>(vehicleId);

        var now = clock.GetUtcNow().UtcDateTime;
        var slots = hours.SlotsOn(date, TestDriveRules.SlotLength).Where(s => s > now).ToList();
        if (slots.Count == 0 || vehicle.Status == VehicleStatus.Sold)
            return ServiceResult<List<SlotDto>>.Ok([]);

        var booked = await BookingsNear(vehicleId, slots[0], slots[^1], ct);

        return ServiceResult<List<SlotDto>>.Ok(slots.Select(s => new SlotDto(
            s,
            TimeZoneInfo.ConvertTimeFromUtc(s, hours.TimeZone).ToString("HH:mm"),
            !booked.Any(b => TestDriveRules.Overlaps(b, s)))).ToList());
    }

    public async Task<ServiceResult<TestDriveDto>> BookAsync(int vehicleId, TestDriveRequest request, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is null) return VehicleService.NotFound<TestDriveDto>(vehicleId);

        DateTime? scheduledUtc = request.ScheduledAt is { } g ? TestDriveRules.ToUtc(g) : null;
        var nearby = scheduledUtc is { } at ? await BookingsNear(vehicleId, at, at, ct) : [];

        var errors = TestDriveRules.Validate(request, vehicle, clock.GetUtcNow().UtcDateTime, nearby, hours);
        if (errors.Count > 0)
        {
            log.LogInformation("Test drive for vehicle {VehicleId} rejected: {@Errors}", vehicleId, errors);
            return ServiceResult<TestDriveDto>.Invalid(errors);
        }

        var testDrive = new TestDrive
        {
            VehicleId = vehicleId,
            Name = request.Name!.Trim(),
            Email = request.Email!.Trim(),
            Phone = request.Phone!.Trim(),
            ScheduledAt = scheduledUtc!.Value,
            RequestedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.TestDrives.Add(testDrive);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Test drive {TestDriveId} booked for vehicle {VehicleId} at {ScheduledAt:o}", testDrive.Id, vehicleId, testDrive.ScheduledAt);
        return ServiceResult<TestDriveDto>.Ok(
            new TestDriveDto(testDrive.Id, vehicleId, testDrive.Name, testDrive.Email, testDrive.Phone, testDrive.ScheduledAt));
    }

    public DateTime ToDutchTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, hours.TimeZone);

    private Task<List<DateTime>> BookingsNear(int vehicleId, DateTime firstUtc, DateTime lastUtc, CancellationToken ct)
    {
        var from = firstUtc - TestDriveRules.SlotLength;
        var to = lastUtc + TestDriveRules.SlotLength;
        return db.TestDrives
            .Where(t => t.VehicleId == vehicleId && t.ScheduledAt > from && t.ScheduledAt < to)
            .Select(t => t.ScheduledAt).ToListAsync(ct);
    }
}
