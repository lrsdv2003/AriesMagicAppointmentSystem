# Independent findings hotfix — live closed-alpha deployment

Date: 29 October 2026. Public smoke verification: 08:43 UTC / 16:43 Asia/Manila.

**Outcome: deployed and publicly verified; authenticated business acceptance remains pending.** The site is available at https://ariesmagicartist-001-site1.htempurl.com/. No application rollback was required. This is a closed-alpha result, not public-launch approval or a complete disaster-recovery attestation.

## Source and release

- PR #2 was reviewed against its approved head `d379e90c694c3c776cb086171cf2cc386f99f64f` and merged through the normal GitHub PR process.
- Exact deployed main commit: `27d492c2dcd468d4e09630d28ff381560574f990`. Its tracked tree matches the approved PR head. No history rewrite or force-push occurred.
- PR #1 remains open and unmerged. Its deployment automation was not included or dispatched.
- Main CI passed: https://github.com/lrsdv2003/AriesMagicAppointmentSystem/actions/runs/40833820542.
- Exact merged source built in Release with zero build warnings/errors; 109 focused checks passed against isolated SQL. Existing main CI supplied the broader regression and clean-checkout checks. No fault injection or concurrency testing targeted production.
- Canonical hotfix package: `.tmp-owner-build/findings-deploy/hotfix-main-27d492c`, net10.0, win-x64, framework dependent, OutOfProcess. Product version includes the exact merge SHA.
- Publish inventory: 498 files, of which 495 are application-owned and three configuration files are excluded. Only the 20 changed application files were transferred. The original frozen alpha candidate was not changed.

## Backup and recovery evidence

Fresh download-only backup retained outside the project and publicly served paths:

`C:\Users\EJ Ridge\Documents\Capstone Files\Backups\ariesmagic-pre-findings-20261029-072933\ariesmagic`

- 547 files, 112,436,996 bytes; all 495 expected prior application paths present; zero failed backup transfers observed.
- Includes 22 financial-proof files, 23 App_Data files in total, and two public upload files. The earlier 20 proofs and two newer proofs were preserved.
- Complete private SHA-256 inventory is stored alongside the backup. Inventory SHA-256: `D4E421A16410C475CEAE5D050B54FE0A2E84C657F1C4923B419BC387435334BE`.
- Verified prior copies of every affected file are in the sibling `rollback-owned-hotfix` directory, with the private manifest `hotfix-rollback-owned-files.private.json`. All 20 prior hashes were checked before deployment.
- Retained SQL backup: `ariesmagic-live-recovery-5d351a79ca004d649c6001448c55f180\database\hosted-sql-current-20261028.bak` under the same protected Backups parent; 8,048,640 bytes; SHA-256 `C378624530FE4137C8E062E5D029D4F3A7CD5248C6ABC3782BB64617BB3B3136` rechecked. This is the retained October 28 backup, not a fresh October 29 database backup.
- Isolated SQL restoration has not been demonstrated. Complete remote cryptographic inventory, external feedback backup coverage, and external Data Protection recovery material remain unverified. These limitations are not marked resolved.

Application rollback procedure: place an owned `app_offline.htm` in `/ariesmagic`, verify maintenance, transfer only the 20 prior files from `rollback-owned-hotfix` to the matching paths, read back and verify hashes, retire only the owned marker, then repeat public and authenticated smoke checks. Preserve all live configuration and data. **Do not restore SQL or reverse business records for this binary-only rollback.** No schema migration accompanied this deployment.

## Transfer and preservation

The existing authenticated FileZilla explicit-FTPS session at `win8214.site4now.net` was used for the existing `/ariesmagic` application. Target continuity was corroborated by matching public asset bytes to the downloaded application, and by the known live executable. A fresh hosting-panel inspection was unavailable; no panel values were changed.

The live x64 apphost, dependencies, application root and launcher were retained. The source diff contains no Program.cs, schema, migration, Data Protection configuration, or dependency change. Production startup guards remain in place; the deployment did not run migrations or seeding.

An owned 373-byte `app_offline.htm` was uploaded and verified by HTTP 503 plus its unique marker. All 20 replacements took place under maintenance. Successful-transfer counters increased from 2901 to 2921 for the application payload. Two root-file attempts disconnected while the overwrite prompt was pending; both were explicitly retried successfully. The final failed-transfer list and queue were empty. No deletion synchronization was used.

Payload: **20 files / 4,989,113 bytes**, plus the separate maintenance marker:

