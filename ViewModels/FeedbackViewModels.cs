using System.ComponentModel.DataAnnotations;
using AriesMagicAppointmentSystem.Models;
namespace AriesMagicAppointmentSystem.ViewModels;

public class FeedbackInput
{
    public int BookingId { get; set; }
    [Range(1,5,ErrorMessage="Choose an overall rating from 1 to 5.")] public int OverallRating { get; set; }
    [Range(1,5,ErrorMessage="Choose a performance rating from 1 to 5.")] public int PerformanceRating { get; set; }
    [Range(1,5,ErrorMessage="Choose a booking rating from 1 to 5.")] public int BookingRating { get; set; }
    [StringLength(1500)] public string? Experience { get; set; }
    [StringLength(1500)] public string? Suggestions { get; set; }
    [StringLength(60)] public string? PublicDisplayName { get; set; }
    public bool TextConsent { get; set; }
    public bool NameConsent { get; set; }
    public bool MediaConsent { get; set; }
    public List<IFormFile> Files { get; set; } = [];
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public string EventLabel { get; set; } = "";
}
public class FeedbackReviewInput
{
    public int Id { get; set; }
    [Required] public string Version { get; set; } = "";
    [Required] public string Decision { get; set; } = "";
    [StringLength(500)] public string? Reason { get; set; }
    public List<int> PublicMediaIds { get; set; } = [];
    public bool ConfirmPublication { get; set; }
}
public sealed record FeedbackListPage(List<EventFeedback> Items,int Page,int Pages,int Total,string Filter,string Search);
public sealed record ClientFeedbackPage(List<EventFeedback> Items,List<Booking> Eligible,int Page,int Pages);
