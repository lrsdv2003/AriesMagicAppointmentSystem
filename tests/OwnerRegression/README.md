# Three-role regression verification

Current active roles: **Owner, Staff, Client**. Admin remains only in explicit negative fixtures and historical attribution. No harness reads the production connection string or sends real email.

## Required local checks

Use .NET 10 and a disposable SQL Server database. The default is a dedicated SQL LocalDB instance supplied by `ARIES_TEST_LOCALDB_INSTANCE`. On this workstation, the already running local SQL Server 2022 instance is supported explicitly through `OwnerRegression --local-sql` and `ARIES_TEST_USE_DEFAULT_SQL=true` for the other test runners. This option selects only the fixed local server `.` and GUID-owned test databases; it never accepts an arbitrary hosted SQL target. No permissions or service settings are changed. SQL restore tests use that instance's existing backup/data/log directories.

- Build and run `tests/OwnerRegression` with `--render` (and `--local-sql` when selected).
- Build/run `tests/ReleaseIntegration`: full run, `--personnel-only` and `--personnel-operator-only`.
- Build `tests/PersonnelOperator` before its contract test. Set `ARIES_TEST_OPERATOR_DLL` only when using a separate build-artifacts directory.
- Build/run `tests/OwnerEmailDelivery`.
- Run `npm ci --ignore-scripts`, then `npm run test:dom` after rendering. The DOM runner uses `current-fixtures.json`, excluding stale four-role HTML left by previous runs.
- Run `scripts/Test-CiDeploymentPolicy.ps1` and `python scripts/Test-RepositorySafety.py`.

`Invoke-CiVerification.ps1` creates its own LocalDB instance and runs the automated gates. Alternate artifacts paths allow tests while an older isolated local preview is still running; do not overwrite its locked DLLs.

## Browser and visual evidence

After rendering current fixtures:

    node tests/OwnerRegression/three-role.browser.mjs

Install Puppeteer and axe-core in an approved local tooling directory. `PUPPETEER_MODULE` may specify its module URL; `PUPPETEER_EXECUTABLE_PATH` selects the browser; `AXE_CORE_PATH` selects axe's script. Cache the application's existing FullCalendar 6.1.15 bundle at `%TEMP%/aries-fullcalendar.js`. All requests are intercepted and served from synthetic HTML/local assets. No live website or account is involved.

The runner covers 16 Owner/Staff pages at 1440, 1024, 768, 390 and 320 pixels, axe violations, overflow, and keyboard traversal of migrated forms. Images/fonts finish loading before capture. Results are under ignored `tmp/three-role/after`. Normal screenshots preserve the 900px viewport and internal scroll pane. Additional `-complete.png` captures expand viewport height to show the complete content at the same desktop/mobile width. `--baseline` is for capturing a pre-change checkout, never evidence of the current old interface.

DOM checks cover current public/authentication/Client form contracts, consent, payment previews, rescheduling review, internal navigation and migrated administration. Actual Identity cookies, enrollment expiry/replay, CSRF, financial boundaries, legacy session rejection and audit rollback are checked by the SQL/HTTP runners. The offline tool contract exercises exact-identity approval separately; it is never included in web publish output.

The other purpose-specific browser scripts remain supplemental diagnostics. Their role lists/routes have been updated for three active roles. Syntax validation does not mean every supplemental script was executed; see the dated release report for actual results and limitations.

## Migration and documentation

Owner transfer was validated at commit `308b72d` before Admin retirement. No EF migration, editable capacity policy, automatic identity cleanup or default privileged password is added. Calendar conflict/race checks ensure blocking and payment confirmation cannot commit an invalid confirmed schedule. Historical booking money/times/names, private conversations and audit attribution remain preserved.

See `THREE_ROLE_MIGRATION_INVENTORY.md`, `docs/THREE_ROLE_ARCHITECTURE.md` and `docs/THREE_ROLE_RELEASE_RUNBOOK.md`. Formal research requirements and privately held defense documents require EJ/advisor review where they explicitly name the original Admin role.
