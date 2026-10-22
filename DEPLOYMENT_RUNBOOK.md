# Controlled release and recovery runbook

Status: preparation only, 22 October 2026. The release owner has not selected an Azure target. No production mutation, secret rotation, infrastructure deployment or Azure restore has been performed.

## 1. Preconditions and evidence

Keep `RELEASE_BLOCKERS.md` as the clearance record. Require evidence for provider revocation, account recovery, selected worker runtime, target configuration, restored production-source backup, external-provider delivery, monitoring and rollback before changing the verdict. Local synthetic tests are necessary but do not replace those checks.

Assign a release owner, Azure resource owner, database recovery owner and Google SMTP account owner. Obtain explicit approval for credential invalidation, resource creation/billing, schema application, deployment or slot swap. The scripts' execution switches do not substitute for that approval.

Record subscription/resource-group/app/slot names, approved hostnames, database/server identity, backup/restore point, storage/key locations, previous deployment artifact and expected migration IDs. Do not record secret values or reset links. Use a staging slot with access restrictions and synthetic recipients/payment proofs; use no real money transfers.

## 2. Provider credential incident response

The historical SMTP configuration uses Gmail (`smtp.gmail.com`). The current source does not contain an SMTP password, but history does. The account owner must open [Google app-password management](https://myaccount.google.com/apppasswords), identify and revoke the exposed app password, review account activity and other app passwords, and create a uniquely named replacement if app passwords remain the chosen transport. A Google account password change revokes app passwords, but has wider consequences and must be explicitly authorized. [Google guidance](https://support.google.com/accounts/answer/185833?hl=en).

Store the replacement in Azure Key Vault or restricted App Service settings. Give the managed identity only secret-read access where applicable; mark environment-specific secrets as slot settings. Obtain a provider-side revocation record and an authorized old-credential rejection check without printing the credential. Send a test using the replacement to an approved test recipient and record receipt time/message identity without tokens or personal content. This repository cannot revoke Google credentials and no such revocation is claimed.

Historical seeded account candidates include `client2@ariesmagic.com`, `client@ariesmagic.com`, `admin2@ariesmagic.com`, `staff@ariesmagic.com` and `owner@ariesmagic.com`. These are candidate identifiers, not proof the accounts exist in a deployment. Review Identity records and roles on the selected environment. Do not reconstruct or print historical passwords.

The operator tool defaults to read-only and examines one explicit account. Supply the connection string through a secure process environment or secret manager, never a command-line argument:

```powershell
dotnet build tests/ReleaseAdmin/ReleaseAdmin.csproj -c Release
# RELEASE_ADMIN_CONNECTIONSTRING must already be injected securely.
dotnet tests/ReleaseAdmin/bin/Release/net9.0/ReleaseAdmin.dll --email <reviewed-account-email>
```

Before applying, verify the person's ownership and recovery mailbox, validate SMTP delivery, retain a separately authorized Owner recovery path, and obtain approval for that specific production account change. Then the same command with `--apply --recovery-verified --production-change-approved` removes the existing password, rotates the security stamp and removes stored Identity user tokens in an audited transaction. It does not disable or create accounts. The verified owner completes normal Forgot Password. Confirm old-password denial, new login and old-cookie rejection after the one-minute revalidation interval. No refresh-token authentication system is configured in this application. Do not clear every account indiscriminately.

## 3. Runtime and local release gate

The target remains .NET 9 with a minimum .NET/ASP.NET runtime of **9.0.21**, LatestPatch roll-forward, and Identity/EF packages 9.0.21. A private Windows runtime can be installed without changing the computer's system .NET:

```powershell
./scripts/Install-PrivateRuntime.ps1
dotnet restore tests/ReleaseIntegration/ReleaseIntegration.csproj
dotnet restore tests/OwnerRegression/OwnerRegression.csproj
dotnet build tests/ReleaseIntegration/ReleaseIntegration.csproj -c Release --no-restore
dotnet build tests/OwnerRegression/OwnerRegression.csproj -c Release --no-restore
dotnet tool restore
./scripts/New-ReleaseMigrationScript.ps1
./.tmp-owner-build/dotnet-9.0.21/dotnet.exe tests/ReleaseIntegration/bin/Release/net9.0/ReleaseIntegration.dll
./.tmp-owner-build/dotnet-9.0.21/dotnet.exe tests/OwnerRegression/bin/Release/net9.0/OwnerRegression.dll --render
./scripts/Test-DependencyAdvisories.ps1
dotnet publish AriesMagicAppointmentSystem.csproj -c Release --no-restore -o .tmp-owner-build/audit-publish
./scripts/Test-ReleasePolicy.ps1
```

The SDK used to build may be the existing 9.0 SDK; execute the tests with the private patched host or a patched system runtime. Production uses the published framework-dependent artifact and must have compatible patched .NET and ASP.NET Core runtimes. Do not copy the private build/tool directories into production.

Verify the **selected** worker runtime from the application's `Selected application runtime ...` startup log, not only the App Service runtime family or list of installed runtimes. A runtimeconfig change does not install Azure runtime patches. Recheck Microsoft's [servicing/support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) at release time. .NET 9 support ends 10 November 2026; the release-policy script refuses clearance from that date. Complete a tested migration to .NET 10 LTS before that deadline. No major framework upgrade has been attempted in this pass.

`.github/workflows/release-verification.yml` runs builds, advisories, real integration tests, original regression tests, DOM checks and publish-safety checks on a Windows runner with LocalDB. It contains no deployment job or production secrets. The workflow has not been executed on GitHub in this chat; make its successful run a protected release requirement.

## 4. Azure portal settings and read-only inspection

Choose the App Service OS/plan and a staging slot before evaluating platform-dependent settings. Configure HTTPS Only, minimum TLS 1.2+ for app and SCM, valid domain certificates, FTP disabled, Always On where supported, restricted SCM access, managed identity and least privilege. Restrict SQL/storage networking; avoid broad "Allow Azure services" access unless the actual architecture requires and documents it. Enable encrypted SQL with certificate validation (`TrustServerCertificate=False`). Use a deployment identity for DDL and a separate least-privilege application identity.

Required names (no values are provided here):

| Setting | Requirement |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Production |
| `AllowedHosts` | Explicit staging/production hostnames, no wildcard |
| `ConnectionStrings__DefaultConnection` or App Service connection string `DefaultConnection` | Correct remote database; TLS and certificate validation; secure identity/secret |
| `EmailSettings__Host`, `__Port`, `__SenderName`, `__SenderEmail`, `__Username`, `__Password`, `__EnableSsl` | Replacement credentials, valid sender, required TLS; port 465 uses implicit TLS, others STARTTLS |
| `PrivateStorage__RootPath` | Durable private writable root outside webroot for financial proofs, with least-privilege permissions and backup |
| `BackgroundJobs__Enabled` | true for normal operation; false for isolated restore/UAT where job mutation is unwanted |
| `Database__InitializeOnStartup` | false/absent; production never migrates or seeds automatically |
| `VenueDistance__BaseLatitude`, `__BaseLongitude`, radii/speed/fee | Business-verified origin and policy |
| `OcrVerification__ExpectedReceiver` | Actual registered receiver |
| Platform timezone | Manila-compatible scheduling after verifying the chosen App Service OS configuration |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` / platform monitoring settings | Operational telemetry, privacy policy and alerts; verify actual telemetry ingestion |

The private root setting currently covers financial proofs. Internal profile photos still use `App_Data/profiles` and Client photos use `wwwroot/uploads/profiles`; persist and back up those locations or complete a tested storage migration before choosing read-only run-from-package. Before changing a deployed financial root, copy and verify existing proof files with a backup while preserving relative paths. No automatic migration or deletion of existing files occurs.

Verify persistent Data Protection keys, key-at-rest protection and cookie continuity across restarts/instances. Azure slot swaps can invalidate cookies when key rings differ; either establish the intended shared protected key ring or document the controlled reauthentication policy. Do not broadly trust arbitrary forwarded headers. Verify `Request.Scheme`, recovery URLs, secure cookies and rate-limit IP identity through the actual reverse proxy.

Run the read-only script after Azure CLI sign-in on the approved operator workstation:

```powershell
./scripts/Test-AzureRelease.ps1 -Subscription <id> -ResourceGroup <group> -AppName <name> -Slot <staging-slot> -ExpectedHost <staging-host>
```

It serializes booleans/metadata, never raw application setting or connection-string values. Its output lists checks it cannot verify, including selected runtime, reference resolution, network topology, certificates, key persistence, alerts and restore evidence. Local syntax validation is not a successful live Azure inspection.

## 5. Backup restoration and safe migration

Identify an actual production-source backup/restore point, retention policy, storage-file backup and recovery targets. Native LocalDB `.bak` testing demonstrates local mechanics only. Azure SQL Database uses Azure PITR/LTR recovery to a new database, not native `RESTORE DATABASE` against the live Azure source. [Microsoft recovery guidance](https://learn.microsoft.com/en-us/azure/azure-sql/database/recovery-using-backups?view=azuresql).

Prepare the restore plan without execution:

```powershell
./scripts/Restore-AzureSqlReview.ps1 -Subscription <id> -ResourceGroup <group> -Server <server> -SourceDatabase <source> -TargetDatabase aries-restore-<review-id> -RestoreTimeUtc <approved-UTC-time>
```

Only after explicit approval, repeat with `-Execute` and its confirmation prompt. This creates a separately billed database; it never overwrites the source or automatically deletes either database. Record provider operation identity/start/end times, then run `scripts/Preflight-FinancialMigration.sql` against the restored copy using an approved authenticated SQL client. It reports counts only and refuses invalid amounts or mismatched refund receipts. Correct historical data only through an independently reviewed and approved process; never delete records just to pass constraints.

Review these forward migrations:

- `AddTransactionalEmailOutbox`: adds the private email queue and unique event index.
- `ProtectFinancialHistory`: replaces two financial cascade relationships with NO ACTION, adds positive/approved amount constraints and a booking schedule index. It does not drop data tables or delete records. Invalid legacy rows cause the migration to fail safely.

Generate the forward idempotent script on a workstation with patched .NET and the pinned tool manifest:

```powershell
dotnet tool restore
dotnet ef migrations script --idempotent -o .tmp-owner-build/release-migrations.sql
```

Review the exact script and current `__EFMigrationsHistory`; apply first to the restored copy, run `scripts/Verify-RestoredDatabase.sql`, compare booking/payment/refund counts and totals with the approved baseline, and connect an isolated app instance to that database. Disable automatic jobs and outbound real-recipient delivery during restore review. A restored outbox may contain previously pending email; never start dispatch against real recipients merely because restoration succeeded.

After those checks, obtain explicit approval and a current verified backup before applying the reviewed script to production with the DDL identity. Do not run migration Down commands as a routine rollback: the outbox Down path drops queued messages and the financial Down path restores cascade deletion. New databases also require reviewed role/catalog provisioning and a secure Owner onboarding process; automatic default-user creation remains removed.

## 6. Staging integration and smoke tests

Configure approved test recipients and provider sandboxing; never send real payments or issue actual refunds. Verify real cookies for Client/Staff/Admin/Owner, failed/locked logins, verification/recovery tokens, disabled accounts, logout and session invalidation. Confirm direct IDOR/role/CSRF denial. Test the full Client booking → Staff approval → proof upload/OCR → Owner verification → balances/calendar/Upcoming Events path, rescheduling and the existing refund eligibility/review/completion rules. Check simultaneous confirmations, reschedules/refunds, duplicate POSTs, unavailable geocoding/SMTP/SQL, private-file access and restart durability.

Confirm native Tesseract libraries and `tessdata` on the actual OS. Verify true provider delivery/TLS and geocoding success/failure; loopback SMTP/Nominatim contracts are not proof of Google/OpenStreetMap service access. Check pages/assets at desktop/mobile widths and verify secure cookies, HTTPS/HSTS, mixed content and correct redirect hosts in the live slot.

Post-promotion read-only smoke: HTTPS landing/packages, login/logout, protected role pages, public denial of financial proofs, own proof access, current dashboard/calendar totals, `/health` healthy, current migration history, no startup exceptions, and correct selected runtime startup log. Avoid creating paid production records as a smoke test.

## 7. Outbox, monitoring and operational limits

Financial confirmation/rejection/schedule-lock emails are queued with their database transaction. The worker polls every 15 seconds, leases up to 20 items per pass, retries with capped backoff and stops after ten failures. Successful delivery clears body/recipient/attachment payload while retaining event/attempt metadata. Database backup/access controls still protect pending payloads. Delivery is **at least once**: a crash after SMTP acceptance before database acknowledgement can cause a duplicate email. No outbox action sends or retries money transfers.

Alert on application errors, failed health checks, failed/aged outbox items, database availability/resource saturation and backup failures. Verify a deliberate synthetic alert reaches the responsible operator. `FailedAtUtc IS NOT NULL` needs operational review; do not reset or replay delivered items. Inspect event IDs and sanitized failure types, never bodies/tokens in dashboards/logs. Reschedule emails and account-verification/recovery retain their existing synchronous workflows and require separate delivery/failure monitoring.

The outbox lease is multi-worker aware; other archiving/expiry/inactivity jobs still need scale-out validation. Until that is complete, use one active application worker instance and validate restart behavior. Do not claim that this configuration alone proves adequate production capacity.

## 8. Promotion, rollback and clearance

After all mandatory evidence passes, have the release owner review the exact artifact/config/migration plan and approve the production deployment or slot swap. No command in this runbook has been executed against Azure in this chat.

For an application regression, return to the previous approved artifact/slot after verifying schema compatibility. Stop or isolate outbound email dispatch during investigation; preserve queues, proofs, audit records and logs. Do not reverse migrations or overwrite a database reflexively. If data recovery is needed, restore into a new isolated database, verify financial/audit consistency and file backups, assess data loss, then obtain explicit approval for connection cutover. Record incident timing and reconcile any post-backup transactions before resuming writes.

Change `AUDIT_REPORT.md` to READY only after the real provider/account/runtime/Azure/backup/integration/monitoring/rollback evidence is attached to `RELEASE_BLOCKERS.md`. An undecided target, successful local publish or prepared checklist does not satisfy that gate.

## Feedback feature addition

Before deploying post-event feedback, follow FEEDBACK_FEATURE.md for the additive migration, private persistent media root, ClamAV networking/signature checks, worker configuration, consent/role smoke tests, and backup coverage. Video acceptance remains disabled. Verify real scanner responses and storage persistence on the selected hosting target; local fixtures do not close these deployment gates.

