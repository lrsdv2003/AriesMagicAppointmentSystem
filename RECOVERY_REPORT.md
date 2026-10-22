# Application recovery and engineering stabilization

Recovery date: 22 October 2026. Overall classification: **PARTIALLY STABLE**. The local application and published build launch successfully and the confirmed schema/runtime failures are repaired. Live SMTP, production identity recovery, Azure/staging, actual malware scanning, video processing, scale-out, and real-user acceptance remain separate release gates. This is not production clearance.

## 1. Confirmed root causes

The initial restore hit the agent sandbox's denial of access to the normal user NuGet configuration. Retrying with authorized access restored successfully; this was an execution-environment restriction, not a repository dependency defect. Debug and Release compilation succeeded on the installed .NET 9.0.21 runtime. The earlier machine-runtime deficit has already been resolved outside this recovery: both Microsoft.NETCore.App and Microsoft.AspNetCore.App 9.0.21 are now installed system-wide.

Running the existing `http` development profile reproduced the actual application failure:

```text
Application started ... http://localhost:5207
SqlException: Invalid object name 'EventFeedbacks'.
SqlException: Invalid object name 'EmailOutboxMessages'.
SqlException: Invalid object name 'FeedbackMedia'.
GET / -> HTTP 500
```

`HomeController.Index` queries the public feedback feed; the email and media workers query their corresponding new tables. The connected schema predated three migrations:

- `20261021182539_AddTransactionalEmailOutbox`
- `20261021183211_ProtectFinancialHistory`
- `20261021220548_AddPostEventFeedback`

Automatic schema initialization was intentionally off, but startup only checked optional initialization and `/health` only tested SQL connectivity. A database could therefore appear healthy while required tables were absent.

There was also an important configuration override: development user-secrets selected `(localdb)\MSSQLLocalDB`, while `appsettings.json` named `localhost`. Both contained a database called `AriesMagicAppointmentSystemDb`. I initially inspected, backed up, rehearsed, and migrated the localhost database before identifying the override. This was an additional local database change, not the effective application repair. Its records were preserved and its verified backup retained. The effective LocalDB database was subsequently backed up and repaired separately. No connection secrets were printed, removed, or replaced.

The broad browser run found a separate pre-existing dashboard defect: a programmatic document scroll moved the header to -149px even though the intended content region had its own scrolling. Establishing a positioning context on `.internal-ui .dashboard-content` prevented that escaped overflow; the same diagnostic then recorded scrollY=0 and header top=0. This affects internal layout containment, not the Client design.

## 2. Recovery changes and data preservation

For each same-named local database, recovery followed copy-only backup with CHECKSUM, RESTORE VERIFYONLY, restoration to a newly named `AriesRecovery_<guid>` database, forward migration rehearsal, integrity/constraint checks, and only then application of the forward idempotent script to the source. No Down migration, database reset, data deletion, account reset, seeding, or production cutover was performed. The financial preflight found no invalid payment/refund amounts or mismatched refund payments.

| Effective LocalDB aggregate | Before | After repair |
|---|---:|---:|
| Bookings | 2 | 2 |
| Payments | 3 | 3 |
| Payment total | PHP 6,000 | PHP 6,000 |
| Identity accounts | 5 | 5 |
| Applied migration history rows | 9 | 12 |

The extra historical `InitialIdentitySquashed` history entry was preserved; migration history was not rewritten. The secondary localhost database retained its 0 bookings, 0 payments, PHP 0 payment total, and 5 Identity accounts, with history advancing from 8 to 11 entries. Counts are preservation evidence, not a claim that an exhaustive row-by-row data audit was conducted. No account passwords or private customer answers were inspected.

Normal background jobs were enabled during ordinary profile and published-build smoke runs, so their established automatic completion, expiration, inactivity, and notification behavior could execute. No manual booking, payment, or account edits were made. The two isolated rehearsal databases were removed after validation; verified source backups remain. After confirming no disposable integration databases remained, 23 obsolete synthetic fixture directories were removed from the ignored build/temp locations. Runtime/tool dependencies and current evidence logs were retained.

