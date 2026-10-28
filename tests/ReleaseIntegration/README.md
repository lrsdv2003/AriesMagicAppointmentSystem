# Isolated release integration

Unified alpha preparation (28 October 2026): **193 checks pass on both 10.0.7 and installed 10.0.13**, including a real SQL-backed healthy `/health` response. The separate no-database Production package smoke deliberately expects 503 from that health check. New `--release-review-ui-fixture` mode uses its own GUID LocalDB/private state on loopback 5310, preserving existing manual previews; see `tests/ReleasePackaging/README.md`. Earlier phase counts below remain historical. Every synthetic ReleaseFactory host now explicitly uses ephemeral keys; no product Data Protection default is changed by that test isolation.

Run from the repository root on Windows with SQL Server LocalDB and the .NET 10 SDK. Prefer the current serviced .NET/ASP.NET Core 10 runtime. The suite also passed on a private 10.0.7 runtime to verify the reported hosting baseline; this compatibility test does not certify that older patch as current for production.

```powershell
./scripts/Install-PrivateRuntime.ps1
dotnet restore tests/ReleaseIntegration/ReleaseIntegration.csproj
dotnet build tests/ReleaseIntegration/ReleaseIntegration.csproj -c Release --no-restore
dotnet tool restore
./scripts/New-ReleaseMigrationScript.ps1
./.tmp-owner-build/dotnet-10.0.13/dotnet.exe tests/ReleaseIntegration/bin/Release/net10.0/ReleaseIntegration.dll
```

The harness uses actual ASP.NET Identity cookies/tokens and the real MVC request pipeline through WebApplicationFactory/TestServer. It does not register fixture/header authentication. SQL is a uniquely named disposable LocalDB database; all migrations are applied and the forward idempotent script is executed twice. Native backup/restore uses a separate generated database, CHECKSUM/VERIFYONLY/CHECKDB/CHECKCONSTRAINTS, record/history comparison and an authenticated application read after restoration. Only generated test databases are deleted; backup/proof fixtures remain under the ignored build directory.

SMTP and Nominatim-compatible servers bind to loopback and exercise real network contracts. No message goes to Google or real recipients, and no money is transferred. Generated passwords and reset tokens are never printed. The PNG under Fixtures is a synthetic receipt created with System.Drawing; actual Tesseract reads its PHP 2,000 amount. Production OCR/platform compatibility still needs target verification.

The factory's audit-failure override forces rollback testing. Its short-ticket option tests expiry, and security-stamp revalidation is made immediate for the revocation assertion only. Neither change affects production registration. Expected SMTP, constraint and forced-audit error logs are deliberate failure scenarios; the final success line determines the suite result.

The harness records a sanitized machine-readable summary at `.tmp-owner-build/release-evidence.json`. It explicitly marks Azure, staging, production and provider credential revocation as unverified. A successful synthetic restore does not certify production recovery or RTO/RPO.

## Authentication presentation verification (Phase 5)

Phase 7 adds the explicit `--client-hub-ui-fixture` mode on 127.0.0.1:5298 using the existing guarded BookingBrowserFixture. It keeps 5295 and its manual records intact. `ClientHubFixtureSeed` inserts only in its own verified LocalDB GUID database: representative booking/payment/reschedule/private-feedback records, notifications, participant-scoped messages, an empty Client and nine single-status Clients. The fixture's effective application context/environment are checked before state is written. `CLIENT_HUB_PREVIEW.md` documents generated credentials and guarded stop/restart. New CLI dispatch and seed data are test tooling, never a production endpoint or schema migration. The unchanged HTTP/SQL integration suite passes 192 checks on the reviewed build.

The explicit `--manual-ui-preview` mode reuses BookingBrowserFixture on port 5295 for current-source manual exploration in Development. It creates only its own `AriesBookingVisual_<GUID>` LocalDB database; verifies the actual application's environment/EF target; uses loopback SMTP/geocoding; and keeps existing routes/authorization available. State/credentials and the synthetic @example.test mailbox are private ignored files. It accepts no connection-string argument and creates no public diagnostics route. See `LOCAL_UI_PREVIEW.md` for generated-account login, navigation, registration/recovery codes and guarded start/stop. The ordinary automated fixture modes retain their original ports and behavior.

The explicit `--auth-ui-fixture` mode is documented in `tests/ClientVisual/README.md`. It is a separate loopback-only synthetic browser harness; ordinary invocation still runs the full integration suite. It does not deploy or connect to production. The optional `ReleaseFactory.authUi` flag selects ephemeral test keys only for that browser fixture; the production Program and Identity options are unchanged. The fixture's private filesystem commands must never be added to normal application routes or published as diagnostics.

Phase 6: `--booking-ui-fixture` is a test-executable-only, loopback preview of actual booking/payment/reschedule controllers against a generated GUID LocalDB/private root. See `tests/ClientVisual/README.md` for guarded start/stop, private state handling and responsive browser scripts. No production connection or credentials are accepted by this fixture. The Concierge rework integration suite passes 192 checks, including own/unrelated Client booking-context metadata; existing checks remain intact and include the complete HTTP workflow, authoritative pricing, downpayment/Owner verification, private-proof access, SQL capacity, rescheduling and synthetic backup/restore. The preview's ephemeral Data Protection and unsupported TestServer request-body-limit feature do not establish production hosting verification.
