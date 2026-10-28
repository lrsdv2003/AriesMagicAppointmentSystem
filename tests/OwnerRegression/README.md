# Owner regression checks

## Wonder Studio client design

After rendering fixtures, `node tests/OwnerRegression/wonder.browser.mjs` checks 54 Client/edge fixtures at 320/390/768/1280/1920px, fonts/media/brand tokens, JavaScript errors, and isolation of all three internal roles. This includes authentication validation/recovery/locked states, Gallery guest/client and About empty/published states. Set `AXE_CORE_PATH` to check WCAG A/AA rules at both 320px and 1280px; `WONDER_AXE_PAGES` accepts a comma-separated fixture-name selection. Use the existing `PUPPETEER_MODULE` and `PUPPETEER_EXECUTABLE_PATH` environment overrides when needed. Screenshots and findings are written to `%TEMP%/AriesOwnerSnapshots`. See `CLIENT_UI_REDESIGN_REPORT.md` Section 16 for current results and verification limits; the older `WONDER_STUDIO.md` compositions are historical.

## Pre-deployment security additions (22 October 2026)

The harness also runs `SecurityRegression` and `SecurityHttpRegression`: matching image signatures,
cumulative refund reservations, repeated financial actions, overlapping schedules, simultaneous SQL
confirmation attempts for the last daily slot, and real MVC HTTP authorization/antiforgery responses
for all four roles. Header-based authentication is registered only in the isolated test host, never
in the application. The suite with `--render` now reports 289 checks and 68 fixture pages.

`security.http.mjs` separately exercises the real application pipeline. Run it only against a local
probe at `http://127.0.0.1:5189` configured with Development environment,
`BackgroundJobs__Enabled=false`, `Database__InitializeOnStartup=false`, `Database__ValidateOnStartup=false`, and an unreachable test SQL
connection. It deliberately expects unhealthy database readiness and exhausts the account rate limit.
It does not test successful authentication or external providers. See `AUDIT_REPORT.md` for results
and remaining acceptance requirements.

    node tests/OwnerRegression/security.http.mjs

Run from the repository root on Windows with .NET 10 and SQL Server LocalDB:

    dotnet run --project tests/OwnerRegression/OwnerRegression.csproj

To also render role-specific fixture pages using synthetic data:

    dotnet run --project tests/OwnerRegression/OwnerRegression.csproj -- --render

The harness creates a uniquely named temporary LocalDB database and deletes it in finally.
It does not read the application connection string, send notifications, or change application data.
Rendered HTML is written under the OS temporary directory in AriesOwnerSnapshots.

Coverage includes Owner-only financial action attributes, anti-forgery requirements,
OCR comparison edge cases, pending/approved financial states, report filters and exports,
cancelled-booking collections/refunds, rejection reasons, latest OCR filtering, and Razor rendering.

Report date/month/year filters select bookings by event date. Financial summaries include
all verified receipts and completed refunds for those bookings; trend points use collection dates.
Cancelled, declined, and expired bookings do not contribute outstanding receivables.

Browser validation uses rendered fixtures. It does not exercise real payment processing,
external email delivery, or live client messaging.

## Shared filter regression checks

The render option now includes Client, Staff, Admin and Owner interface fixtures (68 pages total).
Run the browser checks from the repository root with Puppeteer and Chromium installed:

    node tests/OwnerRegression/filters.browser.mjs

If Puppeteer is installed outside this repository, set PUPPETEER_MODULE to its module URL.
The browser test intercepts all requests and serves only synthetic HTML and local static assets.
It checks 13 filter screens at desktop/mobile sizes, labels, touch targets, combined GET
serialization, reset links, and Client combined search/group filters and empty states. It does not
claim to exercise live authentication or every controller query against production data.

## Package, calendar and notification checks

The regression suite also checks package saving/validation, archived-state preservation,
notification ownership, read timestamps, and the anti-forgery requirement for marking all read.

After rendering fixtures, run:

    node tests/OwnerRegression/interface.browser.mjs

Use PUPPETEER_MODULE as above. Cache the same FullCalendar 6.1.15 script used by the app at
%TEMP%/aries-fullcalendar.js from https://cdn.jsdelivr.net/npm/fullcalendar@6.1.15/index.global.min.js.
All browser requests are intercepted; no live application data is changed.
This checks the actual FullCalendar rendering at 1440, 1024, 768 and 390 pixels, date/marker
separation, Staff management restrictions, inclusion add/remove submission, summary preview,
blocked-day keyboard access and notification labels.


## Admin console checks

The same isolated regression harness exercises authorization policies for all four roles,
financial proof access, fixed capacity at confirmation and rescheduling, legacy-limit bypass
prevention, Staff account target restrictions, and audit role snapshots.
No application database migration is required. Legacy configurable limits remain stored but
are ignored; the fixed rule is three confirmed bookings per day.

