# Owner Round 2 inspection - 15 November 2026

The operator selected the newer worktree and branch `codex/aries-final-ui-20261115`, baseline `c1d027c`. The outer workspace's older four-role branch is not the implementation target. Existing untracked operator files are preserved. The supplied four-page Round 2 PDF has an internal Round 1 title; its screenshots are references, not instructions to restore retired roles, sample data, or old business rules.

Inspected before application edits: Owner/Client shells, dashboards, notification and messaging partials, profiles, operational Calendar and Settings, payment/refund controllers and proof endpoints, authoritative financial service and reports, Personnel provisioning/enrollment, History and Archives, Booking Details, role definitions and existing regression tooling. Baseline isolated SQL/controller/security suite: 531 checks passed, including rendered Razor fixtures (`.tmp-owner-build/owner-round2/baseline-regression.log`).

| Request / PDF location | Baseline status | Required action |
|---|---|---|
| Client-style header, notification/message shortcuts and profile menu (p1) | Implemented | Retain newer shared navy shell, unread counts and account-scoped access. |
| Notifications and messaging (p1) | Implemented | Retain shared Client notification/conversation components and participant authorization. |
| Profile and photo editing (p1) | Implemented | Retain internal profile security fields and photo management. |
| Calendar presentation (p1) | Implemented | Retain operational calendar, package filters and responsive layout. |
| Calendar Settings relocation (p1) | Missing | Add Owner-only Calendar toolbar link and return link, then remove separate sidebar item; reuse existing Settings action and data. |
| History presentation and Booking Details with transactions (p1) | Implemented | Retain shared presentation and protected actual ledger/OCR/refund records. |
| Dashboard Financial Requests and three financial cards (p2) | Implemented / partial | Retain layout and calculations; add refund submission date. |
| Payment verification compact table, supported OCR indicators and secure proof (p2) | Implemented | Preserve decision/eligibility/CSRF/financial logic. |
| Payment search, filters, refresh and pagination (p2) | Partial | Replace silent 250-row cap with bounded SQL paging; support displayed booking-code search, manual refresh and filter-preserving links. |
| Refund table, summary cards, proof and Owner decisions (p3) | Implemented / partial | Keep policies; remove misleading eligibility estimate that ignores other reserved refunds. Show approved amount separately where recorded. |
| Refund search, filters, refresh and pagination (p3) | Partial | Add bounded SQL paging, full code/reference search and refresh; keep archived-booking requests accessible. |
| Financial indicators, charts, records, date selection and export (p3) | Implemented / partial | Preserve authoritative calculations and event-date cohort; emphasize four financial indicators/three financial charts and retain operational details in expandable sections. |
| Personnel invitation + authorized accounts (p4) | Implemented / partial | Preserve Staff-only enrollment and account management; show invitation date only from recorded issuance audit events, otherwise Not recorded. |
| Three active roles | Implemented | Owner, Staff, Client only; preserve historical Admin audit metadata and denial of retired-role access. |
| History completed only / unsuccessful Archives (p4) | Partial | History queries, totals, filters and exports still include unsuccessful states. Restrict History to Completed; retain unsuccessful Archives and old authorized detail links. No status rewrite or deletion for classification. |
| Navy / blue / yellow IDs, responsive/accessibility | Implemented | Preserve newer visual system; verify changed layouts at 320/390/768/1024/1440px. |
| Regression and release preparation | Existing tooling | Add focused meaningful paging/classification tests; execute regression, HTTP/Identity, browser, accessibility, build and package checks. Prepare local package and rollback guidance; no deployment. |

Existing lifecycle rule explicitly auto-completes Confirmed events after their end time in `HistoryService.ArchiveDueBookingsAsync`, writes a completion timeline event and preserves balance due. This existing rule is not introduced or expanded by classification changes. Payment verification alone does not complete an event. Financial definitions remain: Verified receipts only; completed (`Refunded`) refunds only; remaining balance = max(0, booking value - verified receipts); unsuccessful bookings excluded from collectible receivables while their receipts/refunds stay in financial records.

No production account/database changes, invitation sends to real recipients, hosting changes, or deployment are authorized by this request. Existing local release evidence is a historical production baseline, not proof of current hosted source identity.
