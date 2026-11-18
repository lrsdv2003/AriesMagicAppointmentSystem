# Policy release validation — 3 November 2026

**Local validation: PASS. Production deployment: NO-GO pending current operational evidence and review. No production changes were made.**

## Resolved local blockers

The failure was execution identity isolation: `CodexSandboxOffline` could not access the desktop user's LocalDB and Windows SQL credentials. Under the existing desktop identity, the existing LocalDB 15.0.4382.1 starts and the local SQL Server 2022 (16.0.1000.6) accepts integrated authentication. No ACL, SQL login, server permission, service identity, encryption policy, or system security setting was changed. Test databases use generated names and are removed by their harnesses.

The native OCR fixture also exceeded Windows native path limits in the deeply nested checkout. Test-only storage now uses a short unique temporary directory. Application storage mappings are unchanged.

## Migration and recovery evidence

The exact forward migrations are `20261102185718_AddBookingPaymentFeedbackPolicy` and `20261102192836_AddCancellationPaymentFeedbackPolicy`. Their Up operations add nullable audit/consent/timestamp fields, safe defaults, and the proof-revision table/index. They do not fabricate legacy verification times or consent and do not drop/update business records.

Synthetic SQL tests applied the full migration chain, ran the forward idempotent SQL script twice, authenticated against the migrated database, and restored a native SQL backup into another generated database. A populated restored synthetic copy was brought to the preceding schema, upgraded through both migrations, and checked for booking/payment/refund/feedback preservation and application startup/readability. Downgrades were used only to construct this disposable fixture, never on an existing database.

The retained hosted backup dated **28 October 2026** was independently hash-verified against the previously recorded SHA-256, staged in SQL Server's already-configured protected Backup directory, and restored into a new `AriesPolicyRecovery_<GUID>` database. RESTORE VERIFYONLY, DBCC CHECKDB before/after migration, both forward migrations, and booking/payment/refund/feedback count plus payment-total comparisons passed. The restored test database was removed afterward; the original backup remains intact. The staged copy remains in the protected SQL Backup directory.

The retained backup has **no embedded SQL backup checksums**. RESTORE WITH CHECKSUM correctly rejected it; the successful evidence is file SHA-256, VERIFYONLY, an actual restore, and full CHECKDB. This does not establish current production recovery coverage. A new provider-supported backup should use checksums where available.

## Test results

| Check | Actual result |
|---|---|
| ReleaseIntegration, real Identity/MVC/SQL | 234 passed on .NET 10.0.13 |
| Independent findings suite | 112 passed |
| OwnerRegression, disposable SQL and Razor rendering | 502 passed; 107 rendered pages |
| DOM | 925 passed |
| Feedback browser interactions | 29 passed |
| Workflow browser/axe | 54 page/state scans passed at 1440, 390, 320px |
| Screenshot artifacts | 66 PNGs: full pages plus dialog viewport captures |
| Build/publish | Release win-x64 publish succeeded |

Coverage includes strict cancellation boundary, Staff review, three refund scenarios, authoritative payment amounts, initial-payment lockout, replacement/revision history, capacity/concurrent approvals, money integrity, terms, image/MP4 validation, real private preview authorization, consent/withdrawal, scanner protocol, quarantine promotion only after clean scanning, bounded missing-file failures, expired Processing recovery, and the owned antiforgery-protected retry route. Background jobs and SMTP are isolated; tests do not prove live scanner/provider/email behavior. ASP.NET TestServer cannot enforce IIS transport body limits; those require hosting verification.

The earlier findings fixture was updated to the new policy: it now expects a verified initial payment to lock further uploads and tests replacement against a separate unverified booking. The source's previous transaction/retry protections remain exercised.

NuGet emitted NU1900 because vulnerability-feed metadata could not be fetched during one build. Compilation and runtime tests passed; this is not a completed fresh dependency-vulnerability audit.

## Visual review and preservation