| Paths | Count |
| --- | ---: |
| `AriesMagicAppointmentSystem.dll` | 1 |
| `AriesMagicAppointmentSystem.staticwebassets.endpoints.json` | 1 |
| `wwwroot/css/client-experience.css`, `client-hub.css`, `notifications.css`, `owner-interface.css`, each with its `.br` and `.gz` counterpart | 12 |
| `wwwroot/js/booking-experience.js`, `client-booking-wizard.js`, each with its `.br` and `.gz` counterpart | 6 |

The exact 20-path inventory and hashes are in `.tmp-owner-build/findings-deploy/EXACT_PLANNED_TRANSFER_FILES.csv` and `FINAL_BASELINE_DELTA.json`. A readback of the two root files and public CSS/JS directories produced 143 verification files; **all 20 deployed payload hashes match the canonical release**.

Preserved: every appsettings file, live web.config, runtimeconfig, apphost, dependencies, hosting overrides, App_Data, uploads, proofs, external feedback mappings, existing keys and recovery archives. No hosting or SQL settings were edited. No private data, test databases, synthetic credentials, certificates, or backup archives were uploaded. H4 PFX remains inactive.

Post-deployment readback independently confirms **web.config and runtimeconfig are byte-for-byte unchanged**. In particular, the live `System.GC.Server=false` setting was preserved even though the generic publish output differs. The live launcher remains OutOfProcess and stdout logging remains disabled. Effective SQL/SMTP/environment override values were not freshly attested; they were preserved in place. Default-provider key repository and old-cookie continuity were not directly inspected or tested, and must not be represented as proven.

The owned maintenance marker was moved reversibly to `/db/ariesmagic-findings-27d492c-app_offline.htm`, outside the mapped application, after checking for a destination collision. It is absent from the application root. FileZilla upload/download defaults both remained `Use default action` after the bounded queue completed.

## Live checks actually performed

All **30 read-only HTTP checks passed**:

- HTTP 200: homepage, health, Experiences, About, Gallery, Login and Registration.
- Expected login redirects: Client Dashboard, booking entry, payment uploads, rescheduling, Notifications, and Staff/Admin/Owner dashboards. Anonymous payment-proof access also redirected to Login.
- `GET /Notifications/Open/999999` returns HTTP 405, confirming the deployed action rejects GET without mutating a notification.
- HEAD returns HTTP 404 for appsettings, production appsettings, alternate casing, encoded-dot production appsettings, web.config, `.env`, and `/App_Data`. Configuration contents were not requested over HTTP.
- All six plain CSS/JS asset responses returned HTTP 200 and matched their release SHA-256 hashes.
- The existing homepage browser tab was reloaded and visually inspected; approved navy, gold, burgundy, typography, navigation and photography remain present. This was a live smoke inspection, not a replacement for the prior full visual regression suite.

The publicly rendered package cards continue to show the existing database-backed Premium, Deluxe and All In packages and their existing prices. No catalog data was edited.

**Not run live:** existing-account login success, authenticated Client Dashboard and booking/payment navigation, Staff/Owner access after login, proof upload/replacement/new installments, balances and duplicate retry behavior, booking/approval decisions, protected notification POST, reschedule decisions/outbox delivery, cross-client venue privacy responses and authenticated OCR review presentation. These behaviors passed applicable isolated tests, but no signed-in approved live test sessions or identified synthetic record IDs were available. The owner's “all of them” response authorizes test roles; it does not establish that every live business record is synthetic. No random record or payment was changed. A live Login tab was left available for sign-in handoff.

## Remaining acceptance and risk

1. Owner/QA: sign in with designated synthetic Client, Staff and Owner accounts, identify synthetic booking/payment IDs, and complete the authenticated scenarios above. Do not introduce real SMTP failures or production concurrency/fault tests.
2. Owner/DBA: demonstrate isolated restoration of an appropriately current SQL backup using an already authorized service-readable location. Do not overwrite production or change server permissions to bypass the earlier restore blocker.
3. Provider/owner: retain evidence for actual Data Protection repository, discriminator and decryption dependencies; verify external feedback storage backup coverage. No key migration was enabled.
4. Provider/owner: retain metadata-only evidence for effective Production overrides. Current functionality and in-place preservation are supporting evidence, not a new complete attestation of every setting.

**Decision: hotfix deployment completed; site available; no observed critical public regression and no rollback required. Authenticated live acceptance and complete disaster recovery remain pending. Continue closed-alpha testing; do not interpret this report as public customer launch approval.**

Operational evidence is under `.tmp-owner-build/findings-deploy/`, including `DEPLOYMENT_OUTCOME.json`, `READBACK_HASH_VERIFICATION.json`, `POSTDEPLOY_PRESERVATION_HASHES.json`, `POSTDEPLOY_PUBLIC_SMOKE.json`, merged-source/CI records and `LIVE_HOMEPAGE_POSTDEPLOY.png`. These operational artifacts were not uploaded to the site.
