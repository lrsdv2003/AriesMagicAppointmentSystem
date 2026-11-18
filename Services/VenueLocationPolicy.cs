namespace AriesMagicAppointmentSystem.Services;

/// <summary>Customer location is a claim; only a reviewed quote authorizes payment.</summary>
public static class VenueLocationPolicy
{
    public const string Search = "Search";
    public const string MapPin = "MapPin";
    public const string Manual = "Manual";

    public const string CustomerConfirmed = "CustomerConfirmed";
    public const string NeedsStaffReview = "NeedsStaffReview";
    public const string RequiresClarification = "RequiresClarification";
    public const string StaffVerified = "StaffVerified";

    public const string Calculated = "Calculated";
    public const string PendingReview = "PendingReview";
    public const string AwaitingCustomerAcceptance = "AwaitingCustomerAcceptance";
    public const string Accepted = "Accepted";

    public static bool IsMethod(string? method) => method is Search or MapPin or Manual;
    public static bool HasCoordinatePair(double? latitude, double? longitude)
        => latitude.HasValue && longitude.HasValue
           && double.IsFinite(latitude.Value) && double.IsFinite(longitude.Value)
           && latitude.Value is >= -90 and <= 90 && longitude.Value is >= -180 and <= 180
           && !(latitude.Value == 0d && longitude.Value == 0d);

    // Client-submitted coordinates are not proof of venue identity. Bound to the Philippines
    // for this business; never substitute the center of a city or province for an event venue.
    public static bool InPhilippines(double latitude, double longitude)
        => latitude is >= 4.0 and <= 22.5 && longitude is >= 116 and <= 128;

    public static bool SufficientManualAddress(string? address, string? barangay,
        string? city, string? province)
        => !string.IsNullOrWhiteSpace(city) && city.Trim().Length >= 2
           && !string.IsNullOrWhiteSpace(province) && province.Trim().Length >= 2
           && ((!string.IsNullOrWhiteSpace(address) && address.Trim().Length >= 4)
               || (!string.IsNullOrWhiteSpace(barangay) && barangay.Trim().Length >= 2));

    public static string FormatManualAddress(string? address, string? barangay, string? city,
        string? province, string? postal, string? venue)
        => string.Join(", ", new[] { venue, address, barangay is null ? null : "Brgy. " + barangay,
                                     city, province, postal }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

    public static bool CanOpenPayment(string? travelFeeStatus) => travelFeeStatus is null or Calculated or Accepted;
}
