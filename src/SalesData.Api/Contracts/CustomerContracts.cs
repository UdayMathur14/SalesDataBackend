using System.ComponentModel.DataAnnotations;

namespace SalesData.Api.Contracts;

public sealed record CustomerRequest(
    [Required, MaxLength(50)] string CustomerCode,
    [Required, MaxLength(250)] string CompanyName,
    [Required, EmailAddress, MaxLength(320)] string CustomerEmail,
    [Required, MaxLength(200)] string ContactPerson,
    [MaxLength(30)] string? CustomerContactNumber1,
    [MaxLength(30)] string? CustomerContactNumber2,
    [MaxLength(30)] string? CustomerContactNumber3,
    [Required, MaxLength(10)] string CountryCode,
    [Required, MaxLength(100)] string Country,
    [MaxLength(100)] string? State,
    [MaxLength(100)] string? City,
    [Required, MaxLength(50)] string Category,
    [Required, MaxLength(100)] string Actor);

public sealed record CustomerResponse(
    int Id, string? CustomerCode, string CompanyName, string CustomerEmail,
    string? EmailDomain, string ContactPerson, string? CustomerContactNumber1,
    string? CustomerContactNumber2, string? CustomerContactNumber3,
    string CountryCode, string Country, string? State, string? City,
    string? Category, string? CreatedBy, DateTime? CreatedOn,
    string? ModifiedBy, DateTime? ModifiedOn);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record ImportError(int ExcelRow, string CompanyName, string CustomerEmail, string? CustomerNumber, string ErrorMessage);
public sealed record ImportResult(int InsertedCount, int RejectedCount, IReadOnlyList<ImportError> RejectedRecords);
