using AutoInventory.Api.Rdw;
using AutoInventory.Core;

namespace AutoInventory.Tests;

public class LicensePlateAndRdwMappingTests
{
    [Theory]
    [InlineData("ab-123-c", "AB123C")]
    [InlineData(" 54 sdk 7 ", "54SDK7")]
    public void Normalize_strips_separators_and_uppercases(string input, string expected) =>
        Assert.Equal(expected, LicensePlate.Normalize(input));

    [Theory]
    [InlineData("AB123C", true)]
    [InlineData("ABCDEF", false)]
    [InlineData("123456", false)]
    [InlineData("AB12", false)]
    public void IsValid_checks_format(string plate, bool expected) =>
        Assert.Equal(expected, LicensePlate.IsValid(plate));

    [Fact]
    public void Rdw_row_is_mapped_to_our_model()
    {
        var v = RdwClient.Map("AB123C", new RdwClient.RdwDto(" TOYOTA ", "YARIS", "20190415"));
        Assert.Equal(new RdwVehicle("AB123C", "Toyota", "YARIS", 2019), v);
    }

    [Fact]
    public void Rdw_row_with_bad_date_maps_year_to_null() =>
        Assert.Null(RdwClient.Map("AB123C", new RdwClient.RdwDto("TOYOTA", "", "onzin")).Year);

    [Theory]
    [InlineData("VOLKSWAGEN", "Volkswagen")]
    [InlineData("SKODA", "Škoda")]
    [InlineData("SEAT", "SEAT")]
    [InlineData("CUPRA", "CUPRA")]
    [InlineData("AUDI", "Audi")]
    [InlineData("LAND ROVER", "Land Rover")]
    public void Brand_names_get_their_official_spelling(string rdw, string expected) =>
        Assert.Equal(expected, Brands.Display(rdw));
}
