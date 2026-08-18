using SalesData.Api.Contracts;

namespace SalesData.Api.Services;

public interface ICustomerService
{
    Task<PagedResult<CustomerResponse>> SearchAsync(string? search, string? category, string? country, int page, int pageSize, CancellationToken ct);
    Task<CustomerResponse?> GetByIdAsync(int id, CancellationToken ct);
    Task<CustomerResponse> CreateAsync(CustomerRequest request, CancellationToken ct);
    Task<CustomerResponse?> UpdateAsync(int id, CustomerRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
    Task<ImportResult> ImportAsync(Stream excel, string actor, CancellationToken ct);
    byte[] BuildTemplate();
}
