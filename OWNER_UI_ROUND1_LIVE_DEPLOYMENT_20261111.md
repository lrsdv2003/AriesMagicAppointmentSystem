# Owner UI Round 1 — verified live deployment, 11 November 2026

**PASS — deployed and live-verified for the existing closed-alpha environment.** Target: https://ariesmagicartist-001-site1.htempurl.com; explicit certificate-validated FTPS destination `/ariesmagic`, using the existing authorized FileZilla hosting profile through the established transport helper. No database migration, account change, email, financial approval, refund or configuration change was performed.

## Source and artifact
- Application source commit: `9d0cd6d62d41db00a8de4f737a0792ad9fbc9cfa` on `codex/owner-round1-completion`, based on `ca6dc90dc577e5d16bb8b5c3028aecb16cc48958`.
- Package: `publish-net10-owner-round1-completion-20261111`; .NET 10 framework-dependent `win-x64`; 596 package files.
- Package manifest SHA-256: `5150F53BDE4F2ECBB86698F8E69C80AEB678BD4B10E457176F34E6D4544302BE`.
- Eight-file transfer allowlist SHA-256: `3861C9086B705D3EC8EA5E70A2C518DAD4CB9BE3EE0FF8846FAC5F7C1C6DC30D`.
- Live DLL before: `0E3A31CF48FEB1BCFEBE2FC9E523BD162AB156308DA4F83D9100775B4C60DB62`.
- Live DLL after: `AB43A9CC1226156C6A8360406576C84ADC73D59362F02BEE9B9083D9922051F5`; verified by remote full-byte readback.

The complete live recovery copy revealed the actual flattened win-x64 deployment layout. The locally published portable alternative was not transferred. The already-built coherent win-x64 artifact matched 582 unchanged deployed dependencies/assets; all 590 required application-owned paths were present and only the eight intended paths differed. A missing native PDB is an unused debug symbol, excluded from transfer. No compiled files were manually combined and no prior UI artifact was deployed.

## Current coordinated recovery checkpoint
Protected non-public location: `C:\Users\EJ Ridge\AriesRecovery\owner-round1-20261111`.

