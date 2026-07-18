using System.Security.Claims;

namespace AriesMagicAppointmentSystem.Extensions
{
    /// <summary>
    /// Centralizes the "who can do what" rules that were previously re-implemented ad hoc as
    /// User.IsInRole("A") || User.IsInRole("B") checks scattered across multiple controllers and
    /// views. Keeping these in one place means a future permission change (e.g. adding a role to
    /// an action) only needs to happen here instead of being hunted down across the UI.
    /// </summary>
    public static class RoleAccessExtensions
    {
        // Bookings
        public static bool CanManageBookingRecords(this ClaimsPrincipal user) =>
            user.IsInRole("Staff") || user.IsInRole("Owner");

        public static bool CanCompleteBookings(this ClaimsPrincipal user) =>
            user.IsInRole("Staff");

        // Packages / Services
        public static bool CanViewPackages(this ClaimsPrincipal user) =>
            user.IsInRole("Staff") || user.IsInRole("Admin") || user.IsInRole("Owner");

        public static bool CanEditPackages(this ClaimsPrincipal user) =>
            user.IsInRole("Admin") || user.IsInRole("Owner");

        // Payments / Refunds (business decisions belong to Owner only)
        public static bool CanVerifyPayments(this ClaimsPrincipal user) =>
            user.IsInRole("Owner");

        public static bool CanReviewRefunds(this ClaimsPrincipal user) =>
            user.IsInRole("Owner");

        // Reschedule requests
        public static bool CanViewRescheduleRequests(this ClaimsPrincipal user) =>
            user.IsInRole("Staff") || user.IsInRole("Admin") || user.IsInRole("Owner");

        public static bool CanApproveRescheduleRequests(this ClaimsPrincipal user) =>
            user.IsInRole("Owner");

        // Calendar
        public static bool CanViewCalendar(this ClaimsPrincipal user) =>
            user.IsInRole("Staff") || user.IsInRole("Admin") || user.IsInRole("Owner");

        public static bool CanManageCalendarSettings(this ClaimsPrincipal user) =>
            user.IsInRole("Admin");

        // Reports
        public static bool CanViewReports(this ClaimsPrincipal user) =>
            user.IsInRole("Owner");

        // Users
        public static bool CanManageUsers(this ClaimsPrincipal user) =>
            user.IsInRole("Admin");

        // History (archived / completed bookings)
        public static bool CanViewHistory(this ClaimsPrincipal user) =>
            user.IsInRole("Owner") || user.IsInRole("Staff");

        // Owner and Admin can export/print history reports. Staff can view but not export.
        public static bool CanExportHistoryReports(this ClaimsPrincipal user) =>
            user.IsInRole("Owner") || user.IsInRole("Admin");

        // Nobody can edit an archived/completed booking through the History module -
        // it is a permanent, read-only record. Kept here so the rule lives in one place.
        public static bool CanModifyHistoryRecords(this ClaimsPrincipal user) => false;
    }
}
