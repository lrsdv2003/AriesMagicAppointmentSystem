using System.ComponentModel.DataAnnotations;
using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels;

public sealed class ConfirmedBookingChangeInput
{
    public int BookingId { get; set; }
    [Required, Range(1, int.MaxValue)]
    public int ServiceId { get; set; }
    [Required, StringLength(500, MinimumLength = 8)]
    public string Venue { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public sealed class ConfirmedBookingChangeEditViewModel
{
    public Booking Booking { get; set; } = null!;
    public ConfirmedBookingChangeInput Input { get; set; } = new();
    public IReadOnlyList<Service> AvailablePackages { get; set; } = Array.Empty<Service>();
    public decimal VerifiedPayments { get; set; }
    public string? Error { get; set; }
}
