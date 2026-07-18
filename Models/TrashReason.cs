namespace AriesMagicAppointmentSystem.Models
{
    public enum TrashReason
    {
        ClientAbandonedBooking = 0,
        PaymentNeverCompleted = 1,
        BookingRequestExpired = 2,
        BookingAutomaticallyCancelled = 3,
        ClientFailedToRespond = 4,
        InvalidBookingRequest = 5,
        RejectedByAdmin = 6
    }
}