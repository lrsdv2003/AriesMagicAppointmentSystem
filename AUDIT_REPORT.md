# Production readiness audit — continuation

Date: 22 October 2026 (Asia/Manila)

**VERDICT: NOT READY FOR PRODUCTION.** The repository has additional tested fixes, a patched private runtime and substantial real-Identity/SQL integration evidence. Mandatory live/provider checks remain open. The owner confirmed that the Azure target is still being decided; no target, live subscription or production-source backup has been verified.

Use [RELEASE_BLOCKERS.md](RELEASE_BLOCKERS.md) for issue severity, component, root cause, corrective action, status, verification and ownership. Use [DEPLOYMENT_RUNBOOK.md](DEPLOYMENT_RUNBOOK.md) for executable preparation, credential recovery, Azure inspection, restoration, migration, smoke testing, monitoring and rollback.

## 1. Audit summary and original blockers

The previous pass removed default-account seeding, enabled lockout, protected financial uploads, added transactional refund/capacity checks, security headers, antiforgery, rate policies and initial regression coverage. Those protections remain. This continuation focused on outstanding evidence and actual defects rather than repeating the complete initial audit.

| Original blocker | Current evidence | Remaining dependency |
| --- | --- | --- |
| Historical SMTP credential | Current source has no SMTP password; prior confirmed history exposure remains. SMTP handling now has bounded timeouts, proper TLS mode and sanitized exceptions. Loopback SMTP integration passes. | Google account owner must revoke the exposed app password, securely configure a replacement, and prove old rejection/new delivery. **Not revoked here.** |
| Historical default accounts | Seeding remains roles-only; an explicit recovery-verified operator tool and transactional invalidation service were implemented. Real old-password rejection, normal reset, new login and old-cookie rejection pass locally. | Select/inventory the actual environment, verify ownership/recovery channels, then authorize any affected-account changes. No deployed account was modified. |
| Outdated runtime | Private .NET/ASP.NET 9.0.21 downloaded from Microsoft with SHA-512 verification; selected runtime confirmed by startup and test logs. Runtime minimum and Identity/EF packages now 9.0.21. | System installation remains 9.0.10; actual Azure worker is unknown. Hosting must be patched and supported at release time. |
| Live Azure configuration | Read-only sanitized inspection script, runtime/package gates, restore planner and detailed runbook prepared. | Owner is still choosing target. No Azure live settings, certificates, identities, networks or alerts were inspected. |
| Backup restoration | Actual native SQL backup and separate-database restore passed checksum/integrity/constraint/history checks and authenticated application reads. Latest measured local recovery: **0.70 seconds**. | This is synthetic LocalDB evidence, not a production-source Azure restore, production RTO/RPO or private-file recovery rehearsal. |
| Real integration | **94 new release checks pass** with actual Identity cookies/tokens, real SQL, native OCR, loopback SMTP/geocoding and the application's MVC pipeline. | External Google/Nominatim access, Azure native-library/storage behavior and authenticated staging UAT still require the chosen environment. |

Critical historical credential exposure remains open until provider/account evidence establishes revocation or absence of affected deployed accounts. Other high release gates are live hosting, real-source recovery, external integration and operational readiness.

## 2. Fixes implemented in this continuation

### Financial email consistency

Added `EmailOutboxMessage`, `EmailOutbox`, `EmailOutboxDispatcher` and a background worker. Financial verification/rejection/schedule-lock email work is queued inside the database transaction; SMTP failures no longer roll back or falsely fail these committed financial operations. Verification notification and audit writes remain inside the transaction. Queue events have unique keys, atomic worker leases, capped backoff and ten-attempt failure retention. Successful delivery clears recipient/body/attachment payload while retaining event metadata.

SMTP delivery is **at least once**: a crash after SMTP acceptance but before database acknowledgement may duplicate an email. The queue never transfers or retries money. Failed/aged items require monitoring. Reschedule and account verification/recovery emails retain synchronous behavior; the runbook distinguishes that remaining scope.

`SmtpEmailService` now uses a 30-second bound, implicit TLS on port 465 and required STARTTLS on other TLS-enabled ports. Production startup validates required TLS/provider configuration. Provider responses and inner exceptions are replaced with a generic `EmailDeliveryException`; logs retain failure type rather than credentials or personal content.

Evidence: actual SMTP authentication rejection, persisted retry state, successful retry, ordinary non-redelivery, queue rollback, and forced audit-outage rollback all pass. Actual Google TLS delivery remains unverified.

### Credential recovery without arbitrary lockout

`AccountCredentialRecovery` refuses invalidation unless recovery has been explicitly verified and the account has a confirmed email. It removes the old password, rotates the security stamp, deletes stored Identity tokens and writes an audit record in one transaction. It does not create or disable users.

`tests/ReleaseAdmin` is a built, explicit operator tool, read-only by default and limited to a single reviewed account. Application of changes requires recovery/approval flags and a securely injected connection string. The service's successful and refused paths are integration-tested; the CLI was compiled but was not run against real accounts. Production sessions normally revalidate stamps within one minute; the isolated revocation test makes that interval immediate to avoid waiting, without changing production policy.

