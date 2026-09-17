namespace AriesMagicAppointmentSystem.Services
{
    public interface IVenueDistanceService
    {
        VenueDistanceResult Calculate(double latitude, double longitude);
        VenueDistanceResult Calculate(double originLatitude, double originLongitude, double destinationLatitude, double destinationLongitude);
        bool IsValidCoordinate(double latitude, double longitude);
    }
}
