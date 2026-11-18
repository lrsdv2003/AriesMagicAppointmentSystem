using AriesMagicAppointmentSystem.Services;
using System.Net;
using System.Text;

internal static class LocationUpgradeTests
{
    private sealed class FakeMapHandler : HttpMessageHandler
    {
        public int Calls {get;private set;}
        public bool Fail {get;set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Calls++;
            var uri=request.RequestUri!.ToString();
            if (!uri.Contains("countrycodes=ph") || !uri.Contains("limit=8"))
                throw new Exception("Search must preserve Philippine bias and bounded result limit.");
            if (Fail)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            const string sample = """
                [
                  {"lat":"14.6510","lon":"121.0500","display_name":"Quezon Memorial Circle, Quezon City",
                   "addresstype":"attraction","place_rank":30},
                  {"lat":"16.4110","lon":"120.5950","display_name":"Baguio City, Benguet",
                   "addresstype":"city","place_rank":16},
                  {"lat":"10.1230","lon":"119.8450","display_name":"Rural Palawan Barangay",
                   "addresstype":"village","place_rank":18},
                  {"lat":"50.0000","lon":"121.0500","display_name":"Outside PH",
                   "addresstype":"house","place_rank":30}
                ]
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content=new StringContent(sample,Encoding.UTF8,"application/json")
            });
        }
    }

    public static async Task RunAsync()
    {
        var checkedCount=0;
        void Check(bool assertion,string name)
        {
            if(!assertion)throw new Exception("LOCATION FAILED: "+name);
            Console.WriteLine("PASS: "+name);checkedCount++;
        }
        Check(VenueLocationPolicy.IsMethod("Search")&&VenueLocationPolicy.IsMethod("MapPin")
            &&VenueLocationPolicy.IsMethod("Manual")&&!VenueLocationPolicy.IsMethod("Unknown"),
            "Only three recognized location methods");
        Check(VenueLocationPolicy.SufficientManualAddress("Unit 4 Oak St.","San Isidro","Antipolo","Rizal"),
            "Detailed subdivision, barangay and city in Rizal accepted");
        Check(VenueLocationPolicy.SufficientManualAddress(null,"San Pedro","Banaue","Ifugao"),
            "Provincial barangay without street number remains valid");
        Check(!VenueLocationPolicy.SufficientManualAddress("House 4",null,null,"Metro Manila"),
            "Missing city cannot silently pass manual-address validation");
        Check(!VenueLocationPolicy.SufficientManualAddress(null,null,"Quezon City","Metro Manila"),
            "City-only approximate address does not qualify for manual review");
        Check(VenueLocationPolicy.FormatManualAddress("Unit 4 Oak St.","San Isidro","Antipolo","Rizal","1870","Aries Hall")
            .Contains("Brgy. San Isidro"),"Manual venue preserves complete structured address");
        Check(!VenueLocationPolicy.HasCoordinatePair(null,121.03) &&
            !VenueLocationPolicy.HasCoordinatePair(0,0) && !VenueLocationPolicy.HasCoordinatePair(double.NaN,121),
            "Incomplete, default and malformed coordinates are rejected");
        Check(VenueLocationPolicy.InPhilippines(14.65,121.05) &&
            VenueLocationPolicy.InPhilippines(10.12,119.84) &&
            !VenueLocationPolicy.InPhilippines(50,121),
            "Metro Manila and provincial coordinates supported without a Manila-only radius");
        Check(!VenueLocationPolicy.CanOpenPayment(VenueLocationPolicy.PendingReview) &&
            !VenueLocationPolicy.CanOpenPayment(VenueLocationPolicy.AwaitingCustomerAcceptance) &&
            VenueLocationPolicy.CanOpenPayment(VenueLocationPolicy.Accepted) &&
            VenueLocationPolicy.CanOpenPayment(VenueLocationPolicy.Calculated),
            "Manual location and revised quotes cannot unlock payment before acceptance");
        Check(VenueLocationPolicy.CanOpenPayment(null),
            "Legacy existing booking price state retained without automatic conversion");

        var handler=new FakeMapHandler();
        using var http=new HttpClient(handler);
        var service=new NominatimGeocodingService(http);
        var query="Quezon Memorial Circle, Diliman, Quezon City "+Guid.NewGuid().ToString("N");
        var list=await service.SearchAsync(query);
        Check(list.Count==3 && list[0].IsPrecise && !list[1].IsPrecise && !list[2].IsPrecise,
            "Well-known venue is precise; city and rural barangay results require review");
        var cached=await service.SearchAsync(query);
        Check(cached.Count==list.Count && handler.Calls==1,"Repeated search reuses cached result without new API request");
        handler.Fail=true;
        try
        {
            await service.SearchAsync("Synthetic missing location "+Guid.NewGuid().ToString("N"));
            throw new Exception("Service outage should be reported");
        }
        catch(HttpRequestException)
        {
            Check(true,"Provider HTTP outage is propagated for safe review fallback");
        }
        Console.WriteLine("SUCCESS: "+checkedCount+" independent location policy/search checks passed.");
    }
}