### Database integrity and concurrency

Prepared two forward migrations:

- `20261021182539_AddTransactionalEmailOutbox`: adds the private queue and indexes.
- `20261021183211_ProtectFinancialHistory`: adds positive/approved refund amount constraints, a schedule index and NO ACTION relationships from financial records to Bookings. No existing data table is dropped or reset in either Up migration.

The full chain applies to an empty test database. Generated forward idempotent SQL executes twice successfully. Invalid money and duplicate outbox keys are rejected directly by SQL, and booking deletion cannot cascade through financial history. Real-cookie simultaneous refund approvals and reschedules preserve original-receipt and daily-capacity limits; the original concurrent confirmation test also passes. SQL contention now returns a generic 409 response rather than being treated as an unhandled application error.

Legacy production rows still need preflight review. Constraints may correctly refuse invalid legacy rows; no automatic data deletion/correction was introduced. Migration Down operations must not be used casually: they can drop queue data or restore cascades. Operational edits and multi-instance background jobs beyond these tested paths remain unverified.

### Runtime, configuration and release controls

The target remains `net9.0`; `.runtimeconfig.json` requires both shared frameworks at 9.0.21 with LatestPatch roll-forward. Identity, EF Core and the test-host packages use 9.0.21. No major framework migration was attempted. The private patched runtime lives under ignored `.tmp-owner-build/dotnet-9.0.21`; it does not alter the system installation. A startup log records the selected runtime without secrets.

Production connection validation now rejects the local development default, LocalDB, missing database, disabled encryption or trusted-server-certificate bypasses. Financial proofs support a configured private root outside the deployment artifact. Existing relative proof paths are preserved, so deployed file relocation requires an approved backup/copy procedure. Profile storage and shared key persistence still need target-specific configuration.

Added a pinned EF tool manifest, fail-closed dependency advisory script, package/runtime gate, idempotent migration generator, read-only Azure inspector, separately named Azure restore planner, SQL preflight/restore checks and a CI workflow with no deployment job or production credentials. Scripts parsed; migration generation and local advisory/package gates executed successfully. Hosted GitHub CI, Azure inspection and Azure restore execution remain unverified.

## 3. Security and credential assessment

Identity remains the sole production authentication scheme. The new release harness does not use header/fixture authentication. It exercises actual MVC authorization/antiforgery with real Identity cookies and SQL identities; SMTP and geocoding use real loopback network servers. `WebApplicationFactory/TestServer` runs the application HTTP pipeline in-process; it does not prove Azure reverse-proxy/TLS behavior. The separate Kestrel probe covers actual local HTTP transport.

New tests verify unrelated-client booking/proof denial, Owner-only verification, failed logins/lockout, logout, expired tickets, password recovery and security-stamp revocation. Existing source-level/default-credential removal and the initial authorization/file protections remain intact.

No provider credential was revoked, no replacement Gmail credential was obtained, no Google message was sent, and no real default account or session was changed. Provider tools were checked; no connected credential-management integration was available. Azure CLI is not installed here and the owner has not selected the deployment target. The precise actions and responsible people are recorded in the runbook/tracker.

The dependency advisory gate reports no known vulnerable resolved application packages after servicing updates. This does not establish full ASVS compliance, absence of unknown vulnerabilities, or patched JavaScript/native/system/production components. Full script CSP, privileged MFA, distributed rate limiting, comprehensive image decoding and target-specific proxy controls remain scoped hardening work.

## 4. Database and recovery evidence

All writes in this pass used uniquely generated disposable LocalDB databases. No configured application database or Azure SQL data was modified. The integration harness uses actual migrations, not just EnsureCreated. The original suite continues to use its isolated synthetic schema.

The latest synthetic backup drill used COPY_ONLY and CHECKSUM, RESTORE VERIFYONLY, RESTORE into a separately named database with distinct physical files, DBCC CHECKDB, DBCC CHECKCONSTRAINTS, booking/payment totals, refund/timeline/migration-history comparison and successful Identity login/application reads on the restored database. Recovery measured **0.70 seconds for this small fixture**; it is not a production recovery-time guarantee. Backup and proof fixtures remain in ignored local test output; only generated test databases are cleaned up.

No production backup strategy, restore point, retention, geo-recovery, storage backup, production RPO/RTO or financial reconciliation after a real incident is verified. The runbook includes a plan-only Azure PITR script and guarded SQL review procedures. Creation of a billed restore database, production schema changes or cutover requires explicit authorization.

## 5. Testing results and evidence

