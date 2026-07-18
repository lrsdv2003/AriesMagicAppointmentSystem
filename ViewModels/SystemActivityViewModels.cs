using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class SystemActivityIndexViewModel
    {
        public List<SystemActivity> Activities { get; set; } = new();

        public int TotalCount { get; set; }

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalPages { get; set; }

        public SystemActivityType? TypeFilter { get; set; }

        public DateTime? FromDateFilter { get; set; }

        public DateTime? ToDateFilter { get; set; }

        public string? SearchFilter { get; set; }

        public List<SystemActivityType> ActivityTypes { get; set; } = new();
    }
}