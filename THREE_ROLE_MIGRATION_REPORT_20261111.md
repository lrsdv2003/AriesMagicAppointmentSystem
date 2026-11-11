# Admin-to-Owner consolidation — implementation and release report

Date: 11 November 2026. Branch: `codex/owner-three-role-consolidation`.

**Local validation: PASS. Production deployment: HOLD / not performed.** No production SQL, account, configuration, key, upload or hosting change occurred during this implementation.

## Sequenced implementation and source identity

- Deployed-source baseline: `9d0cd6d62d41db00a8de4f737a0792ad9fbc9cfa` (verified Owner Round 1 release).
- Audit recorded before changes in `THREE_ROLE_MIGRATION_INVENTORY.md`.
- Owner transfer checkpoint: `308b72d`, validated with 546 SQL/controller/security checks and 75 browser/viewport cases before Admin retirement.
- Final tested application commit: `85efc2996122b7593b901ab55a0c826121958508`. The published DLL's ProductVersion embeds this exact commit.

## Transferred and consolidated functions

| Function | Final Owner implementation |
|---|---|
| Account administration | Existing Personnel Accounts now provides authoritative name/email, role and status filtering, read-only authorized identities, Staff contact editing, Staff invitations, eligible activation/deactivation and session revocation. One search field serves both server filtering and existing instant search. |
| Packages | Existing Create/Edit/Inclusions/Archive/Restore reused. Owner actions are audited transactionally; existing booking name, money, required downpayment, stored removal names and schedule snapshots are preserved. No new package policy. |
| Calendar | Separate Calendar Settings for Owner, preserving operational Staff calendar and fixed capacity of three. Confirmed dates cannot be blocked. Block/unblock and audit are atomic. A SQL race test checks availability blocking against actual payment confirmation. |
| Archives | Existing failed-booking read-only archives and original attribution; package restoration remains in Packages. No permanent delete or failed-booking restoration invented. Missing historical attribution remains Not recorded. |
| Business Activity | Owner-only read-only business events with original actor/time/role metadata. Technical/authentication diagnostics and raw metadata are excluded from the business view. |
| Notifications/messages | New business-administration notifications reach active Owner recipients. Existing Staff/Client flows and historical private participant/sender IDs are retained; Owner never inherits private Admin conversations. |

The financial Owner dashboard and summary sidebar remain intact. Administration is accessed through Personnel Accounts, Packages, Calendar Settings, Business Activity and Archives. Navigation destinations are deduplicated. Existing navy/blue styling, typography, photography and financial hierarchy remain. Existing Owner CSS is an unchanged prefix; 425 appended characters style only the new account filters/contact form.

## Removed active components and permission model

Removed `DashboardController.Admin`, Admin dashboard Razor/CSS, `UserManagementController` and its duplicate views/model, the old Calendar Admin branch/view, Admin login redirects, Admin invitation/delegation/role-change options, active Admin authorization and recipient selection, obsolete dashboard fields and Admin role initialization. Shared administrative CSS/JS remain because Owner uses them. `AdminActivityPresentation` is now `BusinessActivityPresentation`.

Exactly three active roles are accepted: **Owner / Staff / Client**. Owner controls business administration and financial decisions; Staff retains authorized booking/event coordination; Client retains own booking/payment/communication requests. Public registration stays Client-only. Staff/Client cannot access the migrated Owner features. No role-promotion API, default privileged password or publicly reachable setup endpoint exists.

Legacy-only login and previously issued cookies are rejected. Historical Admin enrollment tokens cannot activate accounts. A mixed legacy/active membership retains only its already-authorized active role; no new permission is granted. Personnel never disables/promotes an Owner and cannot overwrite login email/password through Staff contact editing.

## Database and legacy identities

**No EF schema migration is added or required.** Migration/model files are unchanged. No user, role definition, booking, payment, audit, message or historical metadata is automatically deleted or rewritten. Stored `AdminRemarks`, enum value `RejectedByAdmin = 6` and historical role labels remain for integrity.