Verified active-database backup: `.tmp-owner-build/recovery-databases/AriesMagicRecovery_20261022135808.bak`. The secondary localhost copy-only backup location is recorded in `.tmp-owner-build/recovery-backup.log` under the SQL Server instance backup directory. These backups contain private database contents, remain outside source control, and must not be published. Migration, rehearsal, and integrity logs are under `.tmp-owner-build/recovery-*`.

## 3. Preventing recurrence

`Program.cs` now checks for pending migrations before starting the server or hosted workers. A missing schema produces an actionable exception explaining backup/review/forward migration and the possibility of user-secret/environment overrides. SQL connection failure produces a sanitized configuration/availability error without connection values. It does not silently create a database or migrate production at startup.

`appsettings.json` explicitly documents `Database:InitializeOnStartup=false` and `Database:ValidateOnStartup=true`. Validation defaults to true even when the setting is absent. `/health` now requires both connectivity and an up-to-date migration history. Normal schema management remains a separately reviewed operator step. A history check cannot detect arbitrary manual corruption after a migration was recorded; integrity checks and proper deployment discipline are still necessary.

`Database__ValidateOnStartup=false` is used only by the documented isolated HTTP security probe, whose deliberately unreachable SQL connection must yield unhealthy readiness while login-denial routes are tested. The production validation rules, authorization, cookie policy, antiforgery, and upload checks are unchanged.

CI now also builds Debug. Its existing integration job automatically runs the new missing-schema/startup/recovery and chat tests, alongside the full migration and security suites.

## 4. Cleanup inventory and decisions

The review inventoried controllers, models/view models, services and DI/hosted registrations, context/migrations, Razor routing and partials, JS/CSS/assets, project/package settings, tests, deployment scripts, and prior audit/recovery documentation. Reference searches were checked against conventional MVC routing, explicit view names, Razor includes, DI activation, serialization, and test/deployment usage. An absent text match alone was not treated as proof of safety.

| Candidate | Classification/action | Evidence or reason |
|---|---|---|
| `ViewModels/BookingCreateViewModel.cs` | Safe; removed | Only its declaration referenced the type. No controller binding, Razor model, serialization path, DI registration, or reflective loader used it. Active booking inputs are BookingStepOne/BookingStepTwo. |
| `wwwroot/js/aries-client.js` | Safe; removed | No layout, Razor page, script loader, test, or deployment reference loaded this older microinteraction implementation. Active Client scripts remain loaded and pass browser checks. No client behavior or network saving is claimed from deleting an already unloaded script. |
| Unused `ApplyFilter` memberships argument | Safe; removed | The private helper never read the argument; the sole call was updated. Filter behavior is protected by the integration/browser suites. |
| Reused `.tmp-owner-build/audit-publish` output | Safe generated output; cleared and rebuilt | Publishing into an existing folder retained the deleted legacy JS. A fresh publish removed it and passed the package exclusion gate. |
| Generated disposable test proofs/database files | Safe only after matching disposable DBs are absent | These are synthetic fixtures, not the preserved recovery backups. Current evidence logs are retained. |
| `_Layout.cshtml.css` and generated styles bundle | Must preserve | Both shared layouts reference the generated CSS bundle; Razor CSS isolation participates in build output. It is not dead because no explicit source filename appears in HTML. |
| `CreateStepOne`/`CreateStepTwo` views and models | Must preserve | Conventional GET/POST controller routes and TempData serialization still use them. The newer combined wizard does not establish that the old routes are unused. |
| Layered Client/internal CSS | Requires further verification; retained | Broad global and role-specific cascades contain intentional overrides. No full selector-coverage proof was available; deleting by text frequency could change the premium design. |
| Dashboard partials and SoftCard view | Requires further verification; retained | Uncertain historical/dynamic partial usage is insufficient grounds for deletion during recovery. |
| Migrations, audit/recovery reports, tests, seeds, financial services, video-disabled boundary | Must preserve | Deployment history, tests, explicit initialization, security, and the documented future processing contract are valid activation paths. |
| Private runtime/tools, OCR language data, native image/OCR/PDF dependencies | Must preserve | Build, regression, upload, and document paths require them; no package was removed speculatively. |

