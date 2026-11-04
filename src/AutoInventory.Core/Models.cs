namespace AutoInventory.Core;

public enum FuelType { Petrol, Diesel, Electric, Hybrid, Lpg }

public enum VehicleStatus { Available, Sold }

public class Vehicle
{
    public int Id { get; set; }
    public string LicensePlate { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public int? Year { get; set; }
    public int Mileage { get; set; }
    public FuelType Fuel { get; set; }
    public decimal Price { get; set; }
    public VehicleStatus Status { get; set; } = VehicleStatus.Available;
    public string Branch { get; set; } = "";
    public int Version { get; set; }
    public List<TestDrive> TestDrives { get; set; } = [];
}

public class TestDrive
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateTime ScheduledAt { get; set; }
    public DateTime RequestedAt { get; set; }
}