- Application/configuration/accessibly mapped private files: **675 files / 123,273,526 bytes**. Recursive live inventory before and after download matched; complete relative-path/size/SHA-256 inventory recorded. Final reconciliation under maintenance found zero changes.
- Included `App_Data`: 40 files / 9,691,933 bytes; `wwwroot/uploads`: 3 files / 2,953,461 bytes. Existing files were preserved, never transferred over.
- Initial recovery FTPS connection ended with EOF after 200 files; resumed from completed copies. Final expected file count and size, local hashes and source inventory all passed. No failed recovery item remains.
- Hosting SQL backup job: Success, observed Nov 11, 2026, 12:09 PM. Exact provider-generated path `/db/db_ad00ad_ariesmagicdb_11_10_2026_20.bak` (the filename date follows the provider's naming clock).
- SQL backup: 9,293,824 bytes; SHA-256 `B9B3D688BE7EBCC9688B34F512975811E86F787DABE821AEF508086D5DDBBBFB`. Header matched `db_ad00ad_ariesmagicdb`. `RESTORE VERIFYONLY`, restoration to a new GUID-named disposable database on the existing local SQL Server 2022 instance and `DBCC CHECKDB` passed. Isolated copy showed 17 migrations and one Owner; no production database restore or migration occurred. Disposable restored database was removed; backup retained.
- Original eight target files have separate hash-validated rollback copies and `originals.json`.
- Runtime launcher, deps/runtimeconfig, live web.config and all existing appsettings files were hashed before and after and remained identical. Hosting environment variables were not modified or exported.
- External feedback storage and provider-selected Data Protection material are **not** claimed backed up. They were untouched; earlier accepted historical synthetic-media/session disaster-recovery limitations remain. This is a verified application-file rollback checkpoint, not a certification of a portable whole-host/key disaster restore.

## Transfer actually performed
Maintenance used a release-owned `app_offline.htm`, verified with HTTP 503 to pause writes. Fresh SQL and final application checkpoint completed before replacement. Static assets were transferred first, then the static-assets manifest and DLL last. Only this owned maintenance marker was removed after complete readback verification. Existing maintenance archives and unrelated server files were retained. No delete synchronization, pool/identity change or settings overwrite.

**8/8 files verified; 5,665,292 transferred application bytes.** FTPS upload acknowledgements were slow; every file passed server readback length and SHA-256 checks.

| Relative path | Bytes | SHA-256 |
|---|---:|---|
| `AriesMagicAppointmentSystem.dll` | 4497920 | `AB43A9CC1226156C6A8360406576C84ADC73D59362F02BEE9B9083D9922051F5` |
| `AriesMagicAppointmentSystem.staticwebassets.endpoints.json` | 1148611 | `D621FC814B6CFB7346EBFDE4BF42D321CB14631EC76ECFEF083871EF4FC656BE` |
| `wwwroot/css/owner-round1-20261109.css` | 9966 | `95EAE26456B4E212092C22154946DD66D17F621B5CEACFFC413618A3FC68865C` |
| `wwwroot/css/owner-round1-20261109.css.br` | 2115 | `76839BE76E87CFE2330D7799BB21A053B9E40EAF4D7A0BA390DBB3F8B4DDD7B2` |
| `wwwroot/css/owner-round1-20261109.css.gz` | 2547 | `C688769332E397D30EECCD85B024DE604FB08388B2A4AD505E14046D05A7DCD7` |
| `wwwroot/js/owner-round1-20261109.js` | 2387 | `7C40F203E4A6385FDAA1D0598F61FB6657DB2EDF63EF7256976D1A327EE3CFEF` |
| `wwwroot/js/owner-round1-20261109.js.br` | 782 | `A7296E813055B8FB33752D6EC292F41FF9DBF67EB73A5E6CECD6D42DF457E7C0` |
| `wwwroot/js/owner-round1-20261109.js.gz` | 964 | `410DA371CC7CE843C0F490BDB0D4D132925DFB4A66DD28D04D0EC1CF0431D977` |

## Verification actually executed
- Local evidence retained from implementation: Release build zero warnings/errors; full 510-check SQL/controller/HTTP/security suite passed; 52/52 rendered Owner page/viewport cases passed, zero axe violations and zero document overflow. These are local synthetic tests, not a new production transaction suite.
- Post-startup: homepage and `/health` HTTP 200; maintenance removed. All eight uploaded files re-read and verified. Preserved config/runtime hashes remained unchanged.
- 22 public checks passed: Homepage, Experiences, About, Gallery, Login, Registration, health; anonymous Owner/Personnel/payment proof/Staff/Admin restrictions; sensitive configuration/private directory HEAD denial including alternate casing/encoding; exact served CSS/JS content hashes.
- **13 authenticated Owner pages verified** using the existing Owner session after application restart: Dashboard, Personnel, payment queue, payment detail, refunds, Reports, Notifications, Messages, Calendar, History, history detail, Profile, Booking Details.
- Actual dashboard mouse and keyboard tab changes worked. New generic OCR conflict wording, compact blue-CTA Personnel layout and shared Notifications component were observed live.
- Authorized private payment-proof preview loaded an actual existing image. Anonymous proof access was denied. No payment verification/refund/account action was submitted.
- Owner session continuity across this actual restart was observed. Fresh password login, new invitation delivery/activation, password or Owner-email changes and new financial writes were not performed during this UI release smoke test. No claim that those actions were newly executed live.
- Browser left on the updated authenticated Owner Dashboard for manual testing.

## Rollback and retained limitations
Rollback was **not required**. Reviewed `deploy.py rollback` in the protected checkpoint checks this release's live DLL, original target hashes and unchanged protected configuration, installs its own maintenance marker, restores only the eight prior application files, removes only its marker and verifies health. It never restores SQL, resets keys or deletes uploads. Use only under operator incident authorization and after checking no subsequent deployment/configuration change. The original package-path pointer and approved manifests remain in `.tmp-owner-build/owner-round1-completion`; do not substitute another package.

Existing H4/PFX mode, Feedback-Off, SMTP/Brevo settings, accounts, role authorization, Phase 8/Client UI, booking/payment/policy logic and private mappings were preserved. Actual provider key repository/decryption recovery dependencies and historical external media recoverability remain unverified accepted limitations. Large-queue server pagination and an authoritative invitation-issued date remain documented follow-ups; no invented data or unrelated policy changes were added.

Evidence: protected `checkpoint.json`, `application-sha256.json`, `sql-download.json`, `sql-checkpoint.json`, `baseline-comparison.json`, `deployment.json`, `public-smoke.json`, `authenticated-smoke.json`; local UI/test evidence `tmp/pdfs/owner-round1/`. No credentials, config contents, keys or client images are included in this report.

Final verified UTC: `2026-11-11T04:28:57.969250+00:00`.
