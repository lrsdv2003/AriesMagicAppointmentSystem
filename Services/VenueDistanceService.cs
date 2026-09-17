using Microsoft.Extensions.Options;

namespace AriesMagicAppointmentSystem.Services
{
    public class VenueDistanceService : IVenueDistanceService
    {
        private const double EarthRadiusKm = 6371.0088;
        private readonly VenueDistanceOptions _options;

        public VenueDistanceService(IOptions<VenueDistanceOptions> options)
        {
            _options = options.Value;
        }

        public bool IsValidCoordinate(double latitude, double longitude)
            => latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180;

        public VenueDistanceResult Calculate(double latitude, double longitude)
            => Calculate(_options.BaseLatitude, _options.BaseLongitude, latitude, longitude);

        public VenueDistanceResult Calculate(double originLatitude, double originLongitude, double destinationLatitude, double destinationLongitude)
        {
            if (!IsValidCoordinate(destinationLatitude, destinationLongitude))
                throw new ArgumentOutOfRangeException(nameof(destinationLatitude), "The venue coordinates are invalid.");

            if (!IsValidCoordinate(originLatitude, originLongitude))
                throw new ArgumentOutOfRangeException(nameof(originLatitude), "The starting coordinates are invalid.");

            if (_options.EstimatedTravelSpeedKph <= 0)
                throw new InvalidOperationException("The estimated travel speed must be greater than zero.");

            var distanceKm = HaversineKm(originLatitude, originLongitude, destinationLatitude, destinationLongitude);
            var roundedDistance = Math.Round(distanceKm, 2, MidpointRounding.AwayFromZero);
            var estimatedTravelTimeMinutes = (int)Math.Ceiling(distanceKm / _options.EstimatedTravelSpeedKph * 60);

            if (distanceKm > _options.MaximumServiceDistanceKm)
            {
                return new VenueDistanceResult
                {
                    DistanceKm = roundedDistance,
                    TravelFee = 0m,
                    ServiceZone = "Zone D",
                    IsServiceable = false,
                    RequiresManualReview = false,
                    MaximumServiceDistanceKm = _options.MaximumServiceDistanceKm,
                    EstimatedTravelTimeMinutes = estimatedTravelTimeMinutes,
                    OriginLatitude = originLatitude,
                    OriginLongitude = originLongitude
                };
            }

            if (distanceKm > _options.ManualReviewDistanceKm)
            {
                return new VenueDistanceResult
                {
                    DistanceKm = roundedDistance,
                    TravelFee = CalculateTravelFee(distanceKm),
                    ServiceZone = "Zone C",
                    IsServiceable = true,
                    RequiresManualReview = true,
                    MaximumServiceDistanceKm = _options.MaximumServiceDistanceKm,
                    EstimatedTravelTimeMinutes = estimatedTravelTimeMinutes,
                    OriginLatitude = originLatitude,
                    OriginLongitude = originLongitude
                };
            }

            if (distanceKm > _options.FreeTravelRadiusKm)
            {
                return new VenueDistanceResult
                {
                    DistanceKm = roundedDistance,
                    TravelFee = CalculateTravelFee(distanceKm),
                    ServiceZone = "Zone B",
                    IsServiceable = true,
                    RequiresManualReview = false,
                    MaximumServiceDistanceKm = _options.MaximumServiceDistanceKm,
                    EstimatedTravelTimeMinutes = estimatedTravelTimeMinutes,
                    OriginLatitude = originLatitude,
                    OriginLongitude = originLongitude
                };
            }

            return new VenueDistanceResult
            {
                DistanceKm = roundedDistance,
                TravelFee = 0m,
                ServiceZone = "Zone A",
                IsServiceable = true,
                RequiresManualReview = false,
                MaximumServiceDistanceKm = _options.MaximumServiceDistanceKm,
                EstimatedTravelTimeMinutes = estimatedTravelTimeMinutes,
                OriginLatitude = originLatitude,
                OriginLongitude = originLongitude
            };
        }

        private decimal CalculateTravelFee(double distanceKm)
        {
            var chargeableKm = Math.Max(0, distanceKm - _options.FreeTravelRadiusKm);
            return Math.Round((decimal)chargeableKm * _options.TravelFeePerKm, 2, MidpointRounding.AwayFromZero);
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            static double ToRadians(double value) => value * Math.PI / 180.0;

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                    + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                    * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return EarthRadiusKm * c;
        }
    }
}
