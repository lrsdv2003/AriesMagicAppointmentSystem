namespace AriesMagicAppointmentSystem.Services
{
    public class VenueDistanceOptions
    {
        public double BaseLatitude { get; set; }
        public double BaseLongitude { get; set; }
        public double FreeTravelRadiusKm { get; set; } = 25;
        public double ManualReviewDistanceKm { get; set; } = 50;
        public double MaximumServiceDistanceKm { get; set; } = 80;
        public decimal TravelFeePerKm { get; set; } = 15m;
    }
}
