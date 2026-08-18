namespace SalesData.Api.Domain;

public sealed class CleanProspect
{
    public int Id { get; set; }
    public string? CustomerCode { get; set; }
    public string? CompanyName { get; set; }
    public string? ContactPerson { get; set; }
    public string? CustomerContactNumber1 { get; set; }
    public string? CustomerContactNumber2 { get; set; }
    public string? CustomerContactNumber3 { get; set; }
    public string? CustomerEmail { get; set; }
    public string? EmailDomain { get; set; }
    public string? CountryCode { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string? Category { get; set; }
    public int SalesPersonId { get; set; } = 1;
    public string? CreatedBy { get; set; }
    public DateTime? CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string? EventName { get; set; }
}
