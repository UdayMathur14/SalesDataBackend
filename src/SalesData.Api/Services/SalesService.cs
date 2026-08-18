using System.Net.Mail;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SalesData.Api.Contracts;
using SalesData.Api.Domain;
using SalesData.Api.Infrastructure;

namespace SalesData.Api.Services;

public sealed class SalesService(AppDbContext db) : ISalesService
{
    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
        { "CORPORATE", "LAWFIRM", "LAW FIRM", "UNIVERSITY", "PCT", "INDIVIDUAL" };

    public async Task<SalesSearchResult> SearchAsync(SalesSearchRequest request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var cleanQuery = ApplyFilters(db.CleanProspects.AsNoTracking(), request);
        var blockedQuery = ApplyFilters(db.BlockedProspects.AsNoTracking(), request);
        var cleanTotal = request.RecordType == SalesRecordType.Blocked ? 0 : await cleanQuery.CountAsync(ct);
        var blockedTotal = request.RecordType == SalesRecordType.Clean ? 0 : await blockedQuery.CountAsync(ct);

        var clean = request.RecordType == SalesRecordType.Blocked
            ? []
            : (await cleanQuery.OrderByDescending(x => x.CreatedOn).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(ToResponse).ToList();
        var blocked = request.RecordType == SalesRecordType.Clean
            ? []
            : (await blockedQuery.OrderByDescending(x => x.CreatedOn).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(ToResponse).ToList();
        return new SalesSearchResult(clean, blocked, cleanTotal, blockedTotal, page, pageSize);
    }

    public async Task<SalesLeadResponse?> GetAsync(SalesRecordType type, int id, CancellationToken ct)
    {
        if (type == SalesRecordType.Clean)
        {
            var item = await db.CleanProspects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? null : ToResponse(item);
        }
        if (type == SalesRecordType.Blocked)
        {
            var item = await db.BlockedProspects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? null : ToResponse(item);
        }
        throw new SalesValidationException("Record type must be Clean or Blocked.");
    }

    public async Task<SalesLeadResponse> CreateAndClassifyAsync(SalesLeadRequest request, CancellationToken ct)
    {
        var lead = Normalize(request);
        Validate(lead, request.Mode);
        var commonDomains = await LoadCommonDomainsAsync(ct);
        var customers = await LoadCustomerSnapshotAsync(ct);
        var clean = await LoadCleanSnapshotAsync(ct);
        var classified = Classify(lead, request.Mode, commonDomains, customers, clean);
        if (classified.InvalidReason is not null) throw new SalesValidationException(classified.InvalidReason);
        if (classified.BlockReason is null)
        {
            var entity = ToCleanEntity(lead);
            db.CleanProspects.Add(entity); await db.SaveChangesAsync(ct); return ToResponse(entity);
        }
        var blocked = ToBlockedEntity(lead, classified.BlockedBy!, classified.BlockReason);
        db.BlockedProspects.Add(blocked); await db.SaveChangesAsync(ct); return ToResponse(blocked);
    }

    public async Task<SalesLeadResponse?> UpdateCleanAsync(int id, SalesLeadRequest request, CancellationToken ct)
    {
        var entity = await db.CleanProspects.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return null;
        var lead = Normalize(request);
        Validate(lead, request.Mode);
        entity.CompanyName = lead.CompanyName; entity.ContactPerson = lead.ContactPerson;
        entity.CustomerContactNumber1 = lead.Phone1; entity.CustomerContactNumber2 = lead.Phone2; entity.CustomerContactNumber3 = lead.Phone3;
        entity.CustomerEmail = lead.Email; entity.EmailDomain = lead.Domain; entity.CountryCode = lead.CountryCode;
        entity.Country = lead.Country; entity.State = lead.State; entity.City = lead.City; entity.Category = lead.Category;
        entity.EventName = lead.EventName; entity.ModifiedBy = lead.Actor; entity.ModifiedOn = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(SalesRecordType type, int id, CancellationToken ct)
    {
        if (type == SalesRecordType.Clean)
        {
            var item = await db.CleanProspects.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return false; db.CleanProspects.Remove(item);
        }
        else if (type == SalesRecordType.Blocked)
        {
            var item = await db.BlockedProspects.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return false; db.BlockedProspects.Remove(item);
        }
        else throw new SalesValidationException("Record type must be Clean or Blocked.");
        await db.SaveChangesAsync(ct); return true;
    }

    public async Task<SalesImportResult> ImportAsync(Stream excel, SalesImportMode mode, string actor, string? eventName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new SalesValidationException("Actor is required.");
        if (mode == SalesImportMode.Event && string.IsNullOrWhiteSpace(eventName)) throw new SalesValidationException("Event name is required for event upload.");
        using var workbook = new XLWorkbook(excel);
        var sheet = workbook.Worksheet(1);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2) throw new SalesValidationException("Excel file has no data rows.");

        var commonDomains = await LoadCommonDomainsAsync(ct);
        var customers = await LoadCustomerSnapshotAsync(ct);
        var existingClean = await LoadCleanSnapshotAsync(ct);
        var cleanEntities = new List<CleanProspect>();
        var blockedEntities = new List<BlockedProspect>();
        var errors = new List<ImportError>();
        var eventEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var row = 2; row <= lastRow; row++)
        {
            var parsed = ParseRow(sheet, row, actor, eventName);
            try { Validate(parsed, mode); }
            catch (SalesValidationException ex) { errors.Add(ToError(row, parsed, ex.Message)); continue; }
            if (mode == SalesImportMode.Event && parsed.Email.Length > 0 && !eventEmails.Add(parsed.Email))
            { errors.Add(ToError(row, parsed, "Duplicate email in Excel file.")); continue; }

            var classified = Classify(parsed, mode, commonDomains, customers, existingClean);
            if (classified.InvalidReason is not null) { errors.Add(ToError(row, parsed, classified.InvalidReason)); continue; }
            if (classified.BlockReason is null) cleanEntities.Add(ToCleanEntity(parsed));
            else blockedEntities.Add(ToBlockedEntity(parsed, classified.BlockedBy!, classified.BlockReason));
        }

        if (cleanEntities.Count > 0) db.CleanProspects.AddRange(cleanEntities);
        if (blockedEntities.Count > 0) db.BlockedProspects.AddRange(blockedEntities);
        if (cleanEntities.Count + blockedEntities.Count > 0) await db.SaveChangesAsync(ct);
        return new SalesImportResult(cleanEntities.Count, blockedEntities.Count, errors.Count,
            cleanEntities.Select(ToResponse).ToList(), blockedEntities.Select(ToResponse).ToList(), errors);
    }

    public async Task<IReadOnlyList<CompanyLocationResult>> VerifyCompanyAsync(string companyName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(companyName)) throw new SalesValidationException("Company name cannot be empty.");
        var term = companyName.Trim();
        var sales = await db.CleanProspects.AsNoTracking().Where(x => x.CompanyName != null && x.CompanyName.Contains(term)).Take(5)
            .Select(x => new CompanyLocationResult(x.CompanyName!, "Sales / Lead Module", x.State ?? "Clean", x.CreatedBy)).ToListAsync(ct);
        var customers = await db.Customers.AsNoTracking().Where(x => x.CompanyName.Contains(term)).Take(5)
            .Select(x => new CompanyLocationResult(x.CompanyName, "Customer Module", "Active Customer", null)).ToListAsync(ct);
        return sales.Concat(customers).ToList();
    }