An isolated restore of the earlier production backup identified one legacy Admin assignment; that exact ID stays in protected operator evidence and is not published here. This is snapshot evidence, not a new live account audit.

Optional `PersonnelOperator.dll retire-legacy-admin` requires separate approval of the exact database/identity plus an enrolled active Owner. It removes only the legacy membership through UserManager, disables a legacy-only account, revokes pending legacy invitations/security stamps, retains existing Staff/Client memberships and audits atomically. It refuses Owner targets and never promotes/deletes users or drops the historical role. It is not called by startup or a controller, and the utility is excluded from publish output. See `docs/THREE_ROLE_RELEASE_RUNBOOK.md`.

## Executed validation

| Check | Actual result |
|---|---|
| Release builds | PASS, zero warnings/errors in final build logs |
| Owner SQL/controller/security + rendered Razor | **531 checks passed** |
| Full SQL/Identity/MVC workflow integration | **253 checks passed** |
| Independent findings regression | **112 checks passed**, including concurrent financial/booking decisions |
| Personnel real Identity/HTTP and retirement | **117 checks passed**, including stale cookies, forged roles, enrollment replay/expiry, CSRF, role boundaries and unblock audit rollback |
| Offline operator contract | **13 checks passed**, including exact-identity approval, Owner prerequisites and preserved history |
| Owner email security | **37 service + 27 HTTP checks passed**; synthetic delivery only |
| Current-fixture DOM | **910 checks passed** across 107 rendered fixtures; stale four-role HTML excluded by manifest |
| Browser, keyboard and axe | **80/80 page/viewport cases**, zero axe violations and zero document overflow at 1440/1024/768/390/320 |
| CI/deployment safety | **57 checks passed**; no live connection |
| Git index private-artifact scan | PASS, no findings |
| Publish policy and inventory | PASS, all 591 packaged file hashes/inventory membership verified |

The full integration suite performed native SQL COPY_ONLY/CHECKSUM backup, VERIFYONLY, isolated restore, CHECKDB/check-constraints, application login/read against the restored database, populated pre-policy forward upgrade and legacy-record checks. Latest measured restore phase: 2.90 seconds. Databases were GUID-owned disposable local SQL Server 2022 fixtures; none were hosted production or existing development databases. The SQL service's existing default directories were used, with no ACL, identity or security-policy change. No workstation hosted-SQL probe was requested.

Older fixtures were corrected to submit the already-approved current terms version and explicit payment/refund acknowledgments, and to request rescheduling only after verification. Test-only SQL selection supports the existing local instance explicitly. The offline cross-process check reads fresh role rows instead of stale EF-tracked state. These are test updates, not relaxed application policies.

Visual baseline and current screenshots are under ignored `tmp/three-role/before` and `after`. Normal captures retain the 900px viewport/internal scroll pane; `-complete.png` captures show full content at the same width using expanded height. Desktop financial dashboard, mobile Personnel/Staff contact form, Calendar Settings, Archives and Business Activity were visually inspected. Header pixel differences are confined to the unchanged logo raster; text/layout outside that image matches at all five widths. Financial fixture values naturally differ as test records are added. No claim of whole-page pixel identity is made.

Other supplemental browser scripts were updated to remove positive retired-role routes and syntax-checked, but not every historical diagnostic script was rerun. Automated axe checks are not a claim of a full manual assistive-technology audit. Local SMTP/API tests do not establish production email delivery.

## Coherent deployment candidate and hashes

Package: `publish-net10-owner-three-role-20261111`. Framework-dependent **.NET 10 / win-x64 / IIS OutOfProcess**. Existing live launcher is retained. No merged compiled DLLs from different releases.

- Packaged files: **591**.
- Application-owned inventory: **588** paths.
- Byte-identical to last verified deployed baseline: **582** paths.
- One additional runtime JSON is semantically identical with a byte-format difference; preserve the live file. Runtime target, framework requirements and dependency graph are unchanged.
- Exact prospective delta transfer: **5 files**, listed below.
- `appsettings.json`, `appsettings.Production.json`, `web.config`, launcher, keys, private data and all hosting overrides are excluded from this transfer. No test tools/databases, enrollment secrets, PFX/key envelopes or private uploads are packaged.

