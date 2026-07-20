# Owner and Staff Interface Refactor

## Updated
- Owner Dashboard now contains only Payments to Verify, Refund Requests, Confirmed Events, and Revenue This Month.
- Added latest five pending payment verification activities.
- Redesigned upcoming confirmed events with a responsive empty state.
- Staff Dashboard now focuses on latest booking requests and reschedule requests.
- Staff Bookings filters now emphasize Search, Booking Status, Event Date, and Package; Staff no longer sees Cancelled, Expired, or Payment Status filters.
- Archived Packages navigation and archive/restore endpoints are no longer available to Staff; Admin access remains.
- Added responsive styling for dashboard activity rows and balanced booking filters.

## Files changed
- Controllers/DashboardController.cs
- Controllers/BookingsController.cs
- Controllers/ServiceController.cs
- ViewModels/RoleDashboardViewModel.cs
- ViewModels/BookingManagementViewModel.cs
- Views/Dashboard/Owner.cshtml
- Views/Dashboard/Staff.cshtml
- Views/Bookings/Index.cshtml
- Views/Services/Index.cshtml
- wwwroot/css/site.css

## Local verification
Run:

```powershell
dotnet clean
dotnet build
dotnet run
```

Then test the Owner Dashboard, Staff Dashboard, Staff Bookings, and Staff Packages pages with their respective accounts.