    public async Task<SalesFilterOptions> GetFilterOptionsAsync(string? actor, CancellationToken ct)
    {
        var baseQuery = db.CleanProspects.AsNoTracking();
        var eventQuery = string.IsNullOrWhiteSpace(actor) ? baseQuery : baseQuery.Where(x => x.CreatedBy == actor);
        var categories = await baseQuery.Where(x => x.Category != null && x.Category != "").Select(x => x.Category!).Distinct().OrderBy(x => x).ToListAsync(ct);
        var events = await eventQuery.Where(x => x.EventName != null && x.EventName != "").Select(x => x.EventName!).Distinct().OrderBy(x => x).ToListAsync(ct);
        var users = await baseQuery.Where(x => x.CreatedBy != null && x.CreatedBy != "").Select(x => x.CreatedBy!).Distinct().OrderBy(x => x).ToListAsync(ct);
        return new SalesFilterOptions(categories, events, users);
    }

    public async Task<byte[]> ExportXlsxAsync(SalesSearchRequest request, CancellationToken ct)
    {
        var (clean, blocked) = await LoadExportRowsAsync(request, ct);
        using var workbook = new XLWorkbook();
        var cleanSheet = workbook.Worksheets.Add("Clean Leads");
        var blockedSheet = workbook.Worksheets.Add("Blocked Leads");
        string[] cleanHeaders = ["Category", "Created By", "Company Name", "Contact Person", "Email", "Contact Number", "Created On", "Event"];
        string[] blockedHeaders = ["Category", "Created By", "Company Name", "Contact Person", "Email", "Contact Number", "Blocked On", "Blocked Reason", "Blocked By", "Event"];
        WriteHeaders(cleanSheet, cleanHeaders); WriteHeaders(blockedSheet, blockedHeaders);
        for (var i = 0; i < clean.Count; i++)
        {
            var x = clean[i]; var row = i + 2;
            cleanSheet.Cell(row, 1).Value = x.Category; cleanSheet.Cell(row, 2).Value = x.CreatedBy;
            cleanSheet.Cell(row, 3).Value = x.CompanyName; cleanSheet.Cell(row, 4).Value = x.ContactPerson;
            cleanSheet.Cell(row, 5).Value = x.CustomerEmail; cleanSheet.Cell(row, 6).Value = x.CustomerContactNumber1;
            cleanSheet.Cell(row, 7).Value = x.CreatedOn; cleanSheet.Cell(row, 7).Style.DateFormat.Format = "yyyy-MM-dd";
            cleanSheet.Cell(row, 8).Value = x.EventName;
        }
        for (var i = 0; i < blocked.Count; i++)
        {
            var x = blocked[i]; var row = i + 2;
            blockedSheet.Cell(row, 1).Value = x.Category; blockedSheet.Cell(row, 2).Value = x.CreatedBy;
            blockedSheet.Cell(row, 3).Value = x.CompanyName; blockedSheet.Cell(row, 4).Value = x.ContactPerson;
            blockedSheet.Cell(row, 5).Value = x.CustomerEmail; blockedSheet.Cell(row, 6).Value = x.CustomerContactNumber1;
            blockedSheet.Cell(row, 7).Value = x.CreatedOn; blockedSheet.Cell(row, 7).Style.DateFormat.Format = "yyyy-MM-dd";
            blockedSheet.Cell(row, 8).Value = x.BlockReason; blockedSheet.Cell(row, 9).Value = x.BlockedBy; blockedSheet.Cell(row, 10).Value = x.EventName;
        }
        cleanSheet.Columns().AdjustToContents(1, 50); blockedSheet.Columns().AdjustToContents(1, 50);
        using var output = new MemoryStream(); workbook.SaveAs(output); return output.ToArray();
    }