| Check | Result / scope |
| --- | --- |
| New Release integration suite | **94 passed on selected runtime 9.0.21**, with Identity/EF 9.0.21 |
| Existing SQL/controller/role regressions | **289 passed**, including concurrent confirmation; 68 synthetic role pages rendered |
| Actual Kestrel security probe | **23 passed** on runtime 9.0.21, with unreachable test SQL and jobs/migrations disabled |
| UI DOM suite | **441 passed** after fixture refresh |
| Affected responsive browser checks | **12 passed**: Owner dashboard/payment/refund screens at 1440/1024/768/390, plus existing mobile navigation/focus/filter/menu interactions |
| Prior full responsive baseline | 156 checks retained from the initial pass; complete unchanged UI suite was not redundantly rerun |
| Release builds | Application, integration harness, original regression harness and operator tool compiled; zero warnings/errors |
| Local Release publish | Passed; minimum runtime and exclusion of private/test/deployment artifacts passed |
| Package advisories | No known vulnerable direct/transitive application packages reported by refreshed sources |
| Migration generation/application | Forward idempotent SQL generated/reviewed; full chain plus twice-run script passed locally |
| Script validation | PowerShell parsing passed; Azure restore plan-only mode and local gates exercised; no Azure mutations |
| Git whitespace | Passed |

Evidence files: `.tmp-owner-build/release-evidence.json`, `release-integration-final.log`, `audit-regression-final.log`, `release-ui.log`, `dependency-advisories.json` and `release-migrations.sql`. The JSON explicitly records synthetic source, selected runtime, local test count, measured recovery and false staging/production/provider-revocation flags. These ignored files are local evidence, not a durable production audit store.

Initial harness content-root/token-page issues and a corrupt preliminary image fixture were corrected. The final fixture is a generated receipt that native Tesseract actually reads as PHP 2,000; this is stronger than merely accepting an OCR failure. A transient NuGet file-exists error was followed by a successful clean restore. Expected SMTP/constraint/audit failures are intentional test cases, not silently ignored regressions.

See `tests/ReleaseIntegration/README.md` and the runbook for reproducible commands. No claims are made for actual Google TLS transport, Nominatim availability/rate policy, Azure native OCR compatibility, production load, production restore, hosted CI execution, full contrast/screen-reader coverage or every live control.

## 6. Azure configuration and deployment readiness

**No staging or production verification has occurred.** The target is undecided. Required names, exact safe inspection/restore commands, migration steps, credentials procedure, storage/key/proxy/timezone considerations, monitoring and rollback are in `DEPLOYMENT_RUNBOOK.md`.

Mandatory target evidence includes HTTPS Only/TLS/certificates; explicit allowed hosts; Production environment; secure SQL/SMTP and Key Vault resolution where used; selected runtime; SQL/storage network and least privilege; durable proof/profile/key storage; `/health`; real monitoring/alerts; staging integration; restored production-source data/files; and an approved rollback rehearsal. Read-only Azure output is designed to contain booleans/metadata, not raw secrets.

Microsoft's current servicing baseline is 9.0.21, and .NET 9 support ends **10 November 2026**. Choose a supported runtime at the actual release date and complete a tested .NET 10 LTS migration before that deadline. The package release-policy gate refuses clearance from that date. [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

## 7. Remaining issues and shortest safe path

| Severity / owner | Required next action |
| --- | --- |
| Critical — Google account owner | Revoke the historical SMTP app password, review activity, configure the replacement securely and record old rejection/new approved-recipient delivery. [Google app-password guidance](https://support.google.com/accounts/answer/185833?hl=en). |
| Critical until inventory — application owner/operator | On the chosen deployment, identify affected default accounts, verify legitimate recovery channels and authorize targeted invalidation/reset. Retain evidence of password/session rejection and recovery. If no affected accounts were ever deployed, establish that through inventory. |
| High — release/Azure owner | Select the app/slot/host/OS/plan; patch and verify the actual worker; run the sanitized read-only checks and target-specific TLS/network/storage/key/monitoring verification. |
| High — database recovery owner | Restore an approved real-source backup to a new isolated review DB, verify constraints/records/application/files and measure realistic recovery; approve migrations only after preflight. [Azure recovery guidance](https://learn.microsoft.com/en-us/azure/azure-sql/database/recovery-using-backups?view=azuresql). |
| High — release engineer | Pass provider-connected staging UAT, hosted CI, alert delivery and rollback rehearsal; obtain approval for production deployment. |
| Medium — engineering/operations | Validate multi-instance jobs/other operational edits, profile persistence, private-root migration, key protection/proxy/timezone, remaining synchronous operational email failure handling, full CSP/MFA/distributed limits and native file-processing bounds. |
| Low — QA | Complete remaining screen-reader/contrast/every-control UAT; GET notification marking and plain status-page UX remain as previously documented. |

The current work resolves significant local engineering and evidence gaps. These external release gates cannot be completed by changing repository text or inventing a target/credential. No production deployment, resource creation, credential rotation, history rewrite, production migration or real-account change occurred.

## 8. Final production clearance

**VERDICT: NOT READY FOR PRODUCTION.** Preserve this classification until the mandatory provider/account/runtime/Azure/real-backup/integration/monitoring/rollback evidence is complete. The runbook and tracker distinguish **implemented**, **tested locally**, **requires external action**, **unresolved**, and the currently empty **verified in staging/production** categories. A prepared release process and passing local tests are not production clearance.
