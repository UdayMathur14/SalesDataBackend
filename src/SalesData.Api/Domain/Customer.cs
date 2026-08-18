namespace SalesData.Api.Domain;

public sealed class Customer
{
    public int Id { get; set; }
    public string? CustomerCode { get; set; }
    public string CompanyName { get; set; } = null!;
    public string CustomerEmail { get; set; } = null!;
    public string? EmailDomain { get; set; }
    public string ContactPerson { get; set; } = null!;
    public string? CustomerContactNumber1 { get; set; }
    public string? CustomerContactNumber2 { get; set; }
    public string? CustomerContactNumber3 { get; set; }
    public string CountryCode { get; set; } = null!;
    public string Country { get; set; } = null!;
    public string? State { get; set; }
    public string? City { get; set; }
    public string? Category { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