No broad renaming, design-system rewrite, financial refactor, or speculative database index migration was introduced. Existing message `(ConversationId, SentAt)` and booking/date indexes remain in place.

## 5. Refactoring and measured performance

The chat controller previously eagerly loaded every message for every inbox conversation and then loaded the selected conversation's full history again, only to discard all but the latest 250 for display. It also tracked read-only inbox graphs.

Ordinary inbox requests now load only the latest message per conversation, with full unread counts and open-request existence computed in SQL. Inbox graphs use no tracking with identity resolution because the participant/conversation includes form a cycle. Selected conversation details limit messages to 250 in SQL and restore chronological order with a deterministic ID tie-breaker. The tracked membership used to update LastReadAt is fetched separately. Search deliberately retains full-history matching to preserve its existing behavior; that path remains an optimization candidate at very large scale.

The synthetic 1,000-message comparison recorded **4,000 versus 502 distinct message-object materializations**, an **87.45% reduction** under the same EF test process. The baseline probe runs the legacy full-history inbox/detail shapes; the optimized measurement runs the complete inbox plus selected detail, including any other small fixture conversations. This measures entity loading, not wall-clock latency, production memory consumption, or throughput. No production speed percentage is claimed.

Tests verify the exact latest 250 contents and ordering, all 1,000 unread messages, an old open request outside the loaded window, historic-message search, request filtering, and retention of all 1,000 database rows. The controller still supports the existing authorization and client/internal boundaries. No sensitive financial or feedback data is newly cached.

## 6. Security and workflow verification

The real MVC/Identity/SQL suite preserves role restrictions, password hashing, lockout, cookie expiry/logout, CSRF, upload signatures, private proofs, refund transactions, booking concurrency, and feedback consent/withdrawal. It exercises registration/verification with loopback SMTP, venue handling with a loopback mapping server, native OCR, financial verification/rollback, notifications, communication permissions, and synthetic database restore. Owner-only payment/refund decisions remain enforced; the daily confirmed-booking limit stays three and the downpayment remains PHP 2,000.

Client UI/feedback browser suites and role-specific Razor/DOM fixtures protect the established design. Internal browser coverage includes dashboard, shared navigation, packages, calendar previews, notifications, filtering, administration, and profiles. Fixture browsers intercept network services and do not constitute a live provider or production test.

## 7. Verification results

| Verification | Result |
|---|---|
| Dependency restore | Passed with normal NuGet access |
| Debug application/solution build | Passed, 0 warnings/errors |
| Release app and all three executable test/tool projects | Passed, 0 warnings/errors |
| Fresh publish and release package safety | Passed; removed legacy asset absent |
| Current dependency advisory query | No known vulnerable packages reported |
| Real Identity/MVC/SQL integration | 178 checks passed |
| Existing financial/concurrency/security/Owner regression | 289 checks passed |
| Razor fixtures | 87 rendered |
| DOM suite | 555 checks passed |
| Real local Kestrel security probe | 23 checks passed |
| Client browser suite | 94 checks passed |
| Client refinement/native autofill | 39 checks passed |
| Feedback browser suite | 26 checks passed; no detected configured WCAG A/AA violations |
| Dashboard browser suite | 36 responsive fixtures plus keyboard/header/shortcut/trend checks passed |
| Interface, internal navigation, Admin and profile browser suites | Passed; role fixtures and interactions recorded in their recovery logs |
| Filter browser suite | Passed: 26 responsive fixtures, combined GET serialization/reset, Client search, calendar empty-state/refresh/reset |
| Configured local and fresh published app | Started successfully; homepage, health, packages, login, CSS and JS HTTP 200 |
| Protected Owner route, anonymous request | Redirected to login |
| Application runtime logs during healthy smoke test | No new unhandled exception or `fail:` entry |
| Git whitespace check | Passed; line-ending conversion notices only |

