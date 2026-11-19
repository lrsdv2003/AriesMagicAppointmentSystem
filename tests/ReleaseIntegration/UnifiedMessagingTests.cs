using System.Security.Claims;
using System.Text.Json;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

internal static class UnifiedMessagingTests
{
    private static int _checks;
    private static void Verify(bool condition, string label)
    {
        if (!condition) throw new Exception("FAILED: " + label);
        _checks++;
        Console.WriteLine("PASS UNIFIED: " + label);
    }

    private sealed class NoTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static CommunicationsController As(ApplicationDbContext db, ApplicationUser user, string role)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Role, role)
        }, "SyntheticTest"));
        var http = new DefaultHttpContext { User = principal };
        var controller = new CommunicationsController(db) {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        controller.TempData = new TempDataDictionary(http, new NoTempDataProvider());
        return controller;
    }

    public static async Task RunAsync()
    {
        var dbName = "AriesUnifiedMessagingSynthetic_" + Guid.NewGuid().ToString("N");
        var instance = Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE") ?? "MSSQLLocalDB";
        if (!System.Text.RegularExpressions.Regex.IsMatch(instance, "^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Test SQL instance is invalid");
        var connection = $@"Server=(localdb)\{instance};Database={dbName};Integrated Security=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
        await using var db = new ApplicationDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            Verify(await db.Database.CanConnectAsync(), "migrations applied to disposable SQL database");
            var cols = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE (TABLE_NAME='Conversations' AND COLUMN_NAME='SupportClientId') OR (TABLE_NAME='Messages' AND COLUMN_NAME='RelatedBookingId')").SingleAsync();
            Verify(cols == 2, "both new schema columns exist");

            var client = new ApplicationUser { Id=Guid.NewGuid().ToString("N"),UserName="client-a@example.test",Email="client-a@example.test",FullName="Synthetic Client A" };
            var second = new ApplicationUser { Id=Guid.NewGuid().ToString("N"),UserName="client-b@example.test",Email="client-b@example.test",FullName="Synthetic Client B" };
            var staff = new ApplicationUser { Id=Guid.NewGuid().ToString("N"),UserName="staff@example.test",Email="staff@example.test",FullName="Synthetic Staff" };
            var owner = new ApplicationUser { Id=Guid.NewGuid().ToString("N"),UserName="owner@example.test",Email="owner@example.test",FullName="Synthetic Owner" };
            db.Users.AddRange(client,second,staff,owner);
            foreach(var role in new[]{"Client","Staff","Owner"}) db.Roles.Add(new IdentityRole { Id=role,Name=role,NormalizedName=role.ToUpperInvariant() });
            foreach(var (user,role) in new[] { (client,"Client"),(second,"Client"),(staff,"Staff"),(owner,"Owner") })
                db.UserRoles.Add(new IdentityUserRole<string>{UserId=user.Id,RoleId=role});
            var legacy = new User {FullName="Synthetic Booking Owner",Email="legacy@example.test",PasswordHash="synthetic",Role="Client"};
            var service = new Service {Name="Synthetic Magic",Price=8500m,DurationInHours=2};
            db.LegacyUsers.Add(legacy); db.Services.Add(service);
            await db.SaveChangesAsync();
            Booking MakeBooking(ApplicationUser u,string package) => new() {
                ClientId=legacy.Id,ApplicationUserId=u.Id,ServiceId=service.Id,
                EventDate=DateTime.Today.AddDays(18),StartTime=DateTime.Today.AddDays(18).AddHours(15),
                EndTime=DateTime.Today.AddDays(18).AddHours(17), Status=BookingStatus.Confirmed,
                EventType="Birthday",Motif="Blue",PartyVenue="Synthetic Venue",
                ContactPerson=u.FullName,ContactNumber="09123456789",PackageName=package,
                PaxCount=20,BasePrice=8500m,FinalPrice=8500m,RequiredDownpayment=2000m
            };
            var b1=MakeBooking(client,"Premium"); var b2=MakeBooking(client,"Deluxe"); var other=MakeBooking(second,"All In");
            db.Bookings.AddRange(b1,b2,other);
            await db.SaveChangesAsync();

            var clientActions=As(db,client,"Client");
            var result=await clientActions.StartSupport() as RedirectToActionResult;
            Verify(result?.RouteValues?["id"] is int,"Client starts support thread");
            var canonicalId=(int)result!.RouteValues!["id"]!;
            var secondStart=await clientActions.StartSupport() as RedirectToActionResult;
            Verify((int)secondStart!.RouteValues!["id"]! == canonicalId,"repeated Contact Support reuses same thread");
            var firstLink=await clientActions.StartBookingConversation(b1.Id) as RedirectToActionResult;
            var secondLink=await clientActions.StartBookingConversation(b2.Id) as RedirectToActionResult;
            Verify((int)firstLink!.RouteValues!["id"]! == canonicalId && (int)secondLink!.RouteValues!["id"]! == canonicalId,"multiple bookings reuse canonical conversation");
            Verify((int)secondLink!.RouteValues!["relatedBookingId"]! == b2.Id,"booking button preselects appropriate booking context");

            var stored=await db.Conversations.Include(c=>c.Participants).SingleAsync(c=>c.Id==canonicalId);
            Verify(stored.SupportClientId==client.Id && stored.BookingId==null,"support conversation owned by stable Client identity, not booking");
            Verify(stored.Participants.Any(p=>p.UserId==client.Id) && stored.Participants.Any(p=>p.UserId==staff.Id),"Client and Staff are authorized participants");
            Verify(!stored.Participants.Any(p=>p.UserId==owner.Id),"Owner not granted generalized Client support access without an authorized booking interaction");
            Verify(typeof(CommunicationsController).GetMethod("CreateInternal") == null,
                "manual New Conversation backend action is retired, not hidden by CSS");
            var outsiderSummary = await As(db,second,"Client").ConversationSummaries() as JsonResult;
            using (var document = JsonDocument.Parse(JsonSerializer.Serialize(outsiderSummary!.Value)))
                Verify(document.RootElement.GetProperty("items").GetArrayLength()==0,
                    "unauthorized Client gets no other participant's conversation in sidebar updates");
            var supportSummary = await clientActions.ConversationSummaries() as JsonResult;
            using (var document = JsonDocument.Parse(JsonSerializer.Serialize(supportSummary!.Value)))
                Verify(document.RootElement.GetProperty("items").GetArrayLength()==1,
                    "authorized Client summary includes canonical support conversation");
            Verify(await db.Conversations.CountAsync()==1,
                "sidebar refresh never creates a conversation");
            var ownerActions=As(db,owner,"Owner");
            var ownerSummaryBefore=await ownerActions.ConversationSummaries() as JsonResult;
            using (var document = JsonDocument.Parse(JsonSerializer.Serialize(ownerSummaryBefore!.Value)))
                Verify(document.RootElement.GetProperty("items").GetArrayLength()==0,
                    "Owner cannot see a Client conversation before explicitly authorized booking contact");
            var ownerLink=await ownerActions.StartBookingConversation(b1.Id) as RedirectToActionResult;
            Verify(ownerLink?.RouteValues?["id"] is int ownerConversationId && ownerConversationId==canonicalId,
                "Owner booking communication provisions membership in existing support conversation");
            var ownerSummary=await ownerActions.ConversationSummaries() as JsonResult;
            using (var document = JsonDocument.Parse(JsonSerializer.Serialize(ownerSummary!.Value)))
                Verify(document.RootElement.GetProperty("items").GetArrayLength()==1,
                    "authorized Owner now sees the same canonical conversation");

            var firstSend=await clientActions.SendMessage(canonicalId,"Regarding the premium event.",relatedBookingId:b1.Id);
            var secondSend=await clientActions.SendMessage(canonicalId,"And the deluxe event.",relatedBookingId:b2.Id);
            Verify(firstSend is RedirectToActionResult && secondSend is RedirectToActionResult,"multiple booking-context messages accepted in one thread");
            var references=await db.Messages.Where(m=>m.ConversationId==canonicalId).OrderBy(m=>m.Id).Select(m=>m.RelatedBookingId).ToListAsync();
            Verify(references.SequenceEqual(new int?[]{b1.Id,b2.Id}),"each message retains its individual validated booking reference");

            var forbiddenContext=await clientActions.SendMessage(canonicalId,"Attempt cross-account booking.",relatedBookingId:other.Id);
            Verify(forbiddenContext is BadRequestObjectResult,"Client cannot reference another Client's booking");
            var clientB=As(db,second,"Client");
            Verify(await clientB.SendMessage(canonicalId,"Unauthorized access.") is ForbidResult,"Client cannot send to another Client's thread");
            Verify(await clientB.ReadStates(canonicalId) is ForbidResult,"Client cannot read another Client's receipt state");
            Verify(await clientB.MessagesSince(canonicalId) is ForbidResult,"Client cannot poll another Client's messages");

            var internalActions=As(db,staff,"Staff");
            var staffLink=await internalActions.StartBookingConversation(b2.Id) as RedirectToActionResult;
            Verify((int)staffLink!.RouteValues!["id"]! == canonicalId,"Staff booking shortcut opens same Client conversation");
            var internalView=await internalActions.Index(canonicalId) as ViewResult;
            Verify(((CommunicationCenterViewModel)internalView!.Model!).SelectedConversation!.BookingOptions.Count==2,"Staff sees two validated booking references");
            var clientView=await clientActions.Index(canonicalId,relatedBookingId:b2.Id) as ViewResult;
            var detail=((CommunicationCenterViewModel)clientView!.Model!).SelectedConversation!;
            Verify(detail.SelectedRelatedBookingId==b2.Id && detail.Messages.Count==2,"Client sees selected booking and complete shared message history");
            Verify(detail.Messages.All(x=>x.IsReadByOthers),"Seen reflects actual Staff read timestamp");

            // Legacy per-booking rows remain stored but are not shown as duplicate chats.
            var old=new Conversation {ConversationType=ConversationTypes.Booking,BookingId=b1.Id,
                CreatedByUserId=client.Id,Title="Legacy booking conversation"};
            db.Conversations.Add(old);await db.SaveChangesAsync();
            db.ConversationParticipants.AddRange(
                new ConversationParticipant{ConversationId=old.Id,UserId=client.Id},
                new ConversationParticipant{ConversationId=old.Id,UserId=staff.Id});
            await db.SaveChangesAsync();
            var inbox=await clientActions.Index(null,view:"inbox") as ViewResult;
            Verify(((CommunicationCenterViewModel)inbox!.Model!).Conversations.Count==1,"Client inbox displays one canonical thread, not legacy duplicates");
            var staffInbox=await internalActions.Index(null) as ViewResult;
            Verify(((CommunicationCenterViewModel)staffInbox!.Model!).Conversations.Count==1,"Staff inbox displays one thread per Client");
            Verify(await db.Conversations.AnyAsync(c=>c.Id==old.Id),"legacy conversations are retained, not deleted");

            // Verified payment older than 24h: cancellation action remains linked to the
            // canonical conversation with a message-specific booking reference.
            db.Payments.Add(new Payment{BookingId=b1.Id,Amount=2000m,ProofImagePath="synthetic.jpg",
                Status=PaymentStatus.Verified,InitialVerificationAtUtc=DateTime.UtcNow.AddHours(-48)});
            await db.SaveChangesAsync();
            var cancellation=await clientActions.RequestBookingCancellation(b1.Id,
                "Synthetic cancellation review request for testing.");
            Verify(cancellation is RedirectToActionResult,"eligible Client cancellation review request accepted");
            db.ChangeTracker.Clear();
            var updated=await db.Bookings.SingleAsync(b=>b.Id==b1.Id);
            var cancellationMessage=await db.Messages.SingleAsync(m=>m.Id==updated.CancellationRequestMessageId);
            Verify(cancellationMessage.ConversationId==canonicalId && cancellationMessage.RelatedBookingId==b1.Id,
                "cancellation review links to the correct booking within canonical thread");
            Verify(await db.Messages.AnyAsync(m=>m.Id==updated.CancellationRequestMessageId &&
                m.RelatedBookingId==b1.Id && m.Conversation!.SupportClientId==client.Id),
                "Staff cancellation review evidence remains valid");
            Console.WriteLine("UNIFIED_MESSAGING_PASS total_checks="+_checks+" disposable_db_only");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
