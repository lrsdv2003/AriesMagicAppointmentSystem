using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class TrashHistoryController : Controller
    {
        private readonly ITrashHistoryService _trashHistoryService;

        public TrashHistoryController(ITrashHistoryService trashHistoryService)
        {
            _trashHistoryService = trashHistoryService;
        }

        public async Task<IActionResult> Index(TrashHistoryFilterViewModel filters)
        {
            var model = await _trashHistoryService.GetTrashHistoryAsync(filters);
            return View(model);
        }

        public async Task<IActionResult> Details(int id)
        {
            var model = await _trashHistoryService.GetDetailsAsync(id);
            if (model == null)
            {
                return NotFound();
            }
            return View(model);
        }
    }
}