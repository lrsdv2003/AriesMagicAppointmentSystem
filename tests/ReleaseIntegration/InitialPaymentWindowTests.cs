using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

internal static class InitialPaymentWindowTests
{
    private static int checks;
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception("FAILED: " + label);
        checks++;
        Console.WriteLine("PASS: " + label);
    }

    public static async Task RunAsync()
    {
        var approvalUtc = new DateTime(2026, 11, 10, 6, 0, 0, DateTimeKind.Utc);
        var deadlineUtc = InitialPaymentWindowPolicy.DeadlineFromOpenedUtc(approvalUtc);
        Check(deadlineUtc == approvalUtc.AddHours(24), "Opening payment sets precisely a 24-hour UTC deadline");
        Check(!InitialPaymentWindowPolicy.IsOverdue(deadlineUtc, deadlineUtc.AddTicks(-1)), "Just before deadline remains eligible");
        Check(InitialPaymentWindowPolicy.IsOverdue(deadlineUtc, deadlineUtc), "Exact deadline closes the payment window");
        Check(!InitialPaymentWindowPolicy.IsOverdue(null, deadlineUtc.AddDays(500)), "Legacy booking without marker receives no invented deadline");
        Check(InitialPaymentWindowPolicy.FormatManila(deadlineUtc).Contains("November 11, 2026 at 2:00 PM"), "Deadline display converts UTC to Philippine time");
        Check(InitialPaymentWindowPolicy.DeadlineFromOpenedUtc(approvalUtc) == deadlineUtc, "Repeated deadline calculations cannot restart window");

        var name = "AriesPaymentWindowFixture_" + Guid.NewGuid().ToString("N");
        var connection = $@"Server=(localdb)\MSSQLLocalDB;Database={name};Integrated Security=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        try
        {
            await using var db = new ApplicationDbContext(options);
            await db.Database.MigrateAsync();
            var user = new User { FullName = "Payment deadline synthetic client", Email = "deadline@example.test",
                PasswordHash = "NONLOGIN_SYNTHETIC", Role = "Client" };
            var service = new Service { Name = "Payment deadline synthetic package", Price = 10000m, DurationInHours = 2 };
            db.LegacyUsers.Add(user);db.Services.Add(service);await db.SaveChangesAsync();

            var cases = new Dictionary<string,int>();
            Booking Make(string key, string status, double? ageHours, string? proof = null)
            {
                var date = DateTime.Today.AddDays(70+cases.Count);
                var b = new Booking
                {
                    ClientId=user.Id,ServiceId=service.Id,PackageName=service.Name,
                    BasePrice=service.Price,FinalPrice=service.Price,
                    EventDate=date,StartTime=date.AddHours(10),EndTime=date.AddHours(12),
                    Status=status,EventType="Company Events",Motif="Blue",
                    PartyVenue="Synthetic Philippine venue",PaxCount=20,
                    ContactPerson="Synthetic",ContactNumber="09123456789"
                };
                db.Bookings.Add(b);
                db.SaveChanges();
                cases[key]=b.Id;
                if(ageHours.HasValue)
                    db.BookingTimelines.Add(new BookingTimeline{
                        BookingId=b.Id,EventType=TimelineEventType.PaymentWindowOpened,
                        CreatedAt=DateTime.UtcNow.AddHours(-ageHours.Value), Notes="Synthetic test UTC approval"
                    });
                if(proof!=null)
                    db.Payments.Add(new Payment{
                        BookingId=b.Id,Amount=2000m,ProofImagePath="synthetic-not-real.jpg",
                        PaymentMethod="GCash",Status=proof,UploadedAt=DateTime.Now.AddHours(-25)
                    });
                db.SaveChanges();
                return b;
            }

            Make("fresh",BookingStatus.AwaitingDownpayment,23);
            Make("overdue",BookingStatus.AwaitingDownpayment,25);
            Make("legacy",BookingStatus.AwaitingDownpayment,null);
            Make("pendingProof",BookingStatus.AwaitingDownpayment,25,PaymentStatus.Pending);
            Make("verifiedProof",BookingStatus.AwaitingDownpayment,25,PaymentStatus.Verified);
            Make("moreEvidence",BookingStatus.AwaitingDownpayment,25,PaymentStatus.AdditionalEvidenceRequired);
            Make("rejected",BookingStatus.AwaitingDownpayment,25,PaymentStatus.Rejected);
            Make("confirmed",BookingStatus.Confirmed,25);
            Make("completed",BookingStatus.Completed,25);
            db.ChangeTracker.Clear();

            var expiration = new BookingExpirationService(
                db,null!,new SystemActivityService(db),NullLogger<BookingExpirationService>.Instance);
            var expired = await expiration.ExpireOverdueBookingsAsync();
            Check(expired==2,"Only overdue unpaid and overdue rejected proofs expire");
            var statuses=await db.Bookings.AsNoTracking().Where(b=>cases.Values.Contains(b.Id))
                .ToDictionaryAsync(b=>b.Id,b=>b.Status);
            Check(statuses[cases["overdue"]]==BookingStatus.Expired,"Unpaid overdue booking transitions to Expired");
            Check(statuses[cases["rejected"]]==BookingStatus.Expired,"Rejected proof past deadline does not extend window");
            Check(statuses[cases["fresh"]]==BookingStatus.AwaitingDownpayment,"Still-open payment window remains payable");
            Check(statuses[cases["legacy"]]==BookingStatus.AwaitingDownpayment,"Legacy approved booking is not retroactively expired");
            Check(statuses[cases["pendingProof"]]==BookingStatus.AwaitingDownpayment,"Timely pending proof survives slow Owner review");
            Check(statuses[cases["verifiedProof"]]==BookingStatus.AwaitingDownpayment,"Verified payment never expires");
            Check(statuses[cases["moreEvidence"]]==BookingStatus.AwaitingDownpayment,"Additional proof review is preserved");
            Check(statuses[cases["confirmed"]]==BookingStatus.Confirmed,"Confirmed booking remains confirmed");
            Check(statuses[cases["completed"]]==BookingStatus.Completed,"Completed booking remains completed");
            Check(await db.Bookings.AnyAsync(b=>b.Id==cases["overdue"] && b.ArchivedAt!=null),
                "Expired booking remains stored with archival metadata");
            Check(await db.BookingTimelines.CountAsync(t=>t.EventType==TimelineEventType.BookingExpired
                && cases.Values.Contains(t.BookingId))==2,"Every expiry creates exactly one timeline event");
            Check(await db.SystemActivities.CountAsync(a=>a.Type==SystemActivityType.BookingExpired)>=2,
                "Expiry is recorded in system activity audit");
            Check((await expiration.ExpireOverdueBookingsAsync())==0,
                "Repeated expiration pass is idempotent");
            Check(await db.BookingTimelines.CountAsync(t=>t.EventType==TimelineEventType.BookingExpired
                && cases.Values.Contains(t.BookingId))==2,"Repeated pass does not duplicate expiry history");

            var deadlines=await InitialPaymentWindowPolicy.GetDeadlinesUtcAsync(db,cases.Values);
            Check(!deadlines.ContainsKey(cases["legacy"])&&deadlines.ContainsKey(cases["fresh"]),
                "Deadline queries never infer UTC timestamps for legacy bookings");
            Console.WriteLine("PAYMENT_WINDOW_TESTS_PASS checks=" + checks);
        }
        finally
        {
            if (!name.StartsWith("AriesPaymentWindowFixture_",StringComparison.Ordinal))
                throw new InvalidOperationException("Guarded fixture cleanup rejected");
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(
                @"Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=True;TrustServerCertificate=True");
            await master.OpenAsync();
            var sql = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
            await using var command=new SqlCommand(sql,master);
            await command.ExecuteNonQueryAsync();
            Console.WriteLine("Synthetic LocalDB database cleaned up.");
        }
    }
}
