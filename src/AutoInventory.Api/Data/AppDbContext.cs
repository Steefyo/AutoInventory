using AutoInventory.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AutoInventory.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<TestDrive> TestDrives => Set<TestDrive>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c) =>
        c.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Vehicle>(e =>
        {
            e.Property(v => v.LicensePlate).HasMaxLength(Limits.LicensePlate).IsRequired();
            e.HasIndex(v => v.LicensePlate).IsUnique();
            e.Property(v => v.Brand).HasMaxLength(Limits.Brand).IsRequired();
            e.Property(v => v.Model).HasMaxLength(Limits.Model).IsRequired();
            e.Property(v => v.Price).HasPrecision(10, 2);
            e.Property(v => v.Fuel).HasConversion<string>().HasMaxLength(20);
            e.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(v => v.Brand);
            e.Property(v => v.Branch).HasMaxLength(Limits.Branch).IsRequired();
            e.Property(v => v.Version).IsConcurrencyToken();
            e.HasIndex(v => v.Branch);

            e.HasData(
                Seed(1, "GV123X", "Volkswagen", "GOLF", 2020, 68_500, FuelType.Petrol, 19_950m, "Dordrecht"),
                Seed(2, "KZ842P", "Audi", "A3 SPORTBACK", 2021, 42_100, FuelType.Petrol, 27_450m, "Ridderkerk"),
                Seed(3, "TN502R", "Škoda", "OCTAVIA COMBI", 2019, 98_000, FuelType.Diesel, 16_900m, "Zwijndrecht"),
                Seed(4, "54SDK7", "SEAT", "IBIZA", 2018, 87_300, FuelType.Petrol, 11_750m, "Sliedrecht", VehicleStatus.Sold),
                Seed(5, "PL671H", "CUPRA", "BORN", 2023, 24_800, FuelType.Electric, 29_950m, "Dordrecht"),
                Seed(6, "RB309N", "Volkswagen", "ID.4", 2022, 36_200, FuelType.Electric, 32_500m, "Oud-Beijerland"),
                Seed(7, "HF884K", "Toyota", "YARIS", 2020, 51_000, FuelType.Hybrid, 15_450m, "'s-Gravendeel"));
        });

        static Vehicle Seed(int id, string licensePlate, string brand, string model, int year, int km, FuelType fuel,
            decimal price, string branch, VehicleStatus status = VehicleStatus.Available) => new()
        {
            Id = id, LicensePlate = licensePlate, Brand = brand, Model = model, Year = year, Mileage = km,
            Fuel = fuel, Price = price, Branch = branch, Status = status,
        };

        b.Entity<TestDrive>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(Limits.Name).IsRequired();
            e.Property(t => t.Email).HasMaxLength(Limits.Email).IsRequired();
            e.Property(t => t.Phone).HasMaxLength(Limits.Phone).IsRequired();
            e.HasIndex(t => new { t.VehicleId, t.ScheduledAt }).IsUnique();
            e.HasOne(t => t.Vehicle).WithMany(v => v.TestDrives)
                .HasForeignKey(t => t.VehicleId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
