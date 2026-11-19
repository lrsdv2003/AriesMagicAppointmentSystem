using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Real Razor + authenticated routes, entirely isolated on GUID LocalDB.
// Covers the same shared Messaging UI for Client, Staff, and Owner.
internal static class UnifiedMessagingRedesignTests
{
    private static int checks;
    private static void Check(bool condition,string label)
    {
        if (!condition) throw new InvalidOperationException("FAILED Messaging Redesign: "+label);
        checks++; Console.WriteLine("PASS Messaging Redesign: "+label);
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(
        html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")
        .Groups[1].Value);
    private static async Task<HttpClient> LoginAsync(ReleaseFactory app,string email,string password)
    {
        var client=app.CreateClient(new WebApplicationFactoryClientOptions
        {AllowAutoRedirect=false,HandleCookies=true});
        var login=await client.GetStringAsync("/Account/Login");
        var token=Token(login);
        Check(token.Length>0,"Identity form antiforgery token present");
        using var response=await client.PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string,string>{
                ["Email"]=email,["Password"]=password,["__RequestVerificationToken"]=token
            }));
        Check(response.StatusCode==HttpStatusCode.Redirect,"authenticated "+email);
        return client;
    }
    private static async Task<(HttpStatusCode Status,string Html)> GetAsync(HttpClient client,string url)
    {
        using var response=await client.GetAsync(url);
        return (response.StatusCode,await response.Content.ReadAsStringAsync());
    }
    private static JsonDocument Json(string text)=>JsonDocument.Parse(text);
    private static int ItemCount(JsonElement root)=>root.GetProperty("items").GetArrayLength();

    public static async Task RunAsync()
    {
        Environment.SetEnvironmentVariable("BackgroundJobs__Enabled","false");
        Environment.SetEnvironmentVariable("Database__InitializeOnStartup","false");
        var instance=Environment.GetEnvironmentVariable("ARIES_TEST_LOCALDB_INSTANCE")??"MSSQLLocalDB";
        if(!Regex.IsMatch(instance,"^[A-Za-z][A-Za-z0-9_-]{0,63}$"))
            throw new InvalidOperationException("Invalid isolated SQL instance");
        var name="AriesMessagingRedesign_"+Guid.NewGuid().ToString("N");
        Check(Regex.IsMatch(name,"^AriesMessagingRedesign_[0-9a-f]{32}$"),"GUID isolated SQL database");
        var conn=$@"Server=(localdb)\{instance};Database={name};Integrated Security=True;TrustServerCertificate=True";
        var root=Path.Combine(Path.GetTempPath(),name);
        Directory.CreateDirectory(root);
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(conn).Options;
        var fixtureRoot=Environment.GetEnvironmentVariable("ARIES_UNIFIED_MESSAGING_FIXTURE_ROOT");
        if(!string.IsNullOrWhiteSpace(fixtureRoot))
        {
            fixtureRoot=Path.GetFullPath(fixtureRoot);
            if(!fixtureRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture output must stay under Temp");
            Directory.CreateDirectory(fixtureRoot);
        }
        async Task Fixture(string file,string html)
        {
            if(fixtureRoot!=null)await File.WriteAllTextAsync(Path.Combine(fixtureRoot,file+".html"),html);
        }
        await using var db=new ApplicationDbContext(options);
        await using var smtp=new SmtpFixture();smtp.Start();
        try
        {
            await db.Database.MigrateAsync();
            await using var app=new ReleaseFactory(conn,smtp.Port,new Uri("http://127.0.0.1:9"),root);
            using var scope=app.Services.CreateScope();
            var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(22));
            foreach(var role in new[]{"Client","Staff","Owner"})
                if(!await roles.RoleExistsAsync(role))
                    Check((await roles.CreateAsync(new IdentityRole(role))).Succeeded,
                        "isolated "+role+" role");
            async Task<ApplicationUser> Account(string key,string role)
            {
                var email="unified-"+key.Replace(" ","-").ToLowerInvariant()+"@example.test";
                var account=new ApplicationUser{
                    Email=email,UserName=email,EmailConfirmed=true,IsActive=true,
                    FullName="Synthetic "+key,PhoneNumber="09123456789"
                };
                Check((await users.CreateAsync(account,password)).Succeeded &&
                    (await users.AddToRoleAsync(account,role)).Succeeded,
                    "isolated "+key+" identity");
                return account;
            }
            var a=await Account("Client A","Client");
            var b=await Account("Client B","Client");
            var c=await Account("Client C","Client");
            var staff=await Account("Staff","Staff");
            var owner=await Account("Owner","Owner");
            var legacy=new User{FullName="Synthetic Client A",Email="legacy-messaging@example.test",
                Role="Client",PasswordHash="IDENTITY_MANAGED"};
            var service=new Service{Name="Synthetic Magic Package",Price=8500,DurationInHours=3};
            db.LegacyUsers.Add(legacy);db.Services.Add(service);
            await db.SaveChangesAsync();
            var future=DateTime.Today.AddDays(30);
            var booking=new Booking{
                ClientId=legacy.Id,ApplicationUserId=a.Id,ServiceId=service.Id,
                Service=service,Status=BookingStatus.Confirmed,
                CreatedAt=DateTime.Today.AddDays(-4),
                EventDate=future,StartTime=future.AddHours(13),EndTime=future.AddHours(16),
                EventType="Birthday",Motif="Blue",PartyVenue="Synthetic Hall in Rizal",
                ContactPerson="Synthetic Client A",ContactNumber="09123456789",
                PackageName=service.Name,BasePrice=service.Price,FinalPrice=service.Price,
                PaxCount=25,IsServiceable=true
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            var when=DateTime.UtcNow.AddMinutes(-12);
            var supportA=new Conversation{
                ConversationType=ConversationTypes.ClientSupport,SupportClientId=a.Id,
                Title="Aries Magic Support",CreatedByUserId=a.Id,CreatedAt=when,UpdatedAt=when.AddMinutes(2)
            };
            var supportB=new Conversation{
                ConversationType=ConversationTypes.ClientSupport,SupportClientId=b.Id,
                Title="Aries Magic Support",CreatedByUserId=b.Id,CreatedAt=when,UpdatedAt=when.AddMinutes(1)
            };
            var internalChat=new Conversation{
                ConversationType=ConversationTypes.InternalDirect,Title="Existing staff-owner coordination",
                CreatedByUserId=staff.Id,CreatedAt=when,UpdatedAt=when
            };
            db.Conversations.AddRange(supportA,supportB,internalChat);
            await db.SaveChangesAsync();
            foreach(var (conversationId,userId) in new []{
                (supportA.Id,a.Id),(supportA.Id,staff.Id),(supportA.Id,owner.Id),
                (supportB.Id,b.Id),(supportB.Id,staff.Id),
                (internalChat.Id,staff.Id),(internalChat.Id,owner.Id)})
                db.ConversationParticipants.Add(new ConversationParticipant{
                    ConversationId=conversationId,UserId=userId,JoinedAt=when
                });
            db.Messages.AddRange(
                new Message{ConversationId=supportA.Id,SenderId=a.Id,
                    MessageContent="Hello about my magic package.",SentAt=when.AddMinutes(1)},
                new Message{ConversationId=supportA.Id,SenderId=staff.Id,
                    MessageContent="Our team can help.",SentAt=when.AddMinutes(2),
                    RelatedBookingId=booking.Id},
                new Message{ConversationId=supportA.Id,SenderId=owner.Id,
                    MessageContent="Owner joins this case.",SentAt=when.AddMinutes(3)},
                new Message{ConversationId=supportB.Id,SenderId=b.Id,
                    MessageContent="PRIVATE-B-ONLY message",SentAt=when.AddMinutes(1)},
                new Message{ConversationId=internalChat.Id,SenderId=staff.Id,
                    MessageContent="Existing internal coordination remains.",SentAt=when.AddMinutes(1)}
            );
            await db.SaveChangesAsync();
            using var clientA=await LoginAsync(app,a.Email!,password);
            using var clientB=await LoginAsync(app,b.Email!,password);
            using var clientC=await LoginAsync(app,c.Email!,password);
            using var staffHttp=await LoginAsync(app,staff.Email!,password);
            using var ownerHttp=await LoginAsync(app,owner.Email!,password);
            using var guest=app.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
            using(var notSignedIn=await guest.GetAsync("/Communications/Index"))
                Check(notSignedIn.StatusCode!=HttpStatusCode.OK,"messaging requires authenticated role");
            using(var obsolete=await staffHttp.PostAsync("/Communications/CreateInternal",
                new FormUrlEncodedContent(new Dictionary<string,string>{{"title","Invalid manual conversation"}})))
                Check(obsolete.StatusCode==HttpStatusCode.NotFound ||
                      obsolete.StatusCode==HttpStatusCode.MethodNotAllowed,
                    "retired direct manual creation endpoint cannot accept POST");
            Check(await db.Conversations.CountAsync()==3,"obsolete POST created no conversation");

            foreach(var (role,http,id) in new[]{
                ("client",clientA,supportA.Id),("staff",staffHttp,supportA.Id),
                ("owner",ownerHttp,supportA.Id)
            })
            {
                var inbox=await GetAsync(http,"/Communications/Index?view=inbox");
                Check(inbox.Status==HttpStatusCode.OK &&
                    inbox.Html.Contains("comm-page comm-unified",StringComparison.Ordinal) &&
                    inbox.Html.Contains("comm-workspace",StringComparison.Ordinal) &&
                    inbox.Html.Contains("comm-conversation-search",StringComparison.Ordinal),
                    role+" uses identical shared inbox and search structure");
                Check(!inbox.Html.Contains("newConversationModal",StringComparison.Ordinal) &&
                    !inbox.Html.Contains("CreateInternal",StringComparison.Ordinal) &&
                    !inbox.Html.Contains("New Conversation",StringComparison.Ordinal),
                    role+" has no manual New Conversation UI");
                Check(!inbox.Html.Contains("comm-filter-strip",StringComparison.Ordinal) &&
                    inbox.Html.Contains("Select a conversation",StringComparison.Ordinal) &&
                    !inbox.Html.Contains("comm-has-selection",StringComparison.Ordinal),
                    role+" inbox has no filter bar and shows list-first state");
                await Fixture(role+"-inbox",inbox.Html);
                var selected=await GetAsync(http,"/Communications/Index?id="+id);
                Check(selected.Status==HttpStatusCode.OK &&
                    selected.Html.Contains("comm-has-selection",StringComparison.Ordinal) &&
                    selected.Html.Contains("id=\"commMessageHistory\"",StringComparison.Ordinal) &&
                    selected.Html.Contains("id=\"commComposer\"",StringComparison.Ordinal),
                    role+" selected conversation shares scroll area and bottom composer");
                Check(selected.Html.Contains("aria-label=\"Send message\"",StringComparison.Ordinal) &&
                    selected.Html.Contains("class=\"comm-booking-selector\"",StringComparison.Ordinal) &&
                    selected.Html.Contains("Message about",StringComparison.Ordinal) &&
                    selected.Html.Contains("name=\"relatedBookingId\"",StringComparison.Ordinal),
                    role+" retains accessible send action and verified booking context");
                Check(selected.Html.Contains("data-summaries-url",StringComparison.Ordinal) &&
                    selected.Html.Contains("data-messages-url",StringComparison.Ordinal) &&
                    selected.Html.Contains("id=\"commLatest\"",StringComparison.Ordinal),
                    role+" exposes authorized synchronization controls");
                Check(selected.Html.Contains("comm-message mine",StringComparison.Ordinal) &&
                    selected.Html.Contains("comm-message theirs",StringComparison.Ordinal),
                    role+" renders incoming/outgoing bubbles independently");
                Check(!selected.Html.Contains("New Conversation",StringComparison.Ordinal),
                    role+" selected view contains no manual creation controls");
                await Fixture(role+"-selected",selected.Html);
            }
            var listA=await GetAsync(clientA,"/Communications/ConversationSummaries");
            var listB=await GetAsync(clientB,"/Communications/ConversationSummaries");
            var listStaff=await GetAsync(staffHttp,"/Communications/ConversationSummaries");
            var listOwner=await GetAsync(ownerHttp,"/Communications/ConversationSummaries");
            using(var aJson=Json(listA.Html))
            using(var bJson=Json(listB.Html))
            using(var staffJson=Json(listStaff.Html))
            using(var ownerJson=Json(listOwner.Html))
            {
                Check(ItemCount(aJson.RootElement)==1 &&
                    aJson.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32()==supportA.Id,
                    "Client A summaries return only authorized support thread");
                Check(ItemCount(bJson.RootElement)==1 &&
                    bJson.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32()==supportB.Id,
                    "Client B summaries cannot see Client A messages");
                Check(ItemCount(staffJson.RootElement)==3,
                    "Staff sees only existing authorized support and internal threads");
                Check(ItemCount(ownerJson.RootElement)==2,
                    "Owner sees only specifically participated support/internal threads");
                Check(!listA.Html.Contains("PRIVATE-B-ONLY",StringComparison.Ordinal) &&
                    !listOwner.Html.Contains("PRIVATE-B-ONLY",StringComparison.Ordinal),
                    "private Client B preview never leaks to Client A or Owner");
                await Fixture("client-summary",listA.Html);
                await Fixture("staff-summary",listStaff.Html);
                await Fixture("owner-summary",listOwner.Html);
            }
            Check((await GetAsync(clientB,"/Communications/MessagesSince?id="+supportA.Id)).Status!=
                HttpStatusCode.OK,"message polling rejects another Client's conversation ID");
            Check((await GetAsync(clientB,"/Communications/ReadStates?id="+supportA.Id)).Status!=
                HttpStatusCode.OK,"read receipts reject another Client's conversation ID");
            Check((await GetAsync(ownerHttp,"/Communications/MessagesSince?id="+supportB.Id)).Status!=
                HttpStatusCode.OK,"Owner cannot poll an unjoined Client's support thread");
            Check((await GetAsync(clientA,"/Communications/MessagesSince?id="+supportA.Id)).Status==
                HttpStatusCode.OK,"authorized Client can poll own conversation");
            Check((await GetAsync(staffHttp,"/Communications/MessagesSince?id="+supportA.Id)).Status==
                HttpStatusCode.OK,"authorized Staff can poll participant conversation");

            // The retired manual Internal conversation route must not be callable
            // even by an authenticated Staff/Owner with a valid antiforgery token.
            foreach (var (actor, role) in new[] { (staffHttp, "Staff"), (ownerHttp, "Owner") })
            {
                var inboxHtml=(await GetAsync(actor,"/Communications/Index?view=inbox")).Html;
                using var direct=await actor.PostAsync("/Communications/CreateInternal",
                    new FormUrlEncodedContent(new Dictionary<string,string>{
                        ["participantIds"]=staff.Id,["initialMessage"]="Should never create a thread",
                        ["__RequestVerificationToken"]=Token(inboxHtml)
                    }));
                Check(direct.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                    role+" direct retired CreateInternal endpoint is unavailable");
            }

            var selectedA=(await GetAsync(clientA,"/Communications/Index?id="+supportA.Id)).Html;
            var tokenA=Token(selectedA);
            Check(tokenA.Length>0,"send composer includes antiforgery token");
            using(var noMessage=await clientA.PostAsync("/Communications/SendMessage",
                new FormUrlEncodedContent(new Dictionary<string,string>{
                    ["conversationId"]=supportA.Id.ToString(),["messageContent"]=" ",
                    ["__RequestVerificationToken"]=tokenA
                })))
                Check(noMessage.StatusCode==HttpStatusCode.BadRequest,
                    "server rejects empty message without creating a record");
            var beforeMessageCount=await db.Messages.CountAsync();
            using(var sent=await clientA.PostAsync("/Communications/SendMessage",
                new FormUrlEncodedContent(new Dictionary<string,string>{
                    ["conversationId"]=supportA.Id.ToString(),
                    ["messageContent"]="Sent from unified composer",
                    ["relatedBookingId"]=booking.Id.ToString(),
                    ["__RequestVerificationToken"]=tokenA
                })))
                Check(sent.StatusCode==HttpStatusCode.Redirect,
                    "Client composer posts to existing conversation successfully");
            Check(await db.Messages.CountAsync()==beforeMessageCount+1 &&
                await db.Messages.AnyAsync(m=>m.ConversationId==supportA.Id &&
                    m.MessageContent=="Sent from unified composer" && m.RelatedBookingId==booking.Id),
                "send creates exactly one correctly associated message");
            Check(await db.Notifications.AnyAsync(n=>n.UserId==staff.Id &&
                n.Link=="/Communications/Index/"+supportA.Id),
                "existing message notification workflow preserved");
            var updated=await GetAsync(staffHttp,"/Communications/ConversationSummaries");
            using(var updatedJson=Json(updated.Html))
                Check(updatedJson.RootElement.GetProperty("items")[0]
                    .GetProperty("latestMessage").GetString()=="Sent from unified composer",
                    "authorized sidebar summaries refresh latest preview and order");
            Check(await db.Conversations.CountAsync()==3,
                "message send/summaries do not create duplicate threads");

            var noConversations=await GetAsync(clientC,"/Communications/Index?view=inbox");
            Check(noConversations.Status==HttpStatusCode.OK &&
                noConversations.Html.Contains("No conversations yet",StringComparison.Ordinal) &&
                noConversations.Html.Contains("Contact Aries Magic Support",StringComparison.Ordinal) &&
                !noConversations.Html.Contains("New Conversation",StringComparison.Ordinal),
                "Client without a thread sees legitimate support provisioning, not manual creation");
            await Fixture("client-empty",noConversations.Html);
            using(var support=await clientC.PostAsync("/Communications/StartSupport",
                new FormUrlEncodedContent(new Dictionary<string,string>{
                    ["__RequestVerificationToken"]=Token(noConversations.Html)
                })))
                Check(support.StatusCode==HttpStatusCode.Redirect,
                    "authorized Client support provision remains functional");
            Check(await db.Conversations.AnyAsync(x=>x.SupportClientId==c.Id) &&
                await db.ConversationParticipants.AnyAsync(x=>x.UserId==staff.Id &&
                    x.Conversation!.SupportClientId==c.Id),
                "canonical support thread joins existing Staff without manual endpoint");
            Check(await db.Conversations.CountAsync()==4,
                "only deliberate canonical support provision creates new thread");

            var countBefore=await db.Conversations.CountAsync();
            await clientA.GetAsync("/Communications/ConversationSummaries?q=NoMatch");
            await ownerHttp.GetAsync("/Communications/ConversationSummaries?q=NoMatch");
            Check(await db.Conversations.CountAsync()==countBefore,
                "searching and refreshing do not create conversations");
            Console.WriteLine($"UNIFIED_MESSAGING_REDESIGN_PASS: {checks} isolated MVC/SQL checks");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