After rendering fixtures, run with Puppeteer as above:

    node tests/OwnerRegression/admin.browser.mjs

This checks nine Admin pages at 1440, 1024, 768 and 390 pixels, labels, navigation cleanup,
sticky header, keyboard modal dismissal, blocked-date confirmation/error recovery, and shared
messaging layout. Financial services, email and live user accounts are not exercised by
the browser fixtures. Booking archives remain read-only; package restoration uses the
existing confirmation workflow. Unrecorded historical actors/roles/dates are not inferred.


## Role dashboard checks

The isolated data suite now verifies Staff pending-only previews, chronological schedules,
unread conversation counts, reload freshness, Owner reconciliation to unfiltered Reports
(including receipts/refunds on cancelled bookings), Admin user/archive totals, dashboard
authorization, and isolation of a failed financial-summary widget.

After rendering fixtures, run:

    node tests/OwnerRegression/dashboard.browser.mjs

The browser checks populated, empty and error states for Staff, Owner and Admin at
1440, 1024, 768 and 390 pixels, plus role-appropriate links, single-column mobile metrics,
keyboard focus, sticky headers and the accessible six-month booking-volume chart.
Dashboard financial figures are all-time; the business overview uses event dates in the
current month. Today's schedule counts confirmed events still in progress or upcoming;
the upcoming count starts tomorrow. Admin failed-login attention uses today's recorded
UTC events, not an inferred platform health score.


## Internal account profiles and User Management filters

`ProfileRegression.cs` covers Staff/Admin/Owner authorization, anonymous/Client denial,
anti-forgery requirements, permitted input fields, current-user isolation, profile validation,
Identity password policy, security-stamp rotation and the current-session refresh call,
private photo storage/type/size checks, replacement/removal, and audit records without passwords.
The sign-in refresh is a test double; these checks do not use real accounts or send notifications.
The full suite now passes 216 checks and renders 37 fixture pages.

Run `node tests/OwnerRegression/profile.browser.mjs` after `--render` (with the same
PUPPETEER_MODULE setup as the other browser scripts). It checks 16 layouts across four widths,
labels, CSRF fields, account-menu links, keyboard focus, password show/hide and reset,
photo preview/cancel, personal-information submission, and filter Apply/Clear URLs.
Set SKIP_LAYOUT=1 to rerun interactions only. Browser requests use synthetic pages and local assets.

The internal roles share `/InternalProfile/Index`; the existing Client profile remains separate.
Email, role, verification and account status are read-only. Names retain the existing FullName
field rather than inventing first/last-name data. No schema migration is needed.
User Management preserves its existing filter semantics (including Disabled) and has no pagination.

Internal account photos are written under `App_Data/profiles` outside wwwroot and are served only
for the currently signed-in account through `/InternalProfile/Photo`. Keep this directory writable
and persistent with the application data during deployment/backup; it is excluded from Git.
Existing generated photos under uploads/profiles remain readable until replaced or removed.


## Shared internal UI and role-aware History

The internal layout opts into `internal-ui.css`, which owns shared typography, spacing, colors,
controls, tables, badges, panels and navigation. Existing module CSS retains layout-specific
behavior. The primary font is Segoe UI (Arial/sans-serif fallback), without a web-font request.
The desktop sidebar and top header stay fixed while main content scrolls. Below 992px the
same sidebar uses Bootstrap offcanvas, including keyboard focus management and Escape dismissal.
Client layouts do not opt into this stylesheet.

History now includes Completed, Cancelled, Declined and Expired bookings, with a shared filter
partial and responsive table. Staff sees operational fields and lifecycle events; Owner also
sees payment-review state and shared financial summaries. Details are read-only. Staff-supplied
payment/refund filters and revenue sorting are ignored. Existing date/refund query parameters
are preserved in pagination and exports. CSV, print and PDF include the final booking status.
Full booking codes are searchable, paging is bounded, and completion dates prefer the actual
completion event over the later archival marker. Outstanding-balance notifications go to Owner.

Validation: 236 isolated regression checks; 48 rendered fixtures. Run
`node tests/OwnerRegression/internal-ui.browser.mjs` with PUPPETEER_MODULE configured as above.
It covers 39 internal screens at 1440, 1024, 768 and 390px (156 responsive checks), role-safe
History rendering, read-only details, consistent typography, sidebar contrast, fixed headers,
mobile navigation focus, filters and the shared profile menu. UI_PAGES accepts comma-separated
fixture names for targeted runs; SKIP_LAYOUT=1 runs interactions only. Screenshots are viewport
captures because content scrolls inside the shell. External font/icon requests are blocked in
fixtures; real authenticated account UAT remains separate from these synthetic checks.
The existing interface, Admin and profile browser interaction suites also pass.