Screenshots and machine results are under `.tmp-owner-build/policy-visual/`; feedback interaction screenshots are under `%TEMP%/AriesOwnerSnapshots`. Desktop/mobile Client, Staff and Owner views include cancellation confirmations, populated payment options, refund review, proof revisions and explicit feedback consent. Current screenshots show no horizontal page overflow or browser JavaScript errors. Axe scans ran WCAG A/AA tagged rules; this is scoped automated evidence, not full assistive-technology certification.

The current Client navy/burgundy/gold system, typography, marketing pages and booking/dashboard structures are retained. Accessible names now derive from their visible text rather than conflicting aria-label overrides; this has no visual effect. Proof-history cells use horizontal table scrolling instead of letter-by-letter wrapping. Historical fixture HTML/images were preserved under `.tmp-owner-build/policy-baseline` before rendering. They are not all matching-state controlled baselines; no pixel-perfect whole-site regression claim is made. Human sign-off of the new policy states remains appropriate.

The branch is based on `27d492c`; the separate Phase 8 checkout/commit `925e026` was not merged or deployed. Internal screenshots reflect this branch's existing internal layout, not the independent Phase 8 draft. GitHub CLI is unavailable here, so current remote reviewer/protection state has not been confirmed or bypassed.

## Local package

Package: `.tmp-owner-build/policy-release-win-x64` (framework-dependent .NET 10, win-x64).
Manifest: `.tmp-owner-build/policy-release-manifest.json`.

Files: **500**. Bytes: **204,337,278**. Manifest SHA-256: `061535FABA53ECDC5322E88F719BAB6C034964942CD5847C9E39B0150E939FAE`.

Published web.config uses OutOfProcess and stdoutLogEnabled=false. Inventory screening found no App_Data, wwwroot/uploads, environment-specific appsettings, PFX, SQL backups, or key envelopes. The original frozen 498-file candidate was not modified. This new package has not been uploaded. Preserve live configuration and private data; the package is not authority to replace server-managed settings.

## Genuine production blockers and owners

1. **Current backup/recovery set — owner/hosting operator.** The SQL backup is dated October 28 and the latest documented application backup is October 29. Obtain a fresh SQL backup and current application/private-media inventory, with protected retention, integrity manifest and isolated restore. Verify all external feedback storage and Data Protection recovery dependencies are covered. The successful old-backup test does not resolve freshness/completeness.
2. **Effective hosting configuration and recovery dependencies — operator/myASP.NET support.** Confirm metadata-only Production environment, intended SQL/TLS target, AllowedHosts, SMTP presence, startup validation enabled, H4 PFX inactive, unchanged content root/discriminator/repository/decryption dependencies, and exact private storage mappings/access. Do not export secret values or reset keys.
3. **Live scanner and feedback storage — operator/provider.** Verify actual private feedback root, worker write/read access, antivirus scanner reachability/protocol/limits, quarantine retries, and representative media playback on the hosting platform. Local scanner fixtures are not hosting evidence. Do not disable scanning or publish quarantined files.
4. **Review and deployment target — repository owner/release operator.** Review this feature branch without Phase 8, satisfy actual GitHub reviewer requirements, approve the migration script and artifact hashes, and verify the current FTPS session/IIS `/ariesmagic` mapping. Remote reviewer status was not available in this operation.

## Validated release order and recovery

After the above evidence is complete: verify all artifacts against the manifest; establish the current rollback set; place an owned maintenance marker and confirm it is active; take the final SQL recovery point; apply only these forward migrations in timestamp order; verify migration history; transfer application-owned files through the established FTPS method without deletion synchronization; preserve live configuration, App_Data, uploads, keys and external storage; verify uploaded hashes; remove only the owned maintenance marker; run public and approved synthetic-account smoke tests.

Before reopening writes, an application failure can be handled by restoring the verified prior application-owned binaries/assets while leaving additive schema in place. Do not run Down migrations or restore an old SQL backup blindly. If new business writes have occurred, keep maintenance active and reconcile recovery/forward-fix with the owner/DBA; a database restore could discard legitimate financial events.

No hosted migration, file transfer, configuration edit, pool recycle, credential change, PFX activation, GitHub merge, or deployment was performed. Local validation has resolved the previous SQL/test blockers. Production remains NO-GO until the four evidence gates above are satisfied.
