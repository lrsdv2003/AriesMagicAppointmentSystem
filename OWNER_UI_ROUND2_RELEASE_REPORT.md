# Owner UI Round 2 - release review, 15 November 2026

Implementation and local validation are complete. **Deployment is not authorized and has not been performed.** No production accounts, database records, invitations, configuration, private files or hosting settings were changed.

Worktree: `.worktrees/cancellation-payment-feedback-policy`, branch `codex/aries-final-ui-20261115`, starting commit `c1d027cf63130abcee39fe3824422766da153523`. The operator explicitly selected this newer three-role baseline. The outer workspace's older four-role branch and pre-existing untracked operator scripts/reports were preserved. Changes remain reviewable in this worktree; no commit, merge or push was performed.

The supplied Round 2 PDF contains four pages and an internal Round 1 title. All four rendered pages were inspected. The pasted user request governs the work: old Admin controls, sample people, sample transactions and old financial assumptions in the screenshots were not adopted. The [pre-edit requirement inventory](docs/OWNER_UI_ROUND2_AUDIT.md) records implemented/partial/missing status for every requirement.

## Implemented refinements

| Area | Result |
|---|---|
| Calendar Settings | Owner-only button in the existing Calendar toolbar, return link to Calendar, and correct Calendar active-navigation state. Removed the separate Settings sidebar entry. Reused the same authorized Settings controller/view and data. Capacity, blocking conflicts and availability writes are unchanged. |
| Payment Verification | SQL pagination (10/25/50), stable timestamp/ID ordering, full displayed booking-code search, current-page Refresh and filter-preserving first/previous/next/last links. Removed the silent 250-record cap. Counts cover all matching records. Latest OCR filter/indicator ordering agrees when processing times tie. |
| Refund Requests | Same paging/refresh controls, full booking-code and completion-reference search, readable review indicators, accessible complete reasons, and actual approved amounts where recorded. Removed the old estimate labelled Eligible because it ignored other reserved refunds. No eligibility or payout rule changed. Missing supporting proof is identified without inventing a receipt. |
| Financial Summary | Four financial cards, three primary financial charts, compact filter/export/print toolbar, retained net collection context and recent booking financial records. Existing booking charts, operational metrics and accessible chart-data tables remain in expandable sections. Original data sources and exports are preserved. |
| Personnel | Compact desktop filters, existing two-section invitation/account layout, actual invitation date from the initial issuance audit event, UTC labelling and Not recorded for older accounts lacking evidence. Staff-only invitations and all existing authorized account actions remain. |
| Dashboard | Retained financial requests and the three authoritative financial metrics; added refund submission date. No duplicate cards or financial action shortcuts were introduced. |
| History | Listings, details, counts and exports now use Completed status only. An unsuccessful-status query cannot mix Archives into History. Old authorized unsuccessful History detail URLs redirect Owner to Archives and Staff to existing operational Booking Details. |
| Archives | Cancelled/Declined/Expired records remain accessible without requiring an archive timestamp. Display actual final status, avoid an invented invalid-booking reason when metadata is missing, support full booking-code search and clamp paging. Detail pages link to existing Booking Details/transactions and related refund requests. |
| Responsive access | Owner account menu provides a Messages link below 768px, where existing shared CSS hides the header message icon. Desktop messaging, unread queries, profile and notification behavior remain. Added Owner-scoped spacing, readable action/status text, yellow booking IDs, keyboard-scrollable financial tables and accessible pagination controls. |

## Existing requirements retained

The newer baseline already supplied the navy Client-inspired header/branding, profile dropdown, notification indicator, private messaging, shared Client notification rows, internal profile editing/photo/password controls, operational calendar/package filters, History presentation, and Owner Booking Details with the actual ledger, claims, references, OCR findings, proof revisions and refund history. These were inspected and preserved instead of recreated.

Financial requests, protected proof endpoints, advisory OCR, manual Owner decisions, anti-forgery validation, payment/refund audit and transaction behavior, report date selection/CSV/print, secure single-use invitation enrollment and the retired-Admin enforcement were already implemented. No payment verification, refund approval, cancellation, scheduling, personnel provisioning or security policy was rewritten for presentation.

The account administration consolidated into Personnel remains available, including read-only Client/Owner/historical account rows. The screenshot's old Admin invitation/account-management options are not supported. Missing legacy invitation dates are deliberately not inferred from account creation, token expiry or later management events.

## Financial and lifecycle verification

`PaymentFinancialService` is unchanged and remains authoritative:

| Quantity | Source / rule retained |
|---|---|
| Booking value | Existing FinalPrice/BasePrice fallback; dashboard/reports exclude Cancelled, Declined and Expired from eligible value. |
| Collected revenue | Sum of Verified payments only. Pending, rejected and additional-evidence proofs contribute zero. |
| Completed refunds | Refunded records only, using ApprovedAmount when present, otherwise Amount. Pending/approved/processing refunds are not completed payouts. |
| Net collected | Verified collections less completed refunds, using the existing nonnegative booking-level calculation. |
| Remaining balance | max(0, booking value - verified payments); a refund does not reopen an already credited payment obligation. |
| Collectible outstanding | Remaining balances of eligible bookings; unsuccessful bookings do not become outstanding receivables. Their actual receipts/refunds remain in financial reporting. |

Reports preserve the existing **event-date booking cohort**, with collection-date trend points for that cohort. This is not a newly introduced cash-date accounting policy. Baseline reconciliation and the executed tests confirm dashboard/report agreement, retained cancelled-booking collections, completed-refund subtraction, chart totals and formula-safe CSV behavior.

Executed edge cases include unpaid/unverified/rejected/additional-evidence proofs, partial and full payment, pending/approved/completed refund, approved refund amount differing from requested amount, cancelled and expired bookings, rejected additional proof, and verified future events. Two queues with 263 synthetic records prove that records after row 250 remain reachable; page sizes, negative/oversized pages, empty ranges, stable order and combined filters are checked against actual SQL queries.

History and Archives are mutually exclusive by recorded lifecycle status. Classification does not delete bookings or rewrite terminal/pending states. A rejected payment proof does not archive an active booking. Pending and completed refunds on cancelled/expired bookings remain reachable in the Owner refund queue and review endpoint.

The existing lifecycle service explicitly auto-completes Confirmed events after their **end time**, records the completion timeline event, locks completed details and preserves outstanding balance notifications. This established rule was retained unchanged. Payment verification by itself does not complete a future event.

## Executed checks and scope

Evidence is under `.tmp-owner-build/owner-round2/`. Counts below are separate suites and overlap in coverage.

| Check | Result | Evidence |
|---|---|---|
| Before-edit SQL/controller/security/Razor baseline | 531 passed | `baseline-regression.log` |
| Final Owner SQL/controller/security/financial/Razor suite | **572 passed**, 111 current fixtures | `owner-regression.log` |
| Real Identity-cookie/MVC/SQL workflow integration | **277 passed** on .NET 10.0.13 | `integration.log` |
| Independent findings regression, including concurrency/rollback | **112 passed** | final summary in `integration.log` |
| Personnel provisioning, expiry/replay, retirement and real-cookie boundaries | **117 passed** | `personnel.log` |
| Cross-role rendered DOM, labels, dialogs, CSRF and interactions | **934 passed** | `dom.log` |
| Final responsive/browser/keyboard/automated accessibility | **105/105 cases**, zero document overflow, zero detected WCAG A/AA violations, zero JavaScript exceptions | `browser.log`, `after/results.json` |
| Release builds / publish | Passed, zero warnings/errors in recorded Release builds | `build.log`, `integration-build.log`, `packaging-build.log`, `publish.log` |
| Deployment safety policy | **57 passed**, no live connection | `deployment-policy.log` |
| Final packaged assembly/default-state inspection | **143 passed**, 128 compiled Razor items | `packaged-assembly.log`, `release/packaged-assembly.json` |
| Final publish/private-artifact policy | Passed | `release-policy.log` |
| Exact final package HTTP/HTTPS smoke in disposable copy | Passed; private/config probes denied, HTTPS/security headers/redirect checked | `package-smoke.log` |
| Protected source/schema/config preservation | 15 protected paths unchanged; no entity/schema/configuration changes | `release/preservation-checks.json` |
| Package and ZIP integrity | All 600 application-owned entries re-read and hash-verified; runtime source manifest rechecked | `release/application-owned.json`, `release/delivery-summary.json` |
| Diff whitespace check | Passed | `git diff --check` |

SQL suites select only generated disposable databases on the explicitly selected local SQL instance. HTTP integration uses real Identity cookies, native OCR and loopback SMTP/map fixtures; no real recipients or funds are involved. Deliberate audit/SMTP/SQL failure injection produces expected exception logs; the suite's final persisted-state assertions and SUCCESS lines determine acceptance.

