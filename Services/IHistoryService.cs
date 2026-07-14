using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;

namespace AriesMagicAppointmentSystem.Services
{
    public interface IHistoryService
    {
        /// <summary>
        /// Finds bookings that have passed their event end time and are still sitting as
        /// "Confirmed", flips them to "Completed", and makes sure every completed booking
        /// (however it got there) has a permanent "Archived" timeline entry and a one-time
        /// Owner notification. Safe to call as often as needed - fully idempotent.
        /// </summary>
        Task<int> ArchiveDueBookingsAsync();

        Task<HistoryIndexViewModel> GetHistoryAsync(HistoryFilterViewModel filters);

        Task<HistoryDetailsViewModel?> GetDetailsAsync(int bookingId);

        Task<HistorySummaryViewModel> GetSummaryAsync();

        /// <summary>
        /// Returns the same filtered/sorted result set as GetHistoryAsync but without paging,
        /// for use by CSV/PDF/print exports.
        /// </summary>
        Task<List<HistoryRowViewModel>> GetHistoryForExportAsync(HistoryFilterViewModel filters);
    }
}
