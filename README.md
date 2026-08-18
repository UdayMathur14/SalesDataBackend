# Sales Data Backend - Customer Master

.NET 10 Web API for the Customer Master flow. Authentication/authorization is intentionally outside the current scope.

## Structure

- `Controllers`: HTTP endpoints and status codes
- `Contracts`: request/response DTOs
- `Services`: customer business rules, duplicate checks, Excel import/template
- `Domain`: database entities
- `Infrastructure`: EF Core and SQL Server mapping

## Run

1. Put the correct SQL Server connection string in `src/SalesData.Api/appsettings.json` (or use the `ConnectionStrings__DefaultConnection` environment variable).
2. Run `dotnet restore`.
3. Run `dotnet ef database update --project src/SalesData.Api` after creating migrations for a new database.
4. Run `dotnet run --project src/SalesData.Api`; the API listens on `http://localhost:5080` in the included development profile.

## Customer APIs

- `GET /api/customers?search=&category=&country=&page=1&pageSize=50`
- `GET /api/customers/{id}`
- `POST /api/customers`
- `PUT /api/customers/{id}`
- `DELETE /api/customers/{id}`
- `POST /api/customers/import` (`multipart/form-data`: `file`, `actor`)
- `GET /api/customers/template`
- `GET /api/countries`

For corporate/non-individual customers, email or company duplication is rejected across both Customer Master and Clean Prospects. For `INDIVIDUAL`, duplication is checked by email, matching the supplied MVC flow.

## Sales Transaction APIs

- `GET /api/sales` - paged clean/blocked search with category, event, user and date filters
- `GET /api/sales/{recordType}/{id}` - get a clean or blocked lead
- `POST /api/sales` - submit and automatically classify one lead
- `PUT /api/sales/clean/{id}` - update a clean lead
- `DELETE /api/sales/{recordType}/{id}` - delete a clean or blocked lead
- `POST /api/sales/import` - optimized bulk Excel import; supports `Standard` and `Event` modes
- `GET /api/sales/export?format=Xlsx` - filtered Excel or CSV export
- `POST /api/sales/import-results/export` - export one upload result into Clean, Blocked and Invalid sheets
- `GET /api/sales/templates/Standard` - normal upload template
- `GET /api/sales/templates/Event` - event upload template
- `GET /api/sales/verify-company?companyName=...` - check Sales and Customer Master
- `GET /api/sales/filter-options?actor=...` - categories, events and creators for frontend filters

Sales imports prefetch matching data once and classify all Excel rows in memory, avoiding the original multiple-database-queries-per-row bottleneck. Dates use half-open ranges so SQL Server can use the configured indexes.

The supplied prospect schema has `EVENT_NAME` but no `EVENT_DATE` column, so event uploads persist the event name only, matching the provided entity model.
