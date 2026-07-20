# AriesMagicAppointmentSystem — Repair Summary

## Applied fixes

- Added a valid EF Core migration for the missing `SystemActivities` and `TrashHistories` tables.
- Removed the orphaned, unrecognized migration `20260718104141_AddLastLoginAtAndTrashHistoryFields.cs`.
- Configured startup to apply pending migrations before identity/service seeders run.
- Removed the incompatible legacy `User.Notifications` navigation that caused EF to create shadow FK `Notification.UserId1`.
- Explicitly mapped `Notification.UserId` to `ApplicationUser`.
- Added `decimal(18,2)` precision for booking, service, inclusion, payment, refund, and trash-history amounts.
- Made system-activity search null-safe and pagination bounds-safe.
- Disabled HTTPS redirection only in Development to avoid the missing HTTPS-port warning.

## First run

Back up the database, then run:

```powershell
dotnet ef migrations list
dotnet ef database update
dotnet run
```

The new migration should appear as:

`20260719090000_RepairMissingActivityAndTrashTables`

## Validation limitation

The project was statically inspected and repaired in an environment without the .NET SDK, so `dotnet build` and live SQL Server execution could not be performed here. Run the commands above locally and retain the original ZIP/database backup until validation is complete.
