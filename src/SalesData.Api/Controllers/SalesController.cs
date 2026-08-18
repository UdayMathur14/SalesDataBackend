using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SalesData.Api.Contracts;
using SalesData.Api.Services;

namespace SalesData.Api.Controllers;

[ApiController]
[Route("api/sales")]
public sealed class SalesController(ISalesService service) : ControllerBase
{
    [HttpGet]
    public Task<SalesSearchResult> Search([FromQuery] SalesSearchRequest request, CancellationToken ct) => service.SearchAsync(request, ct);

    [HttpGet("{recordType}/{id:int}")]
    public async Task<ActionResult<SalesLeadResponse>> Get(SalesRecordType recordType, int id, CancellationToken ct)
    {
        try { return await service.GetAsync(recordType, id, ct) is { } item ? Ok(item) : NotFound(); }
        catch (SalesValidationException ex) { return ValidationProblem(ex.Message); }
    }

    [HttpPost]
    public async Task<ActionResult<SalesLeadResponse>> Create(SalesLeadRequest request, CancellationToken ct)
    {
        try
        {
            var result = await service.CreateAndClassifyAsync(request, ct);
            return CreatedAtAction(nameof(Get), new { recordType = result.RecordType, id = result.Id }, result);
        }
        catch (SalesValidationException ex) { return ValidationProblem(ex.Message); }
    }

    [HttpPut("clean/{id:int}")]
    public async Task<ActionResult<SalesLeadResponse>> UpdateClean(int id, SalesLeadRequest request, CancellationToken ct)
    {
        try { return await service.UpdateCleanAsync(id, request, ct) is { } item ? Ok(item) : NotFound(); }
        catch (SalesValidationException ex) { return ValidationProblem(ex.Message); }
    }

    [HttpDelete("{recordType}/{id:int}")]
    public async Task<IActionResult> Delete(SalesRecordType recordType, int id, CancellationToken ct)
    {
        try { return await service.DeleteAsync(recordType, id, ct) ? NoContent() : NotFound(); }
        catch (SalesValidationException ex) { return ValidationProblem(ex.Message); }
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<ActionResult<SalesImportResult>> Import([FromForm] SalesImportForm form, CancellationToken ct)
    {
        if (form.File.Length == 0) return BadRequest("Excel file is empty.");
        try
        {
            await using var stream = form.File.OpenReadStream();
            return Ok(await service.ImportAsync(stream, form.Mode, form.Actor, form.EventName, ct));
        }
        catch (SalesValidationException ex) { return ValidationProblem(ex.Message); }
        catch (InvalidDataException) { return BadRequest("Invalid or corrupted Excel file."); }
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] SalesSearchRequest request, [FromQuery] SalesExportFormat format = SalesExportFormat.Xlsx, CancellationToken ct = default)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        if (format == SalesExportFormat.Csv)
            return File(await service.ExportCsvAsync(request, ct), "text/csv; charset=utf-8", $"LeadDataExport-{stamp}.csv");
        return File(await service.ExportXlsxAsync(request, ct), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"LeadDataExport-{stamp}.xlsx");
    }

    [HttpPost("import-results/export")]
    public IActionResult ExportImportResults(SalesImportResult result) => File(service.BuildImportResultWorkbook(result),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MailingUploadResults.xlsx");

    [HttpGet("templates/{mode}")]
    public IActionResult DownloadTemplate(SalesImportMode mode)
    {
        var name = mode == SalesImportMode.Event ? "EventTemplate.xlsx" : "SalesTemplate.xlsx";
        return File(service.BuildTemplate(mode), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    [HttpGet("verify-company")]
    public async Task<IActionResult> VerifyCompany([Required] string companyName, CancellationToken ct)
    {
        try { return Ok(await service.VerifyCompanyAsync(companyName, ct)); }
        catch (SalesValidationException ex) { return ValidationProblem(ex.Message); }
    }

    [HttpGet("filter-options")]
    public Task<SalesFilterOptions> FilterOptions([FromQuery] string? actor, CancellationToken ct) => service.GetFilterOptionsAsync(actor, ct);
}

public sealed class SalesImportForm
{
    [Required] public required IFormFile File { get; init; }
    [Required, MaxLength(100)] public required string Actor { get; init; }
    public SalesImportMode Mode { get; init; } = SalesImportMode.Standard;
    [MaxLength(200)] public string? EventName { get; init; }
}
