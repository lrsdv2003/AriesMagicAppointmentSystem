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

The render option now includes Client, Staff, Admin and Owner filter fixtures (16 pages total).
Run the browser checks from the repository root with Puppeteer and Chromium installed:

    node tests/OwnerRegression/filters.browser.mjs

If Puppeteer is installed outside this repository, set PUPPETEER_MODULE to its module URL.
The browser test intercepts all requests and serves only synthetic HTML and local static assets.
It checks 13 filter screens at desktop/mobile sizes, labels, touch targets, combined GET
serialization, reset links, and Client local filter persistence/empty states. It does not
claim to exercise live authentication or every controller query against production data.