## Cross-role UI quality checks

Phase 7 Client hub: the complete regression/rendering suite passes **509** checks, including 17 new presentation-only next-action cases; DOM passes **876**. Existing backend/role/financial/consent checks remain. `ClientHubRegression` verifies payable/blocked/pending/evidence/reschedule/closed/completed actions against the existing gates; it does not authorize operations. Actual own/other Client HTTP checks are separate.

The broad browser suite passes 277 layout/brand/font/role checks. For the 32 scoped axe states used in Phase 7, set `WONDER_AXE_PAGES` to `Bookings-ClientDashboard,Bookings-MyBookings,Bookings-ClientDetails,Payments-MyUploads,Profile-Index,Notifications-Index-Client,Communications-Index-Client,Feedback-Create,Feedback-Index,Feedback-Details,Bookings-Create,Payments-Upload,RescheduleRequests-Create,RescheduleRequests-MyRequests,Payments-RequestRefund,Payments-MyRefundRequests`. The pre-existing unscoped published-Homepage quotation contrast finding remains tracked in report Section 16; it is not hidden by a claim of full-site accessibility compliance.

Shared internal Notifications/Communications branches were checked with `UI_PAGES=Communications-Index,Communications-Index-Admin,Notifications-Index` in `internal-ui.browser.mjs`: 12 responsive checks plus existing navigation/header/focus/History/account interactions pass. No internal-role redesign was performed. See report Section 17 for screenshots, all seven Client widths, isolated SQL/browser mutations, source/package preservation and manual-review limits.

After rendering the fixtures, run the DOM suite with Node and jsdom:

    node tests/OwnerRegression/ui.dom.mjs

If jsdom is installed outside the repository, set JSDOM_MODULE to the file URL of
its lib/api.js entry point. The suite runs no browser and makes no external requests.
It checks labels, dialog titles, unique IDs, UTF-8 display text, antiforgery fields,
package inclusion binding after deletion, chat duplicate sends and draft preservation,
and booking wizard progression, discount limits, terms, and location invalidation.

The shared app-ui.css defines typography, semantic status colors, controls, focus,
responsive dialogs, and chat layout for all four roles. Package creation and editing
share _PackageEditor; the client status cards use _StatusBadge.

Browser fixture verification now uses installed Edge with Puppeteer. The feedback
delivery rendered 87 synthetic pages, passed 555 DOM checks, and exercised feedback
at 1440/768/390px, including local photo previews, duplicate-submit prevention,
validation recovery, and Staff confirmation. Run `feedback.browser.mjs` after rendering;
set `AXE_CORE_PATH` for automated WCAG checks. `client-experience.browser.mjs` also
supports `CLIENT_PAGES=Home-Index-published,Home-Index-Guest-published` for the consented
public showcase fixtures. See `FEEDBACK_FEATURE.md` for results and deployment gaps.
Real-user sign-off, live provider checks, and assistive-technology testing remain
separate; DOM/browser fixtures do not establish full screen-reader usability.

Phase 6 rework adds the opt-in Concierge shell and meaningful DOM checks for fractional pricing/single discount, overnight schedule display, one moved price subtree, associated validation, immediate duplicate protection, cancelled/restored retries, file-preview type/size/encoding/URL lifecycle and calendar lead time/blocked/full refresh/navigation/async focus. Render the real Razor fixtures first; `ui.dom.mjs` passes **816** checks. Native picker/keyboard and connected HTTP/SQL behavior use the separate fixture in `tests/ClientVisual/README.md`.

Final broad layout check: 277 checks on all 54 Client/edge fixtures and internal-role isolation, with 26 scoped axe scans at 320/1280px. Set:

```powershell
$env:WONDER_AXE_PAGES='Home-Index-Guest,Home-About-Guest,Home-About-Guest-published,Home-Gallery-Guest,Home-Packages,Home-Package,Bookings-Create,Bookings-MyBookings,Bookings-ClientDetails,Payments-Upload,Payments-MyUploads,RescheduleRequests-Create,RescheduleRequests-MyRequests'
node tests/OwnerRegression/wonder.browser.mjs
```

An **additional unscoped** scan reports the existing published-Homepage testimonial quotation contrast issue at 320/1280px. Current and complete pre-rework CSS produce the same colors; that approved Homepage was preserved. See `.tmp-owner-build/concierge-broad-unscoped-accessibility.json`, `concierge-published-contrast-baseline.json` and report Section 16. Do not describe the scoped run as an all-site accessibility pass. No internal-role/business/security source was redesigned.
