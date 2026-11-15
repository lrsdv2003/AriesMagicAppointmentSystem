using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Owner")]
    public class SystemActivityController : Controller
    {
        private readonly ISystemActivityService _activityService;

        public SystemActivityController(ISystemActivityService activityService)
        {
            _activityService = activityService;
        }

        public async Task<IActionResult> Index(
            int page = 1,
            int pageSize = 5,
            SystemActivityType? type = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? search = null, string sortBy = "newest")
        {
            page = Math.Max(page, 1);
            pageSize = 5;
            sortBy = string.Equals(sortBy, "oldest", StringComparison.OrdinalIgnoreCase) ? "oldest" : "newest";

            var (items, totalCount) = await _activityService.GetPagedAsync(
                page, pageSize, type, fromDate, toDate, search, businessOnly:true, sortBy: sortBy);

            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages)
            {
                page = totalPages;
                (items, totalCount) = await _activityService.GetPagedAsync(
                    page, pageSize, type, fromDate, toDate, search, businessOnly: true, sortBy: sortBy);
            }

            var viewModel = new SystemActivityIndexViewModel
            {
                Activities = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize)),
                TypeFilter = type,
                FromDateFilter = fromDate,
                ToDateFilter = toDate,
                SearchFilter = search,
                ActivityTypes = BusinessActivityTypes.All.ToList()
            };

            ViewBag.ActivitySort = sortBy;
            return View(viewModel);
        }
    }
}