using SalesData.Api.Contracts;

namespace SalesData.Api.Services;

public interface ISalesService
{
    Task<SalesSearchResult> SearchAsync(SalesSearchRequest request, CancellationToken ct);
    Task<SalesLeadResponse?> GetAsync(SalesRecordType type, int id, CancellationToken ct);
    Task<SalesLeadResponse> CreateAndClassifyAsync(SalesLeadRequest request, CancellationToken ct);
    Task<SalesLeadResponse?> UpdateCleanAsync(int id, SalesLeadRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(SalesRecordType type, int id, CancellationToken ct);
    Task<SalesImportResult> ImportAsync(Stream excel, SalesImportMode mode, string actor, string? eventName, CancellationToken ct);
    Task<IReadOnlyList<CompanyLocationResult>> VerifyCompanyAsync(string companyName, CancellationToken ct);
    Task<SalesFilterOptions> GetFilterOptionsAsync(string? actor, CancellationToken ct);
    Task<byte[]> ExportXlsxAsync(SalesSearchRequest request, CancellationToken ct);
    Task<Stream> ExportCsvAsync(SalesSearchRequest request, CancellationToken ct);
    byte[] BuildImportResultWorkbook(SalesImportResult result);
    byte[] BuildTemplate(SalesImportMode mode);
}
