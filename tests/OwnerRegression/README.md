# Owner regression checks

Run from the repository root on Windows with .NET 9 and SQL Server LocalDB:

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

The render option now includes Client, Staff, Admin and Owner interface fixtures (34 pages total).
Run the browser checks from the repository root with Puppeteer and Chromium installed:

    node tests/OwnerRegression/filters.browser.mjs

If Puppeteer is installed outside this repository, set PUPPETEER_MODULE to its module URL.
The browser test intercepts all requests and serves only synthetic HTML and local static assets.
It checks 13 filter screens at desktop/mobile sizes, labels, touch targets, combined GET
serialization, reset links, and Client local filter persistence/empty states. It does not
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