| Artifact | SHA-256 |
|---|---|
| Full package manifest | `55715EE3CBAE211FBB56CB286A2F94DCBB1761B6B39A3F0DC5E074A3A3F0C7C3` |
| Application-owned inventory | `885F6D8D21F8D8B15D211698106E83EA19F03D4241AC7A01316C0594B62A4E91` |
| Exact delta allowlist | `4EA2A715F012534A0F3905FE832B75411E9B4CE67106FE708650089485B0761C` |
| Application DLL | `F39E07416765CEDF6D3A0F98783E54F8E53B50180FBFE69953608683C64E6B36` |

Exact delta paths:

1. `AriesMagicAppointmentSystem.dll`
2. `AriesMagicAppointmentSystem.staticwebassets.endpoints.json`
3. `wwwroot/css/owner-round1-20261109.css`
4. `wwwroot/css/owner-round1-20261109.css.br`
5. `wwwroot/css/owner-round1-20261109.css.gz`

Manifests, per-file sizes/hashes, preserved-runtime explanation and final verification are under `.tmp-owner-build/three-role/release`. The prior verified deployed DLL is `AB43A9CC1226156C6A8360406576C84ADC73D59362F02BEE9B9083D9922051F5`. Baseline reconciliation uses the protected pre-transfer recovery copy plus the eight separately recorded, verified Owner Round 1 deployed files; both local sources were rehashed. The recovery copy alone is the pre-Owner-update version, so it must not be mistaken for the current release. This is the **last verified deployment**, not a fresh FTPS inspection today.

The obsolete Admin CSS and its two compressed copies are absent from the candidate and static-endpoint manifest. Do not delete-sync the server; harmless unreferenced old assets may remain until separately approved cleanup. Existing executable/debug files are not replaced with this package. Previous frozen packages/allowlists are untouched.

## Deployment, rollback and remaining acceptance

**No deployment performed; zero release files uploaded; zero production migrations or account retirements.** The live site has not been used as a test target for this task, so no live three-role acceptance is claimed.

Before a later application transfer: obtain approval of this new commit/allowlist; verify current remote baseline and a fresh coordinated SQL/application/configuration/private-file checkpoint under maintenance; retain existing Data Protection identity/repository/decryption dependencies, H4 inactive, Feedback-Off, SQL/Brevo/SMTP/Personnel settings and uploads. Prior November 11 recovery evidence is not automatically a future fresh checkpoint. External feedback/key recovery limitations remain accurately documented.

Use certificate-validated explicit FTPS to `/ariesmagic`, binary transfers of only the five approved changed files, release-owned maintenance and remote hash verification. Start only the coherent build. Verify existing Owner fresh login, Staff/Client authentication, Personnel/Staff enrollment with an approved mailbox, package management/snapshots, calendar conflicts, Archives/Business Activity, payments/refunds/private proof authorization, notifications/messages, text feedback and Feedback-Off. Stop on startup, authentication, SQL, integrity or private-security failure.

Rollback restores the coherent prior application delta including the matching static manifest/CSS from the verified Owner Round 1 package, not the older pre-update DLL by itself. Preserve configuration, keys, uploads and SQL. No Down migration, SQL restore/reset, Owner replacement or key reset is required or authorized by this report. Optional exact-identity retirement is separate and is not undone by a file rollback.

Documentation delivered: migration inventory, current permission matrix/workflows/Mermaid use-case diagram, Owner guide, alpha/beta task prompts and exact-identity runbook. Original academic research requirements/private defense artifacts that mandate a separate Admin role require EJ/advisor review; no approved objectives were silently rewritten.

**Verdict: local implementation and release preparation PASS; production deployment pending its specific approval and fresh operational gates.** The final application implements three active roles while retaining historical records for integrity.
