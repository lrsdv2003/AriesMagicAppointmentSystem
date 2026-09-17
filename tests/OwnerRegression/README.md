# Owner regression checks

Run from the repository root on Windows with .NET 9 and SQL Server LocalDB:

    dotnet run --project tests/OwnerRegression/OwnerRegression.csproj

To also render six Owner pages using synthetic data:

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
