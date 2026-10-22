# Isolated release integration

Run from the repository root on Windows with SQL Server LocalDB and .NET/ASP.NET Core 9.0.21 or a later .NET 9 patch. The system SDK may build the projects; use the private patched runtime to execute them when the system runtime is older.

```powershell
./scripts/Install-PrivateRuntime.ps1
dotnet restore tests/ReleaseIntegration/ReleaseIntegration.csproj
dotnet build tests/ReleaseIntegration/ReleaseIntegration.csproj -c Release --no-restore
dotnet tool restore
./scripts/New-ReleaseMigrationScript.ps1
./.tmp-owner-build/dotnet-9.0.21/dotnet.exe tests/ReleaseIntegration/bin/Release/net9.0/ReleaseIntegration.dll
```

The harness uses actual ASP.NET Identity cookies/tokens and the real MVC request pipeline through WebApplicationFactory/TestServer. It does not register fixture/header authentication. SQL is a uniquely named disposable LocalDB database; all migrations are applied and the forward idempotent script is executed twice. Native backup/restore uses a separate generated database, CHECKSUM/VERIFYONLY/CHECKDB/CHECKCONSTRAINTS, record/history comparison and an authenticated application read after restoration. Only generated test databases are deleted; backup/proof fixtures remain under the ignored build directory.

SMTP and Nominatim-compatible servers bind to loopback and exercise real network contracts. No message goes to Google or real recipients, and no money is transferred. Generated passwords and reset tokens are never printed. The PNG under Fixtures is a synthetic receipt created with System.Drawing; actual Tesseract reads its PHP 2,000 amount. Production OCR/platform compatibility still needs target verification.

The factory's audit-failure override forces rollback testing. Its short-ticket option tests expiry, and security-stamp revalidation is made immediate for the revocation assertion only. Neither change affects production registration. Expected SMTP, constraint and forced-audit error logs are deliberate failure scenarios; the final success line determines the suite result.

The harness records a sanitized machine-readable summary at `.tmp-owner-build/release-evidence.json`. It explicitly marks Azure, staging, production and provider credential revocation as unverified. A successful synthetic restore does not certify production recovery or RTO/RPO.