The new recovery tests run a connected but unmigrated disposable database through unhealthy readiness, startup rejection, forward migration, healthy readiness, and a successful real homepage request. These would have caught the original deployment/schema mismatch. The broad browser suite initially exposed the header defect described above; final results are recorded after the scoped fix, not by weakening its assertion.

The filter suite also contained a stale label-only assertion that required the word “Reset” while the existing Staff UI uses “Clear Filters.” Its selector now recognizes those equivalent labels, retaining the serialized GET and cleared-search URL assertions. Its empty-calendar test now combines a real Regression Package booking code with Other Package, because the expanded fixture legitimately contains Other Package events at the previously searched venue. Empty-state and refresh/reset checks remain mandatory. No filter action or user-facing label was changed to satisfy these tests.

The SQL integration restore time was approximately 0.83 seconds for its synthetic fixture. That is not a production recovery-time objective or an estimate for the real database.

## 8. Remaining issues and operating instructions

Run locally from the repository with `dotnet run --launch-profile http`, then open `http://localhost:5207`. User-secrets override the checked-in development connection. Inspect the effective server/database privately before any future schema operation; never assume the same database name identifies the same SQL instance. No seed passwords were introduced and no existing passwords were changed.

Before future migration: confirm the environment and effective connection, stop application writers as appropriate, take and verify a backup, rehearse on a new restore, run `scripts/Preflight-FinancialMigration.sql`, generate/review the forward idempotent script with `scripts/New-ReleaseMigrationScript.ps1`, apply through the approved deployment process, then verify readiness and critical workflows. Publish into an empty, verified output directory to avoid stale assets. Do not reset a database to fix missing tables.

External release blockers in `RELEASE_BLOCKERS.md` and `DEPLOYMENT_RUNBOOK.md` remain open, including real credential rotation/recovery, provider delivery, target selection and hosting runtime evidence, production backup/restore and monitoring. Blank/unavailable real SMTP credentials prevent claiming successful real-provider registration mail. Automated registration uses generated accounts and loopback SMTP, never a weakened email-verification rule.

Feedback photo storage still requires a persistent private deployment location and an actual verified malware scanner. Missing scanning remains fail-closed/quarantined. Video acceptance remains disabled pending the documented isolated processor. No Azure storage, production database, remote hosting, provider setting, or production account was changed.

The full repository contains broad historical CSS and some large read/search paths. They were inventoried and preserved where use or production scale could not be verified safely. No formal proof that every remaining file is necessary, full load test, screen-reader certification, or real-user acceptance is claimed. These limits do not undo the verified local recovery; they are why the overall classification remains PARTIALLY STABLE rather than production-ready.

## 9. Changed source files

- `Program.cs`, `appsettings.json`, `Services/DatabaseHealthCheck.cs`: explicit schema readiness/startup configuration and actionable failures.
- `Controllers/CommunicationsController.cs`: bounded query materialization and preserved inbox aggregates/search.
- `wwwroot/css/internal-ui.css`: internal scroll containment.
- Deleted `ViewModels/BookingCreateViewModel.cs`, `wwwroot/js/aries-client.js`: verified unused source.
- `tests/ReleaseIntegration/RecoveryRegressionTests.cs`, runner integration: schema recovery and measured chat regressions.
- `tests/OwnerRegression/dashboard.browser.mjs`: explicit document-scroll regression assertion; `filters.browser.mjs` recognizes existing reset labels; README records isolated-probe configuration.
- `.github/workflows/release-verification.yml`: Debug build gate; existing integration execution includes recovery tests.
- This report and deployment/release notes: root causes, backup evidence, limitations, and safe operation.

All changes remain uncommitted for review. No application architecture, booking/payment/refund rule, or migration history was removed.
