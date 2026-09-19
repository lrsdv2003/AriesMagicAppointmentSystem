using AriesMagicAppointmentSystem.Models;
namespace AriesMagicAppointmentSystem.ViewModels;
public static class HistoryPresentation
{
    public static Dictionary<string,string> Routes(HistoryFilterViewModel f, int? page = null) => new()
    {
        ["Search"]=f.Search??"", ["BookingStatus"]=f.BookingStatus??"", ["DateRange"]=f.DateRange??"",
        ["Year"]=f.Year?.ToString()??"", ["CompletedDateFrom"]=f.CompletedDateFrom?.ToString("yyyy-MM-dd")??"",
        ["CompletedDateTo"]=f.CompletedDateTo?.ToString("yyyy-MM-dd")??"", ["ServiceId"]=f.ServiceId?.ToString()??"",
        ["EventType"]=f.EventType??"", ["PaymentStatus"]=f.PaymentStatus??"", ["RefundStatus"]=f.RefundStatus??"",
        ["SortBy"]=f.SortBy??"", ["Page"]=(page??f.Page).ToString(), ["PageSize"]=f.PageSize.ToString()
    };
    public static bool Filtered(HistoryFilterViewModel f) => !string.IsNullOrWhiteSpace(f.Search) ||
        !string.IsNullOrWhiteSpace(f.BookingStatus) || !string.IsNullOrWhiteSpace(f.DateRange) || f.ServiceId.HasValue ||
        f.CompletedDateFrom.HasValue || f.CompletedDateTo.HasValue || !string.IsNullOrWhiteSpace(f.EventType) ||
        !string.IsNullOrWhiteSpace(f.PaymentStatus) || !string.IsNullOrWhiteSpace(f.RefundStatus);
    public static bool ShowEvent(string name, bool financial) => name is
        TimelineEventType.BookingCreated or TimelineEventType.BookingApproved or TimelineEventType.BookingConfirmed or
        TimelineEventType.BookingCompleted or TimelineEventType.BookingCancelled or TimelineEventType.BookingDeclined or
        TimelineEventType.BookingExpired or TimelineEventType.BookingArchived or TimelineEventType.BookingReopened or
        TimelineEventType.RescheduleRequested or TimelineEventType.RescheduleApproved or TimelineEventType.RescheduleRejected ||
        financial && name is TimelineEventType.PaymentVerified or TimelineEventType.RefundRequested;
}
