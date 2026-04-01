namespace AriesMagicAppointmentSystem.Models
{
    public static class TimelineEventType
    {
        public const string BookingCreated = "BookingCreated";
        public const string BookingApproved = "BookingApproved";
        public const string DownpaymentUploaded = "DownpaymentUploaded";
        public const string PaymentVerified = "PaymentVerified";
        public const string BookingConfirmed = "BookingConfirmed";
        public const string BookingCompleted = "BookingCompleted";
        public const string BookingReopened = "BookingReopened";
        public const string RescheduleRequested = "RescheduleRequested";
        public const string RescheduleApproved = "RescheduleApproved";
        public const string RescheduleRejected = "RescheduleRejected";
        public const string BookingExpired = "BookingExpired";
        public const string BookingDeclined = "BookingDeclined";
    }
}