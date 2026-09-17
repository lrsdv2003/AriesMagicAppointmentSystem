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
        public int EstimatedTravelTimeMinutes { get; init; }
        public int? AvailableTravelTimeMinutes { get; init; }
        public bool UsesPreviousEventLocation { get; init; }
        public bool HasSufficientTravelTime { get; init; } = true;
        public string? ServiceabilityReason { get; init; }
        public double? OriginLatitude { get; init; }
        public double? OriginLongitude { get; init; }
        public string StartingPointName { get; init; } = "Aries Magic Base";
    }
}
