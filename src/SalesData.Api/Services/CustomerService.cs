using System.Net.Mail;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SalesData.Api.Contracts;
using SalesData.Api.Domain;
using SalesData.Api.Infrastructure;

namespace SalesData.Api.Services;

public sealed class CustomerService(AppDbContext db) : ICustomerService
{
    private static readonly HashSet<string> IndividualCategories = new(StringComparer.OrdinalIgnoreCase) { "INDIVIDUAL" };

    public async Task<PagedResult<CustomerResponse>> SearchAsync(string? search, string? category, string? country, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        try
        {
            var query = db.Customers.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(x => x.CompanyName.Contains(term) || x.CustomerEmail.Contains(term) || x.ContactPerson.Contains(term) || (x.CustomerCode != null && x.CustomerCode.Contains(term)));
            }
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category.Trim());
            if (!string.IsNullOrWhiteSpace(country)) query = query.Where(x => x.Country == country.Trim());

            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.CreatedOn).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).Select(x => ToResponse(x)).ToListAsync(ct);
            return new PagedResult<CustomerResponse>(items, page, pageSize, total);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The browser canceled an obsolete request (for example, after a filter/page change).
            // Handle it here so the cancellation does not escape user code in the debugger.
            return new PagedResult<CustomerResponse>([], page, pageSize, 0);
        }
    }

    public Task<CustomerResponse?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Customers.AsNoTracking().Where(x => x.Id == id).Select(x => ToResponse(x)).SingleOrDefaultAsync(ct);

    public async Task<CustomerResponse> CreateAsync(CustomerRequest request, CancellationToken ct)
    {
        var normalized = Normalize(request);
        await EnsureUniqueAsync(normalized.CustomerEmail, normalized.CompanyName, normalized.Category, null, ct);
        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            CustomerCode = normalized.CustomerCode, CompanyName = normalized.CompanyName,
            CustomerEmail = normalized.CustomerEmail, EmailDomain = GetDomain(normalized.CustomerEmail),
            ContactPerson = normalized.ContactPerson, CustomerContactNumber1 = normalized.CustomerContactNumber1,
            CustomerContactNumber2 = normalized.CustomerContactNumber2, CustomerContactNumber3 = normalized.CustomerContactNumber3,
            CountryCode = normalized.CountryCode, Country = normalized.Country, State = normalized.State,
            City = normalized.City, Category = normalized.Category, CreatedBy = normalized.Actor,
            CreatedOn = now, ModifiedBy = normalized.Actor, ModifiedOn = now
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return ToResponse(customer);
    }

    public async Task<CustomerResponse?> UpdateAsync(int id, CustomerRequest request, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (customer is null) return null;
        var normalized = Normalize(request);
        await EnsureUniqueAsync(normalized.CustomerEmail, normalized.CompanyName, normalized.Category, id, ct);
        customer.CustomerCode = normalized.CustomerCode; customer.CompanyName = normalized.CompanyName;
        customer.CustomerEmail = normalized.CustomerEmail; customer.EmailDomain = GetDomain(normalized.CustomerEmail);
        customer.ContactPerson = normalized.ContactPerson; customer.CustomerContactNumber1 = normalized.CustomerContactNumber1;
        customer.CustomerContactNumber2 = normalized.CustomerContactNumber2; customer.CustomerContactNumber3 = normalized.CustomerContactNumber3;
        customer.CountryCode = normalized.CountryCode; customer.Country = normalized.Country; customer.State = normalized.State;
        customer.City = normalized.City; customer.Category = normalized.Category; customer.ModifiedBy = normalized.Actor;
        customer.ModifiedOn = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToResponse(customer);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (customer is null) return false;
        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ImportResult> ImportAsync(Stream excel, string actor, CancellationToken ct)
    {
        using var workbook = new XLWorkbook(excel);
        var sheet = workbook.Worksheet(1);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var errors = new List<ImportError>();
        var candidates = new List<Customer>();
        if (lastRow < 3) return new ImportResult(0, 1, [new(0, "", "", null, "Excel file has no data rows; data must start at row 3.")]);

        var commonDomains = (await db.CommonDomains.AsNoTracking().Select(x => x.DomainName.ToLower()).ToListAsync(ct)).ToHashSet();
        var existingCustomers = await db.Customers.AsNoTracking().Select(x => new { x.CustomerEmail, x.CompanyName }).ToListAsync(ct);
        var existingProspects = await db.CleanProspects.AsNoTracking().Select(x => new { x.CustomerEmail, x.CompanyName }).ToListAsync(ct);
        var emails = existingCustomers.Select(x => x.CustomerEmail).Concat(existingProspects.Select(x => x.CustomerEmail)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var companies = existingCustomers.Select(x => x.CompanyName).Concat(existingProspects.Select(x => x.CompanyName)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var row = 3; row <= lastRow; row++)
        {
            var company = sheet.Cell(row, 2).GetString().Trim().ToUpperInvariant();
            var contact = sheet.Cell(row, 3).GetString().Trim();
            var phone1 = sheet.Cell(row, 4).GetString().Trim();
            var email = sheet.Cell(row, 5).GetString().Trim().ToLowerInvariant();
            var countryCode = sheet.Cell(row, 6).GetString().Trim();
            var country = sheet.Cell(row, 7).GetString().Trim().ToUpperInvariant();
            var phone2 = sheet.Cell(row, 8).GetString().Trim();
            var phone3 = sheet.Cell(row, 9).GetString().Trim();
            var state = sheet.Cell(row, 10).GetString().Trim().ToUpperInvariant();
            var city = sheet.Cell(row, 11).GetString().Trim().ToUpperInvariant();
            var category = sheet.Cell(row, 12).GetString().Trim().ToUpperInvariant();
            string? error = !IsValidEmail(email) ? "Invalid email format"
                : new[] { phone1, phone2, phone3 }.Any(x => !IsValidPhone(x)) ? "Phone numbers can contain digits only"
                : string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(contact) || string.IsNullOrWhiteSpace(countryCode) || string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(category) ? "Missing mandatory fields"
                : emails.Contains(email) || (!IndividualCategories.Contains(category) && companies.Contains(company)) ? "Duplicate record found"
                : null;
            if (error is not null) { errors.Add(new(row, company, email, phone1, error)); continue; }

            var domain = GetDomain(email);
            candidates.Add(new Customer { CustomerCode = $"CUST-{Guid.NewGuid():N}"[..17].ToUpperInvariant(), CompanyName = company,
                CustomerEmail = email, EmailDomain = commonDomains.Contains(domain) ? "-" : domain, ContactPerson = contact,
                CustomerContactNumber1 = NullIfEmpty(phone1), CustomerContactNumber2 = NullIfEmpty(phone2), CustomerContactNumber3 = NullIfEmpty(phone3),
                CountryCode = countryCode, Country = country, State = NullIfEmpty(state), City = NullIfEmpty(city), Category = category,
                CreatedBy = actor.Trim(), CreatedOn = DateTime.UtcNow, ModifiedBy = actor.Trim(), ModifiedOn = DateTime.UtcNow });
            emails.Add(email); companies.Add(company);
        }
        if (candidates.Count > 0) { db.Customers.AddRange(candidates); await db.SaveChangesAsync(ct); }
        return new ImportResult(candidates.Count, errors.Count, errors);
    }

    public byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("CustomerTemplate");
        string[] headers = ["", "*CompanyName", "*ContactPerson", "ContactNo1", "*Email", "*CountryCode", "*Country", "ContactNo2", "ContactNo3", "State", "City", "*Category"];
        for (var col = 2; col <= 12; col++) sheet.Cell(1, col).Value = headers[col - 1];
        var examples = new[] { "", "Ennoble IP", "Rajnish Sir", "123456789", "contact@ennobleip.com", "+91", "INDIA", "9876543210", "", "DELHI", "NEW DELHI", "CORPORATE" };
        for (var col = 2; col <= 12; col++) sheet.Cell(2, col).Value = examples[col - 1];
        var header = sheet.Range("B1:L1"); header.Style.Font.Bold = true; header.Style.Font.FontColor = XLColor.Red; header.Style.Fill.BackgroundColor = XLColor.Yellow;
        sheet.Columns(2, 12).AdjustToContents();
        using var output = new MemoryStream(); workbook.SaveAs(output); return output.ToArray();
    }

    private async Task EnsureUniqueAsync(string email, string company, string category, int? excludedId, CancellationToken ct)
    {
        var customerExists = await db.Customers.AnyAsync(x => x.Id != excludedId && (x.CustomerEmail == email || (!IndividualCategories.Contains(category) && x.CompanyName == company)), ct);
        var prospectExists = await db.CleanProspects.AnyAsync(x => x.CustomerEmail == email || (!IndividualCategories.Contains(category) && x.CompanyName == company), ct);
        if (customerExists || prospectExists) throw new CustomerConflictException("Customer email or company already exists in customer/prospect data.");
    }

    private static CustomerRequest Normalize(CustomerRequest x) => x with { CustomerCode = x.CustomerCode.Trim().ToUpperInvariant(), CompanyName = x.CompanyName.Trim().ToUpperInvariant(), CustomerEmail = x.CustomerEmail.Trim().ToLowerInvariant(), ContactPerson = x.ContactPerson.Trim(), CountryCode = x.CountryCode.Trim(), Country = x.Country.Trim().ToUpperInvariant(), State = NullIfEmpty(x.State)?.ToUpperInvariant(), City = NullIfEmpty(x.City)?.ToUpperInvariant(), Category = x.Category.Trim().ToUpperInvariant(), Actor = x.Actor.Trim(), CustomerContactNumber1 = NullIfEmpty(x.CustomerContactNumber1), CustomerContactNumber2 = NullIfEmpty(x.CustomerContactNumber2), CustomerContactNumber3 = NullIfEmpty(x.CustomerContactNumber3) };
    private static bool IsValidEmail(string email) { try { var value = new MailAddress(email); return value.Address == email && value.Host.Contains('.'); } catch { return false; } }
    private static bool IsValidPhone(string? value) => string.IsNullOrWhiteSpace(value) || value.All(char.IsDigit);
    private static string GetDomain(string email) => email[(email.LastIndexOf('@') + 1)..].ToLowerInvariant();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static CustomerResponse ToResponse(Customer x) => new(x.Id, x.CustomerCode, x.CompanyName, x.CustomerEmail, x.EmailDomain, x.ContactPerson, x.CustomerContactNumber1, x.CustomerContactNumber2, x.CustomerContactNumber3, x.CountryCode, x.Country, x.State, x.City, x.Category, x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn);
}

public sealed class CustomerConflictException(string message) : Exception(message);
