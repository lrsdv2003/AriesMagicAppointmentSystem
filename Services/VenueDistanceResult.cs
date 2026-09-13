namespace AriesMagicAppointmentSystem.Services
{
    public class VenueDistanceResult
    {
        public double DistanceKm { get; init; }
        public decimal TravelFee { get; init; }
        public string ServiceZone { get; init; } = string.Empty;
        public bool IsServiceable { get; init; }
        public bool RequiresManualReview { get; init; }
        public double MaximumServiceDistanceKm { get; init; }
    }
}
