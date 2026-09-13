namespace AriesMagicAppointmentSystem.Services
{
    public interface IVenueDistanceService
    {
        VenueDistanceResult Calculate(double latitude, double longitude);
        bool IsValidCoordinate(double latitude, double longitude);
    }
}