    public async Task<Stream> ExportCsvAsync(SalesSearchRequest request, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sales-export-{Guid.NewGuid():N}.csv");
        var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            await using var writer = new StreamWriter(output, new UTF8Encoding(true), 81920, true);
            await writer.WriteLineAsync("RecordType,Category,CompanyName,ContactPerson,Email,ContactNumber,CreatedOn,CreatedBy,EventName,BlockedReason,BlockedBy");
            if (request.RecordType != SalesRecordType.Blocked)
                await foreach (var x in ApplyFilters(db.CleanProspects.AsNoTracking(), request).OrderByDescending(x => x.CreatedOn).AsAsyncEnumerable().WithCancellation(ct))
                    await writer.WriteLineAsync(CsvLine("Clean", x.Category, x.CompanyName, x.ContactPerson, x.CustomerEmail, x.CustomerContactNumber1, x.CreatedOn?.ToString("yyyy-MM-dd"), x.CreatedBy, x.EventName, null, null));
            if (request.RecordType != SalesRecordType.Clean)
                await foreach (var x in ApplyFilters(db.BlockedProspects.AsNoTracking(), request).OrderByDescending(x => x.CreatedOn).AsAsyncEnumerable().WithCancellation(ct))
                    await writer.WriteLineAsync(CsvLine("Blocked", x.Category, x.CompanyName, x.ContactPerson, x.CustomerEmail, x.CustomerContactNumber1, x.CreatedOn?.ToString("yyyy-MM-dd"), x.CreatedBy, x.EventName, x.BlockReason, x.BlockedBy));
            await writer.FlushAsync(ct); output.Position = 0; return output;
        }
        catch { await output.DisposeAsync(); throw; }
    }

    public byte[] BuildImportResultWorkbook(SalesImportResult result)
    {
        using var workbook = new XLWorkbook();
        var clean = workbook.Worksheets.Add("Clean Customers");
        var blocked = workbook.Worksheets.Add("Blocked Customers");
        var invalid = workbook.Worksheets.Add("Invalid Customers");
        WriteHeaders(clean, ["Customer Code", "Company Name", "Email", "Contact Number", "Created By", "Event"]);
        WriteHeaders(blocked, ["Customer Code", "Company Name", "Email", "Contact Number", "Blocked By", "Blocked Reason", "Created By", "Event"]);
        WriteHeaders(invalid, ["Excel Row", "Company Name", "Email", "Contact Number", "Error Message"]);
        for (var i = 0; i < result.CleanRecords.Count; i++)
        {
            var x = result.CleanRecords[i]; var row = i + 2;
            clean.Cell(row, 1).Value = x.CustomerCode; clean.Cell(row, 2).Value = x.CompanyName; clean.Cell(row, 3).Value = x.CustomerEmail;
            clean.Cell(row, 4).Value = x.CustomerContactNumber1; clean.Cell(row, 5).Value = x.CreatedBy; clean.Cell(row, 6).Value = x.EventName;
        }
        for (var i = 0; i < result.BlockedRecords.Count; i++)
        {
            var x = result.BlockedRecords[i]; var row = i + 2;
            blocked.Cell(row, 1).Value = x.CustomerCode; blocked.Cell(row, 2).Value = x.CompanyName; blocked.Cell(row, 3).Value = x.CustomerEmail;
            blocked.Cell(row, 4).Value = x.CustomerContactNumber1; blocked.Cell(row, 5).Value = x.BlockedBy; blocked.Cell(row, 6).Value = x.BlockReason;
            blocked.Cell(row, 7).Value = x.CreatedBy; blocked.Cell(row, 8).Value = x.EventName;
        }
        for (var i = 0; i < result.InvalidRecords.Count; i++)
        {
            var x = result.InvalidRecords[i]; var row = i + 2;
            invalid.Cell(row, 1).Value = x.ExcelRow; invalid.Cell(row, 2).Value = x.CompanyName; invalid.Cell(row, 3).Value = x.CustomerEmail;
            invalid.Cell(row, 4).Value = x.CustomerNumber; invalid.Cell(row, 5).Value = x.ErrorMessage;
        }
        clean.Columns().AdjustToContents(1, 50); blocked.Columns().AdjustToContents(1, 50); invalid.Columns().AdjustToContents(1, 50);
        using var output = new MemoryStream(); workbook.SaveAs(output); return output.ToArray();
    }

    public byte[] BuildTemplate(SalesImportMode mode)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(mode == SalesImportMode.Event ? "EventTemplate" : "SalesTemplate");
        string[] headers = ["Company Name", "Contact Person", "Contact No1", "Email", "Country Code", "Country", "Contact No2", "Contact No3", "State", "City", "Category"];
        string[] example = ["Ennoble IP", "Rajnish Sir", "123456789", "contact@ennobleip.com", "+91", "INDIA", "9876543210", "", "DELHI", "NEW DELHI", "CORPORATE"];
        for (var col = 1; col <= headers.Length; col++) { sheet.Cell(1, col).Value = headers[col - 1]; sheet.Cell(2, col).Value = example[col - 1]; }
        var required = new[] { 1, 2, 5, 6, 11 };
        foreach (var col in required) { sheet.Cell(1, col).Style.Font.FontColor = XLColor.Red; sheet.Cell(1, col).Style.Fill.BackgroundColor = XLColor.LightYellow; }
        var header = sheet.Range(1, 1, 1, headers.Length); header.Style.Font.Bold = true; header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sheet.Range(2, 1, 2, headers.Length).Style.Fill.BackgroundColor = XLColor.LightCyan;
        sheet.Cell(3, 1).Value = "Delete the example row before upload. Email or Contact No1 is required.";
        sheet.Range(3, 1, 3, headers.Length).Merge(); sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents(10, 50);
        using var output = new MemoryStream(); workbook.SaveAs(output); return output.ToArray();
    }

    private async Task<(List<CleanProspect> Clean, List<BlockedProspect> Blocked)> LoadExportRowsAsync(SalesSearchRequest request, CancellationToken ct)
    {
        var clean = request.RecordType == SalesRecordType.Blocked ? [] : await ApplyFilters(db.CleanProspects.AsNoTracking(), request).OrderByDescending(x => x.CreatedOn).ToListAsync(ct);
        var blocked = request.RecordType == SalesRecordType.Clean ? [] : await ApplyFilters(db.BlockedProspects.AsNoTracking(), request).OrderByDescending(x => x.CreatedOn).ToListAsync(ct);
        return (clean, blocked);
    }

    private static IQueryable<CleanProspect> ApplyFilters(IQueryable<CleanProspect> query, SalesSearchRequest x)
    {
        if (!string.IsNullOrWhiteSpace(x.Search)) { var s = x.Search.Trim(); query = query.Where(p => (p.CompanyName != null && p.CompanyName.Contains(s)) || (p.CustomerEmail != null && p.CustomerEmail.Contains(s)) || (p.ContactPerson != null && p.ContactPerson.Contains(s)) || p.CustomerContactNumber1 == s); }
        if (!string.IsNullOrWhiteSpace(x.UserName) && x.UserName != "__ALL__") query = query.Where(p => p.CreatedBy == x.UserName);
        if (!string.IsNullOrWhiteSpace(x.Category)) query = query.Where(p => p.Category == x.Category.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(x.Event)) query = query.Where(p => p.EventName == x.Event.Trim());
        var range = GetDateRange(x); if (range.From is not null) query = query.Where(p => p.CreatedOn >= range.From); if (range.ToExclusive is not null) query = query.Where(p => p.CreatedOn < range.ToExclusive);
        return query;
    }

    private static IQueryable<BlockedProspect> ApplyFilters(IQueryable<BlockedProspect> query, SalesSearchRequest x)
    {
        if (!string.IsNullOrWhiteSpace(x.Search)) { var s = x.Search.Trim(); query = query.Where(p => (p.CompanyName != null && p.CompanyName.Contains(s)) || (p.CustomerEmail != null && p.CustomerEmail.Contains(s)) || (p.ContactPerson != null && p.ContactPerson.Contains(s)) || p.CustomerContactNumber1 == s); }
        if (!string.IsNullOrWhiteSpace(x.UserName) && x.UserName != "__ALL__") query = query.Where(p => p.CreatedBy == x.UserName);
        if (!string.IsNullOrWhiteSpace(x.Category)) query = query.Where(p => p.Category == x.Category.Trim().ToUpper());
        if (!string.IsNullOrWhiteSpace(x.Event)) query = query.Where(p => p.EventName == x.Event.Trim());
        var range = GetDateRange(x); if (range.From is not null) query = query.Where(p => p.CreatedOn >= range.From); if (range.ToExclusive is not null) query = query.Where(p => p.CreatedOn < range.ToExclusive);
        return query;
    }

    private static (DateTime? From, DateTime? ToExclusive) GetDateRange(SalesSearchRequest x) => x.SelectedDate is not null
        ? (x.SelectedDate.Value.Date, x.SelectedDate.Value.Date.AddDays(1))
        : (x.FromDate?.Date, x.ToDate?.Date.AddDays(1));

    private async Task<HashSet<string>> LoadCommonDomainsAsync(CancellationToken ct) =>
        (await db.CommonDomains.AsNoTracking().Select(x => x.DomainName.ToLower()).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task<List<CustomerSnapshot>> LoadCustomerSnapshotAsync(CancellationToken ct) => await db.Customers.AsNoTracking()
        .Select(x => new CustomerSnapshot(x.CompanyName, x.CustomerEmail, x.EmailDomain)).ToListAsync(ct);

    private async Task<List<CleanSnapshot>> LoadCleanSnapshotAsync(CancellationToken ct) => await db.CleanProspects.AsNoTracking()
        .Select(x => new CleanSnapshot(x.CompanyName, x.ContactPerson, x.CustomerEmail, x.EmailDomain, x.CustomerContactNumber1, x.CreatedBy)).ToListAsync(ct);

    private static Classification Classify(ParsedLead lead, SalesImportMode mode, HashSet<string> commonDomains, List<CustomerSnapshot> customers, List<CleanSnapshot> clean)
    {
        var isCommonDomain = lead.Domain.Length > 0 && commonDomains.Contains(lead.Domain);
        var masterMatch = customers.Any(x =>
            (lead.Email.Length > 0 && Eq(x.Email, lead.Email)) ||
            (lead.Domain.Length > 0 && (mode == SalesImportMode.Event || !isCommonDomain) && Eq(DomainOf(x.Email, x.Domain), lead.Domain)) ||
            Eq(x.Company, lead.CompanyName));
        if (masterMatch) return new("Customer already exists in master table.", null, null);

        var otherUsers = clean.Where(x => !Eq(x.CreatedBy, lead.Actor)).ToList();
        CleanSnapshot? match;
        if (lead.Category == "UNIVERSITY")
        {
            match = otherUsers.FirstOrDefault(x => lead.Email.Length > 0 && Eq(x.Email, lead.Email));
            return match is null ? new(null, null, null) : new(null, "University: Exact Email or Contact+Email Match", match.CreatedBy);
        }
        if (!isCommonDomain && lead.Email.Length > 0)
        {
            match = otherUsers.FirstOrDefault(x => Eq(x.Email, lead.Email));
            if (match is not null) return new(null, "Email Match", match.CreatedBy);
            if (mode == SalesImportMode.Event || lead.Category != "INDIVIDUAL")
            {
                match = otherUsers.FirstOrDefault(x => Eq(DomainOf(x.Email, x.Domain), lead.Domain));
                if (match is not null) return new(null, "Domain Match", match.CreatedBy);
            }
        }
        if (lead.Phone1 is not null)
        {
            match = otherUsers.FirstOrDefault(x => Eq(x.Phone1, lead.Phone1));
            if (match is not null) return new(null, "Phone Number Match", match.CreatedBy);
        }
        if (lead.ContactPerson.Length > 0 && (mode == SalesImportMode.Event || lead.Category != "INDIVIDUAL"))
        {
            var partial = lead.CompanyName.Length > 4 ? lead.CompanyName[..(lead.CompanyName.Length / 2)] : lead.CompanyName;
            match = otherUsers.FirstOrDefault(x => Eq(x.Contact, lead.ContactPerson) && x.Company?.Contains(partial, StringComparison.OrdinalIgnoreCase) == true);
            if (match is not null) return new(null, "50% Company + 100% Contact Match", match.CreatedBy);
        }
        if (mode == SalesImportMode.Event || lead.Category != "INDIVIDUAL")
        {
            match = otherUsers.FirstOrDefault(x => Eq(x.Company, lead.CompanyName));
            if (match is not null) return new(null, "100% Company Name Match", match.CreatedBy);
        }
        return new(null, null, null);
    }

    private static void Validate(ParsedLead lead, SalesImportMode mode)
    {
        if (lead.CompanyName.Length == 0 || lead.Category.Length == 0) throw new SalesValidationException("Company name and category are required.");
        if (mode == SalesImportMode.Event && !ValidCategories.Contains(lead.Category)) throw new SalesValidationException("Invalid category.");
        if (mode == SalesImportMode.Event && lead.EventName is null) throw new SalesValidationException("Event name is required for event sales.");
        if (mode == SalesImportMode.Event && (lead.CountryCode is null || lead.Country is null)) throw new SalesValidationException("Country code and country are required.");
        if (lead.Email.Length == 0 && lead.Phone1 is null) throw new SalesValidationException("Email or contact number is required.");
        if (lead.Email.Length > 0 && !IsValidEmail(lead.Email)) throw new SalesValidationException("Invalid email.");
        if (new[] { lead.Phone1, lead.Phone2, lead.Phone3 }.Any(x => x is not null && !x.All(char.IsDigit))) throw new SalesValidationException("Contact numbers can contain digits only.");
    }

    private static ParsedLead ParseRow(IXLWorksheet sheet, int row, string actor, string? eventName) => Normalize(new SalesLeadRequest(
        sheet.Cell(row, 1).GetString(), sheet.Cell(row, 2).GetString(), sheet.Cell(row, 3).GetString(), sheet.Cell(row, 4).GetString(),
        sheet.Cell(row, 5).GetString(), sheet.Cell(row, 6).GetString(), sheet.Cell(row, 7).GetString(), sheet.Cell(row, 8).GetString(),
        sheet.Cell(row, 9).GetString(), sheet.Cell(row, 10).GetString(), sheet.Cell(row, 11).GetString(), actor, eventName));

    private static ParsedLead Normalize(SalesLeadRequest x)
    {
        var email = x.CustomerEmail?.Trim().ToLowerInvariant() ?? "";
        return new ParsedLead(x.CompanyName.Trim().ToUpperInvariant(), x.ContactPerson.Trim().ToUpperInvariant(), Empty(x.CustomerContactNumber1), email,
            DomainOf(email, null), Empty(x.CountryCode), Empty(x.Country)?.ToUpperInvariant(), Empty(x.CustomerContactNumber2), Empty(x.CustomerContactNumber3),
            Empty(x.State)?.ToUpperInvariant(), Empty(x.City)?.ToUpperInvariant(), x.Category.Trim().ToUpperInvariant(), x.Actor.Trim(), Empty(x.EventName));
    }

    private static CleanProspect ToCleanEntity(ParsedLead x) { var now = DateTime.UtcNow; return new CleanProspect { CustomerCode = Code(), CompanyName = x.CompanyName, ContactPerson = x.ContactPerson, CustomerContactNumber1 = x.Phone1, CustomerContactNumber2 = x.Phone2, CustomerContactNumber3 = x.Phone3, CustomerEmail = x.Email, EmailDomain = Empty(x.Domain), CountryCode = x.CountryCode, Country = x.Country, State = x.State, City = x.City, Category = x.Category, SalesPersonId = 1, CreatedBy = x.Actor, CreatedOn = now, ModifiedBy = x.Actor, ModifiedOn = now, EventName = x.EventName }; }
    private static BlockedProspect ToBlockedEntity(ParsedLead x, string? blockedBy, string reason) => new() { CustomerCode = Code(), CompanyName = x.CompanyName, ContactPerson = x.ContactPerson, CustomerContactNumber1 = x.Phone1, CustomerContactNumber2 = x.Phone2, CustomerContactNumber3 = x.Phone3, CustomerEmail = x.Email, EmailDomain = Empty(x.Domain), CountryCode = x.CountryCode, Country = x.Country, State = x.State, City = x.City, Category = x.Category, CreatedBy = x.Actor, CreatedOn = DateTime.UtcNow, BlockedBy = blockedBy, BlockReason = reason, EventName = x.EventName };
    private static SalesLeadResponse ToResponse(CleanProspect x) => new(x.Id, SalesRecordType.Clean, x.CustomerCode, x.CompanyName, x.ContactPerson, x.CustomerContactNumber1, x.CustomerContactNumber2, x.CustomerContactNumber3, x.CustomerEmail, x.EmailDomain, x.CountryCode, x.Country, x.State, x.City, x.Category, x.CreatedBy, x.CreatedOn, x.SalesPersonId, null, null, null, null, null, x.EventName);
    private static SalesLeadResponse ToResponse(BlockedProspect x) => new(x.Id, SalesRecordType.Blocked, x.CustomerCode, x.CompanyName, x.ContactPerson, x.CustomerContactNumber1, x.CustomerContactNumber2, x.CustomerContactNumber3, x.CustomerEmail, x.EmailDomain, x.CountryCode, x.Country, x.State, x.City, x.Category, x.CreatedBy, x.CreatedOn, null, x.BlockedBy, x.BlockReason, x.Released, x.ReleasedBy, x.ReleasedOn, x.EventName);
    private static ImportError ToError(int row, ParsedLead x, string message) => new(row, x.CompanyName, x.Email, x.Phone1, message);
    private static string Code() => $"LEAD-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
    private static string? Empty(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    private static string DomainOf(string? email, string? stored) => email?.Contains('@') == true ? email[(email.LastIndexOf('@') + 1)..].ToLowerInvariant() : !string.IsNullOrWhiteSpace(stored) && stored != "-" ? stored.Trim().ToLowerInvariant() : "";
    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool IsValidEmail(string email) { try { var x = new MailAddress(email); return x.Address == email && x.Host.Contains('.'); } catch { return false; } }
    private static void WriteHeaders(IXLWorksheet sheet, string[] headers) { for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i]; var range = sheet.Range(1, 1, 1, headers.Length); range.Style.Font.Bold = true; range.Style.Font.FontColor = XLColor.White; range.Style.Fill.BackgroundColor = XLColor.BlueGray; sheet.SheetView.FreezeRows(1); }
    private static string CsvLine(params object?[] values) => string.Join(',', values.Select(x => Csv(x?.ToString())));
    private static string Csv(string? value) { if (string.IsNullOrEmpty(value)) return ""; var x = value.Replace("\"", "\"\""); return x.IndexOfAny([',', '\"', '\r', '\n']) >= 0 ? $"\"{x}\"" : x; }

    private sealed record ParsedLead(string CompanyName, string ContactPerson, string? Phone1, string Email, string Domain, string? CountryCode, string? Country, string? Phone2, string? Phone3, string? State, string? City, string Category, string Actor, string? EventName);
    private sealed record CustomerSnapshot(string Company, string Email, string? Domain);
    private sealed record CleanSnapshot(string? Company, string? Contact, string? Email, string? Domain, string? Phone1, string? CreatedBy);
    private sealed record Classification(string? InvalidReason, string? BlockReason, string? BlockedBy);
}

public sealed class SalesValidationException(string message) : Exception(message);
