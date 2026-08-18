using Microsoft.AspNetCore.Mvc;
using SalesData.Api.Contracts;
using SalesData.Api.Services;

namespace SalesData.Api.Controllers;

[ApiController]
[Route("api/customers")]
public sealed class CustomersController(ICustomerService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<CustomerResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResult<CustomerResponse>> Search([FromQuery] string? search, [FromQuery] string? category, [FromQuery] string? country, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        service.SearchAsync(search, category, country, page, pageSize, ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerResponse>> Get(int id, CancellationToken ct) =>
        await service.GetByIdAsync(id, ct) is { } customer ? Ok(customer) : NotFound();

    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(CustomerRequest request, CancellationToken ct)
    {
        try { var created = await service.CreateAsync(request, ct); return CreatedAtAction(nameof(Get), new { id = created.Id }, created); }
        catch (CustomerConflictException ex) { return Conflict(new ProblemDetails { Title = "Duplicate customer", Detail = ex.Message, Status = 409 }); }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CustomerResponse>> Update(int id, CustomerRequest request, CancellationToken ct)
    {
        try { return await service.UpdateAsync(id, request, ct) is { } updated ? Ok(updated) : NotFound(); }
        catch (CustomerConflictException ex) { return Conflict(new ProblemDetails { Title = "Duplicate customer", Detail = ex.Message, Status = 409 }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => await service.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ImportResult>> Import([FromForm] CustomerImportForm form, CancellationToken ct)
    {
        if (form.File.Length == 0) return BadRequest("Excel file is empty.");
        await using var stream = form.File.OpenReadStream();
        return Ok(await service.ImportAsync(stream, form.Actor, ct));
    }

    [HttpGet("template")]
    public IActionResult Template() => File(service.BuildTemplate(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "CustomerTemplate.xlsx");
}

public sealed class CustomerImportForm
{
    public required IFormFile File { get; init; }
    public required string Actor { get; init; }
}
