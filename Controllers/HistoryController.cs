using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AriesMagicAppointmentSystem.Controllers
{
    /// <summary>
    /// Read-only history of completed bookings. Owner and Staff can browse and search
    /// the same records. Dedicated History print and export routes are retired. Nobody can edit a
    /// historical record through this controller - there is intentionally no Edit/Delete action.
    /// </summary>
    [Authorize(Roles = "Owner,Staff")]
    public class HistoryController : Controller
    {
        private readonly IHistoryService _historyService;
        private readonly ITrashHistoryService? _archives;

        public HistoryController(IHistoryService historyService, ITrashHistoryService? archives = null)
        {
            _historyService = historyService;
            _archives = archives;
        }

        public async Task<IActionResult> Index(HistoryFilterViewModel filters)
        {
            if (!User.IsInRole("Owner"))
            {
                filters.PaymentStatus = null;
                filters.RefundStatus = null;
                if (filters.SortBy is "HighestRevenue" or "LowestRevenue") filters.SortBy = "Newest";
            if (User.IsInRole("Staff") && !User.IsInRole("Owner") && filters.SortBy == "ClientName")
                filters.SortBy = "Newest";
            }
            var model = await _historyService.GetHistoryAsync(filters);
            return View(model);
        }

        public async Task<IActionResult> Details(int id)
        {
            var model = await _historyService.GetDetailsAsync(id);

            if (model == null)
            {
                // Preserve existing authorized deep links without listing unsuccessful
                // bookings as completed History or granting Staff archive administration.
                if (_archives != null && await _archives.GetDetailsAsync(id) != null)
                    return User.IsInRole("Owner")
                        ? RedirectToAction("Details", "TrashHistory", new { id })
                        : RedirectToAction("Details", "Bookings", new { id });
                return NotFound();
            }

            return View(model);
        }

    }
}
