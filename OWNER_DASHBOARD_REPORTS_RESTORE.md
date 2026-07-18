# Owner Dashboard and Reports Restoration

## Restored behavior

- Owner Dashboard now routes to `DashboardController.Owner` and displays overview widgets only.
- Owner Reports now routes to `ReportsController.Index` and contains reporting filters, charts, metrics, records, CSV export, and print/PDF support.
- Owner navigation now contains separate Dashboard and Reports menu items.
- Owner payment and notification links use the actual controller route names (`Payment` and `Notification`).

## Files updated

- `Controllers/ReportsController.cs`
- `ViewModels/ReportDashboardViewModel.cs`
- `Views/Dashboard/Owner.cshtml`
- `Views/Reports/Index.cshtml`
- `Views/Shared/_DashboardLayout.cshtml`

No database migration is required.
