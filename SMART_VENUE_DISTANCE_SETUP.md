# Smart Venue Distance & Serviceability — Setup Guide

## Temporary Aries Magic base location

For development, this project currently uses **STI College Ortigas-Cainta / STI Academic Center, Ortigas Avenue Extension, Cainta, Rizal 1900** as the temporary Aries Magic base location. STI's official campus page confirms the address.

Current coordinates configured in `appsettings.json`:

```json
"BaseLatitude": 14.58125,
"BaseLongitude": 121.1258
```

These coordinates correspond to a mapped STI Academic Center location. Because the exact future Aries Magic address may differ, treat these as temporary development coordinates.

## How to replace the location later

Open:

`appsettings.json`

Find:

```json
"VenueDistance": {
  "BaseLatitude": 14.58125,
  "BaseLongitude": 121.1258,
  "FreeTravelRadiusKm": 25,
  "ManualReviewDistanceKm": 50,
  "MaximumServiceDistanceKm": 80,
  "TravelFeePerKm": 15
}
```

Only replace the first two values:

```json
"BaseLatitude": EXACT_ARIES_MAGIC_LATITUDE,
"BaseLongitude": EXACT_ARIES_MAGIC_LONGITUDE
```

Do **not** change the distance rules unless the business rules themselves change.

Example:

```json
"VenueDistance": {
  "BaseLatitude": 14.123456,
  "BaseLongitude": 121.654321,
  "FreeTravelRadiusKm": 25,
  "ManualReviewDistanceKm": 50,
  "MaximumServiceDistanceKm": 80,
  "TravelFeePerKm": 15
}
```

Then restart the application. No database migration is needed just because the base coordinates changed. Existing bookings keep their previously calculated distance/travel-fee snapshot. New bookings use the new base location.

## How to get the exact coordinates later

1. Open Google Maps or another map service.
2. Find the exact Aries Magic address/location.
3. Right-click/tap the exact point.
4. Copy the latitude and longitude.
5. Replace only `BaseLatitude` and `BaseLongitude` in `appsettings.json`.
6. Restart the application.

## Current rules

- 0–25 KM: Zone A, serviceable, no travel fee
- >25–50 KM: Zone B, serviceable, ₱15 per KM beyond 25 KM
- >50–80 KM: Zone C, serviceable but requires manual review
- >80 KM: Zone D, not serviceable

Distance is calculated using the Haversine formula (straight-line geographical distance), not actual driving distance.
