namespace SalesData.Api.Domain;

public sealed class Country
{
    public int Id { get; set; }
    public string CountryName { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
}
