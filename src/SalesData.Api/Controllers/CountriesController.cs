using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesData.Api.Infrastructure;

namespace SalesData.Api.Controllers;

[ApiController]
[Route("api/countries")]
public sealed class CountriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await db.Countries.AsNoTracking().OrderBy(x => x.CountryName).Select(x => new { x.Id, x.CountryName, x.CountryCode }).ToListAsync(ct));
}
