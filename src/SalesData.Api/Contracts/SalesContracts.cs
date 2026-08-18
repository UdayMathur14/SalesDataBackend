using System.ComponentModel.DataAnnotations;

namespace SalesData.Api.Contracts;

public enum SalesRecordType { All, Clean, Blocked }
public enum SalesImportMode { Standard, Event }
public enum SalesExportFormat { Xlsx, Csv }

public sealed record SalesLeadRequest(
    [Required, MaxLength(250)] string CompanyName,
    [Required, MaxLength(200)] string ContactPerson,
    [MaxLength(30)] string? CustomerContactNumber1,
    [MaxLength(320)] string? CustomerEmail,
    [MaxLength(10)] string? CountryCode,
    [MaxLength(100)] string? Country,
    [MaxLength(30)] string? CustomerContactNumber2,
    [MaxLength(30)] string? CustomerContactNumber3,
    [MaxLength(100)] string? State,
    [MaxLength(100)] string? City,
    [Required, MaxLength(50)] string Category,
    [Required, MaxLength(100)] string Actor,
    [MaxLength(200)] string? EventName,
    SalesImportMode Mode = SalesImportMode.Standard);

public sealed record SalesLeadResponse(
    int Id, SalesRecordType RecordType, string? CustomerCode, string? CompanyName,
    string? ContactPerson, string? CustomerContactNumber1, string? CustomerContactNumber2,
    string? CustomerContactNumber3, string? CustomerEmail, string? EmailDomain,
    string? CountryCode, string? Country, string? State, string? City, string? Category,
    string? CreatedBy, DateTime? CreatedOn, int? SalesPersonId,
    string? BlockedBy, string? BlockReason, string? Released, string? ReleasedBy,
    string? ReleasedOn, string? EventName);

public sealed record SalesSearchRequest(
    string? Search = null, string? Category = null, string? Event = null,
    string? UserName = null, DateTime? SelectedDate = null, DateTime? FromDate = null,
    DateTime? ToDate = null, SalesRecordType RecordType = SalesRecordType.All,
    int Page = 1, int PageSize = 50);

public sealed record SalesSearchResult(
    IReadOnlyList<SalesLeadResponse> CleanItems, IReadOnlyList<SalesLeadResponse> BlockedItems,
    int CleanTotalCount, int BlockedTotalCount, int Page, int PageSize);

public sealed record SalesImportResult(
    int CleanCount, int BlockedCount, int InvalidCount,
    IReadOnlyList<SalesLeadResponse> CleanRecords,
    IReadOnlyList<SalesLeadResponse> BlockedRecords,
    IReadOnlyList<ImportError> InvalidRecords);

public sealed record CompanyLocationResult(string CompanyName, string Module, string Status, string? HandledBy);

public sealed record SalesFilterOptions(IReadOnlyList<string> Categories, IReadOnlyList<string> Events, IReadOnlyList<string> Users);
