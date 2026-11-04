namespace AutoInventory.Core;

/// <summary>
/// Maximum field lengths, defined once. Used by the validation rules AND by the EF column sizes
/// (AppDbContext), so the API can never accept something the database will then reject with a 500.
/// The HTML maxlength attributes mirror these numbers for quick feedback only.
/// </summary>
public static class Limits
{
    public const int Name = 100;
    public const int Email = 200;
    public const int Phone = 30;
    public const int Brand = 100;
    public const int Model = 100;
    public const int Branch = 30;
    public const int LicensePlate = 6;
}