The isolated package smoke intentionally uses an unavailable loopback SQL endpoint and disables startup SQL probing/jobs in that child process only. `/health` returning 503 there is expected. Production defaults were not changed. Login/cookie creation is covered by the separate real Identity/SQL suite, not claimed from the no-database package smoke.

Authorization verification covers Owner-only financial queues/decisions, Settings and Archives; Staff operational access and denial of Owner administration/financial actions; Client own-versus-other booking/proof boundaries; invitation role tampering; replay/expiration; stale legacy cookies; and retained private conversation membership. Public registration stays Client-only. Exactly **Owner, Staff, Client** remain active. Historical Admin records remain audit history and do not confer access.

## Visual evidence

The four-page reference informed the preserved navy shell, financial request panel/metric hierarchy, compact searchable tables, four-indicator financial summary and two-section Personnel screen. Screenshot data is synthetic and never represents production transactions. The old Admin controls in the reference were excluded.

| Screen | Before | Final |
|---|---|---|
| Owner dashboard, desktop | [Before](.tmp-owner-build/owner-round2/before/Dashboard-Owner-1440.png) | [Final](.tmp-owner-build/owner-round2/after/Dashboard-Owner-1440.png) |
| Payment verification, desktop | [Before](.tmp-owner-build/owner-round2/before/Payments-PendingVerification-1440.png) | [Final](.tmp-owner-build/owner-round2/after/Payments-PendingVerification-1440.png) |
| Refund requests, desktop | [Before](.tmp-owner-build/owner-round2/before/Payments-RefundRequests-1440.png) | [Final](.tmp-owner-build/owner-round2/after/Payments-RefundRequests-1440.png) |
| Financial Summary, desktop | [Before](.tmp-owner-build/owner-round2/before/Reports-Index-1440.png) | [Final](.tmp-owner-build/owner-round2/after/Reports-Index-1440.png) |
| Personnel, desktop | [Before](.tmp-owner-build/owner-round2/before/Personnel-Index-1440.png) | [Final](.tmp-owner-build/owner-round2/after/Personnel-Index-1440.png) |
| Calendar, desktop | [Before](.tmp-owner-build/owner-round2/before/Calendar-StaffIndex-Owner-1440.png) | [Final](.tmp-owner-build/owner-round2/after/Calendar-StaffIndex-Owner-1440.png) |
| History, mobile | [Before](.tmp-owner-build/owner-round2/before/History-Index-390.png) | [Final](.tmp-owner-build/owner-round2/after/History-Index-390.png) |

Final screenshots also cover Owner profile, notifications, messaging, payment/refund review, Booking Details, Settings, Archives and 25-row/empty queue states at 1440/1024/768/390/320px. `-complete.png` captures expand the viewport height to document content at the same width; normal screenshots retain the application's scroll behavior. Charts are settled before capture. Manual visual review included desktop dashboard/payment/refund/report/Personnel/Calendar/Archive Details, mobile Personnel/History/payment queue and Owner financial details.

## Source and production baseline

The source is based on `c1d027c` plus the reviewable changes below. The frozen runtime input manifest contains 527 files and has fingerprint:

`7AAD6153AA979332D373F5D780C4E8366DB291376E819EEC1F796D343EA4B13F`

The final DLL identifies itself as `1.0.0-owner-round2.20261115+source.7aad6153aa979332`. This distinguishes uncommitted reviewed inputs from the baseline Git commit.

Read-only public checks on 15 November returned HTTP 200 for hosted `/health`; hosted `aries-final-ui.css`, `aries-booking-poll.js` and `owner-round1-20261109.css` matched the unchanged local bytes. Evidence: `production-readonly.json`. These checks verify public health/static baseline only. **The current hosted DLL identity and private configuration were not inspected or claimed verified.** Historical deployment reports are retained as historical evidence, not substituted for a fresh binary checkpoint.

## Deployment package and rollback

Final coherent local publish: `publish-net10-alpha-beta-owner-round2-20261115-final`, framework-dependent .NET 10 / win-x64 / IIS OutOfProcess. It contains 603 files and passed final package checks. Earlier intermediate review directories/archives are superseded; only the final artifact named here is the delivery candidate.

Delivery: [application-files archive](.tmp-owner-build/owner-round2/release/aries-owner-round2-final-application-files.zip), **600 application-owned files**, 48,368,313 bytes. This archive excludes `appsettings.json`, `appsettings.Production.json`, `web.config`, launcher executables, private uploads/proofs/profile photos, account data, keys/certificates, backups and test tooling. The full local publish contains three secret-free hosting/config templates for inspection; do not overwrite live configuration with those templates.

