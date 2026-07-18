using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class SystemActivityController : Controller
    {
        private readonly ISystemActivityService _activityService;

        public SystemActivityController(ISystemActivityService activityService)
        {
            _activityService = activityService;
        }

        public async Task<IActionResult> Index(
            int page = 1,
            int pageSize = 20,
            SystemActivityType? type = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? search = null)
        {
            var (items, totalCount) = await _activityService.GetPagedAsync(
                page, pageSize, type, fromDate, toDate, search);

            var viewModel = new SystemActivityIndexViewModel
            {
                Activities = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                TypeFilter = type,
                FromDateFilter = fromDate,
                ToDateFilter = toDate,
                SearchFilter = search,
                ActivityTypes = Enum.GetValues(typeof(SystemActivityType)).Cast<SystemActivityType>().ToList()
            };

            return View(viewModel);
        }
    }
}