# Phase 7 â€” Concierge Client Hub specification

Authority: 27 October 2026 Phase 7 attachment. This phase accepts the Concierge visual direction; full manual Phase 6 sign-off remains open. No production operation or internal-role redesign.

## Verified checkpoint

540 nonignored source files copied and SHA-256 verified before edits. Exact path and manifest: `.tmp-owner-build/phase7-checkpoint.json`; exclude user index.html/package-lock.json and ignored secrets/uploads/builds. Existing running manual preview on 5295 is left intact while a separate generated fixture is used for Phase 7 QA.

## Audit / before navigation

Marketing header (Home, Experiences, About, Gallery) + account dropdown (dashboard/bookings/payments/messages/feedback/profile/notifications/logout) + eight-link account strip + promotional footer. Concierge transaction pages already have their own focused header. Overview is one navy upcoming-request card and five shortcut cards; no financial snapshot or actual recent activity. Pending requests are called Upcoming event even before confirmation. Profile repeats identity/photo and four KPI/completeness tiles and edits through dialogs. Notifications use existing safe Open links and POST MarkAllRead; Messages has distinct participant-authorized conversations, polling, support and booking chats. Updates is the existing Notifications route, not an independent activity service. Feedback index/create/details preserve eligibility, private suggestions/media and separate publication consent. Refund routes are existing secondary payment features.

## New navigation / shell

A new Client-only layout, separate from public/authentication and internal layouts. Compact navy branded rail on desktop; six primary links: Overview / My bookings / Payments / Messages / Notifications / Profile. Secondary context: Reschedule requests, My feedback, Refund requests; public website and POST logout in the rail footer. Mobile uses an accessible modal navigation drawer with Escape/focus return/background inertness, not horizontal link rows. No floating chat widget. Existing URLs and role gates retained. Phase 6 Create/Upload/Reschedule Create source and focused shell remain unchanged; dashboard links connect to them.

## Screen composition

- Overview: concise first-name greeting; an actual actionable booking first (otherwise next booking) + status/next action; schedule/venue/reference; authoritative financial snapshot; other bookings as compact rows; three recent owned notifications, with safe original Open destinations; meaningful new/past-only states. No invented timeline, KPI wall or guaranteed confirmation.
- My bookings: search + existing active/past grouping, compact responsive rows with real verified/total/balance, status text and contextual next action. No wide table. Group can be initialized by validated UI query parameter; same controller contract.
- Details: existing compact Concierge schedule/financial layout retained within hub, contextual reschedule/history/refund/contact/feedback links and disclosure. No raw internal timeline Notes exposed.
- Payment activity: existing private receipt/verification records in hub; finance/downpayment distinction and actual next step. Upload remains Phase 6 focused flow. Refund pages remain reachable with original forms/authorization.
- Notifications: actual unread/read counts, compact grouped recent/earlier sections; same safe notification row, Open behavior and MarkAllRead POST. No new notification rules.
- Messages: dedicated two-pane inbox/thread on desktop, list-to-thread mobile with explicit Back; compact heading; existing support/search/filter/send/poll IDs/routes and draft/scroll behavior. Client-specific partial/markup prevents internal-role changes. Connection/retry/live announcements retained.
- Profile: identity/avatar once; inline personal-details task, separate photo/security disclosures. Same Edit/UploadPhoto/RemovePhoto/ChangePassword contracts and existing file resolver. Correct password guidance from configured Identity options; no policy change. Save/error announcements, accessible modal focus for retained optional dialogs.
- Feedback: eligible-event entry + concise sent-history rows; clear no-eligible/no-submitted states; same three required rating groups, optional text/suggestions/media and separate sharing consent. Optional photos/sharing disclose to reduce initial density; essential ratings and submission stay visible. No publication or eligibility change.

## Data boundaries

Keep existing controllers/services/models/configuration/migrations unchanged. Read-only view/component projections may add owned notifications/profile/pending-reschedule metadata, constrained by authenticated UID. Existing financial service and ClientBookingPresentation determine values/labels; presentation actions repeat existing eligibility gates and remain server-authorized. No raw internal notes or system-wide activity. No new business status, endpoint, payment calculation or storage root.

## QA acceptance

Before/after actual screenshots. Desktop/mobile pairs for Overview, Bookings, Details, Payments, Notifications, Messages, Profile, Feedback; also form/empty/pending/evidence/completed/reschedule/closed states. Render 320/375/390/430/768/1024/1440. Six passes: navigation/IA; task hierarchy; spacing/type; mobile/scroll; keyboard/axe; full regression/security. New SQL fixture only, including another empty Client and single-status users; actual HTTP mutations only generated records. Build Debug/Release into separate QA outputs while the existing manual preview runs. Preserve old screenshots, approved CSS/views, frozen packages and source hashes. Document pre-existing Homepage quotation contrast and official inclusion-data gap. Stop after Phase 7 review; no deployment/package.