| Artifact | SHA-256 |
|---|---|
| Final application-files ZIP | `B262A00949D385C63052478CFFDA12BB92B74B1257C6A3161362B10E788545E4` |
| Final application DLL | `EC24AAAB79037BA6168B9C57235FFB4902E71B7E0479FFA631A38F5789A97A02` |
| Application-owned manifest | `3E9180F56B84B17C91284D1624ED454EA99BD83BDDD45F96787001E7C1A8DD9C` |

Adjacent `source-manifest.json`, `full-package.json`, `application-owned.json`, `delivery-summary.json` and `packaged-assembly.json` provide exact source/package identity. Every archive entry was reopened and checked against the manifest. No deployment script or production mutation was run.

**No database migration or configuration change is required.** Before any future approved deployment, capture a fresh exact live application-owned inventory/DLL hash and coordinated application/config/private-data/SQL recovery checkpoint; reconcile the approved delta against this candidate; confirm hosting compatibility; preserve existing environment settings, launcher, Data Protection material, private file roots and production account data. Release-specific authorization is still required. The ZIP is not an asserted exact delta against an uninspected live DLL.

Rollback must restore the coordinated prior application DLL, static asset endpoints and associated changed assets from that checkpoint, preserve configuration and private storage, restart through the established hosting workflow, and recheck login/role boundaries, proofs, financial screens and Calendar. There is no schema rollback for this release. Do not overwrite newer production transactions with an old SQL backup merely to roll back this UI; database restoration would require a separately authorized recovery decision. For local source rollback, revert only the reviewed listed changes, preserving the user's unrelated files.

## Remaining gates and risks

- No implementation clarification remains. Unknown historical invitation/reason dates remain Not recorded rather than fabricated.
- Production binary/configuration continuity and the exact transfer delta need a fresh controlled checkpoint before deployment; public asset matches are not proof of the full deployed application.
- Authenticated production UAT, actual provider email delivery and target-host OCR/storage continuity are not established by loopback fixtures. No real financial decisions or personnel enrollment were performed in production.
- Automated axe/keyboard/responsive checks do not replace a full manual assistive-technology audit. Large production-scale query/load behavior was not benchmarked; functional paging was exercised beyond the former cap.
- Existing automatic completion-after-end-time and event-date report cohort policies were preserved. Changing either would be a separate business-policy request.

## Files changed

The generated inventory appended below lists this task's changes only. Pre-existing untracked operator/deployment files are excluded.

Total: 33 files (25 modified, 8 new).

- `Controllers/HistoryController.cs`
- `Controllers/PaymentController.cs`
- `Controllers/PersonnelController.cs`
- `OWNER_UI_ROUND2_RELEASE_REPORT.md`
- `Services/HistoryService.cs`
- `Services/TrashHistoryService.cs`
- `ViewModels/QueuePaginationViewModel.cs`
- `ViewModels/TrashHistoryViewModels.cs`
- `Views/Calendar/Settings.cshtml`
- `Views/Calendar/StaffIndex.cshtml`
- `Views/Dashboard/Owner.cshtml`
- `Views/History/Index.cshtml`
- `Views/Payments/PendingVerification.cshtml`
- `Views/Payments/RefundRequests.cshtml`
- `Views/Personnel/Index.cshtml`
- `Views/Reports/Index.cshtml`
- `Views/Shared/_DashboardLayout.cshtml`
- `Views/Shared/_HistoryFilters.cshtml`
- `Views/Shared/_InternalAccountMenu.cshtml`
- `Views/Shared/_QueuePagination.cshtml`
- `Views/TrashHistory/Details.cshtml`
- `Views/TrashHistory/Index.cshtml`
- `docs/OWNER_UI_ROUND2_AUDIT.md`
- `tests/OwnerRegression/HistoryRegression.cs`
- `tests/OwnerRegression/OwnerRound2Regression.cs`
- `tests/OwnerRegression/OwnerTransferRegression.cs`
- `tests/OwnerRegression/Program.cs`
- `tests/OwnerRegression/README.md`
- `tests/OwnerRegression/SnapshotRenderer.cs`
- `tests/OwnerRegression/owner-round2.browser.mjs`
- `tests/ReleaseIntegration/OwnerUiRound2HttpChecks.cs`
- `tests/ReleaseIntegration/Program.cs`
- `wwwroot/css/owner-round2.css`
