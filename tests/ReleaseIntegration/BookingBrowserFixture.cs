using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Local test executable only. Actual controllers, isolated SQL and fake loopback external services.
static class BookingBrowserFixture
{
 public static async Task RunAsync(bool manualPreview = false, bool hubPreview = false, bool midnightPreview = false, bool releaseReview = false)
 {
  hubPreview |= midnightPreview || releaseReview;
  var fullPreview=manualPreview||hubPreview;
  var root=Directory.GetCurrentDirectory();var statePath=Path.Combine(root,releaseReview?".tmp-owner-build/unified-client-fixture.json":midnightPreview?".tmp-owner-build/midnight-client-fixture.json":hubPreview?".tmp-owner-build/phase7-client-hub-fixture.json":manualPreview?".tmp-owner-build/manual-ui-preview.json":".tmp-owner-build/phase6-booking-fixture.json");
  var baseUrl=releaseReview?"http://127.0.0.1:5310":midnightPreview?"http://127.0.0.1:5299":hubPreview?"http://127.0.0.1:5298":manualPreview?"http://127.0.0.1:5295":"http://127.0.0.1:5297";
  if(File.Exists(statePath))throw new InvalidOperationException("Stop the previous booking fixture first.");
  var database="AriesBookingVisual_"+Guid.NewGuid().ToString("N");
  var connection=@"Server=(localdb)\MSSQLLocalDB;Database="+database+";Integrated Security=True;TrustServerCertificate=True";
  var directory=Path.Combine(root,".tmp-owner-build",database);Directory.CreateDirectory(directory);
  await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
  await using var smtp=new SmtpFixture();smtp.Start();
  var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName=fullPreview?"Development":"BookingUiFixture",ContentRootPath=directory});builder.Logging.ClearProviders();
  await using var proxy=builder.Build();proxy.Urls.Add(baseUrl);
  var mapBuilder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="BookingUiFixture",ContentRootPath=directory});mapBuilder.Logging.ClearProviders();
  await using var map=mapBuilder.Build();map.Urls.Add("http://127.0.0.1:0");
  map.MapGet("/search",(string? q)=>q?.ToLowerInvariant().Contains("failure")==true?Results.StatusCode(503):q?.ToLowerInvariant().Contains("unknown")==true?Results.Json(Array.Empty<object>()):Results.Json(new[]{new{lat=q?.ToLowerInvariant().Contains("distant")==true?"50":"14.58125",lon="121.1258",display_name=q?.ToLowerInvariant().Contains("distant")==true?"Distant synthetic venue":"Synthetic event venue, Philippines"}}));
  await map.StartAsync();
  var mapUrl=new Uri(map.Urls.Single());
  try {
   await db.Database.MigrateAsync();
   await using var factory=new ReleaseFactory(connection,smtp.Port,mapUrl,directory,authUi:true);
   using var scope=factory.Services.CreateScope();await IdentitySeeder.SeedAsync(scope.ServiceProvider);
   var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
   var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
   var client=new ApplicationUser{UserName="client@example.test",Email="client@example.test",EmailConfirmed=true,FullName="Synthetic client",PhoneNumber="09123456789"};
   foreach(var pair in new[]{(client,"Client"),(new ApplicationUser{UserName="owner@example.test",Email="owner@example.test",EmailConfirmed=true,FullName="Synthetic Owner"},"Owner"),(new ApplicationUser{UserName="staff@example.test",Email="staff@example.test",EmailConfirmed=true,FullName="Synthetic Staff"},"Staff"),(new ApplicationUser{UserName="other@example.test",Email="other@example.test",EmailConfirmed=true,FullName="Other synthetic Client"},"Client")})
    if(!(await users.CreateAsync(pair.Item1,password)).Succeeded||!(await users.AddToRoleAsync(pair.Item1,pair.Item2)).Succeeded)throw new Exception("Test identity creation failed.");
   await ServiceSeeder.SeedAsync(db);
   var packages=await db.Services.Include(s=>s.Inclusions).OrderBy(s=>s.Price).ToListAsync();
   // Deliberately technical inclusion rows, never presented as official catalog content.
   foreach(var package in packages)for(var i=1;i<=4;i++)db.ServiceInclusions.Add(new(){ServiceId=package.Id,Name="Synthetic inclusion "+i,IsRemovable=i>1,DeductionAmount=2000});
   var legacy=new User{FullName="Synthetic client",Email=client.Email!,PasswordHash="IDENTITY_MANAGED",Role="Client"};db.LegacyUsers.Add(legacy);await db.SaveChangesAsync();
   Booking NewBooking(int offset,string status,int hour=10)=>new(){ClientId=legacy.Id,ApplicationUserId=client.Id,ServiceId=packages[0].Id,PackageName=packages[0].Name,BasePrice=packages[0].Price,FinalPrice=packages[0].Price,EventDate=DateTime.Today.AddDays(offset),StartTime=DateTime.Today.AddDays(offset).AddHours(hour),EndTime=DateTime.Today.AddDays(offset).AddHours(hour+packages[0].DurationInHours),Status=status,EventType="Company Events",Motif="Champagne",PartyVenue="Synthetic event venue, Philippines",PaxCount=50,ContactPerson="Synthetic client",ContactNumber="09123456789",IsServiceable=true};
   var payable=NewBooking(30,BookingStatus.AwaitingDownpayment);var confirmed=NewBooking(32,BookingStatus.AwaitingDownpayment);db.Bookings.AddRange(payable,confirmed);
   for(var i=0;i<3;i++)db.Bookings.Add(NewBooking(40,BookingStatus.Confirmed,2+i*7));
   db.BlockedDates.Add(new(){Date=DateTime.Today.AddDays(41),Reason="Synthetic blocked-date test"});await db.SaveChangesAsync();
   var hub=hubPreview?await ClientHubFixtureSeed.AddAsync(db,users,client,password,legacy,packages[0],confirmed,directory):null;
   var environment=scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>().EnvironmentName;
   var effectiveConnection=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetDbConnection();
   if(environment!="Development"||effectiveConnection.DataSource!=@"(localdb)\MSSQLLocalDB"||effectiveConnection.Database!=database)throw new InvalidOperationException("Preview isolation verification failed.");
   if(releaseReview) {
    var catalog=await db.Services.AsNoTracking().Where(s=>!s.IsArchived).OrderBy(s=>s.Price)
     .Select(s=>new{s.Id,s.Name,s.Price,s.DurationInHours,s.Description,Inclusions=s.Inclusions.OrderBy(i=>i.Id).Select(i=>new{i.Id,i.Name}).ToList()}).ToListAsync();
    await File.WriteAllTextAsync(Path.Combine(root,".tmp-owner-build/unified-public-catalog.json"),JsonSerializer.Serialize(new{Source="Isolated synthetic technical inclusions; not authoritative commercial offers",Packages=catalog}));
   }
   await File.WriteAllTextAsync(statePath,JsonSerializer.Serialize(new{Pid=Environment.ProcessId,Directory=directory,BaseUrl=baseUrl,Environment=environment,Runtime=Environment.Version.ToString(),Database=database,Hub=hub,Email=client.Email,Password=password,PayableId=payable.Id,ConfirmedId=confirmed.Id,FullDate=DateTime.Today.AddDays(40).ToString("yyyy-MM-dd"),BlockedDate=DateTime.Today.AddDays(41).ToString("yyyy-MM-dd"),AvailableDate=DateTime.Today.AddDays(50).ToString("yyyy-MM-dd"),RescheduleDate=DateTime.Today.AddDays(52).ToString("yyyy-MM-dd")}));
   using var http=new HttpClient(factory.Server.CreateHandler()){BaseAddress=new Uri("http://localhost")};
   proxy.Run(async context=>{
    var path=context.Request.Path.Value??"/";
    var allowed=path=="/"||path=="/favicon.ico"||new[]{"/Account/","/Home/","/Bookings/","/Payments/","/RescheduleRequests/","/css/","/js/","/lib/","/fonts/","/images/"}.Any(path.StartsWith)||path.EndsWith(".styles.css")||path=="/Communications/UnreadCount";
    if(!fullPreview&&!allowed){context.Response.StatusCode=404;return;}
    using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),context.Request.Path+context.Request.QueryString);
    if(context.Request.ContentLength>0){using var memory=new MemoryStream();await context.Request.Body.CopyToAsync(memory);request.Content=new ByteArrayContent(memory.ToArray());}
    foreach(var header in context.Request.Headers){if(header.Key is "Host" or "Connection")continue;if(!request.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray()))request.Content?.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray());}
    request.Headers.Host=context.Request.Host.Value;
    using var response=await http.SendAsync(request);context.Response.StatusCode=(int)response.StatusCode;
    foreach(var header in response.Headers.Concat(response.Content.Headers)){if(header.Key.Equals("Transfer-Encoding",StringComparison.OrdinalIgnoreCase)||header.Key.Equals("Content-Length",StringComparison.OrdinalIgnoreCase))continue;context.Response.Headers[header.Key]=header.Value.ToArray();}
    await context.Response.Body.WriteAsync(await response.Content.ReadAsByteArrayAsync());
   });
   var stop=Path.Combine(directory,"stop");var watcher=Task.Run(async()=>{while(!proxy.Lifetime.ApplicationStopping.IsCancellationRequested){if(File.Exists(stop)){proxy.Lifetime.StopApplication();break;}await Task.Delay(150);}});
   // Private local mailbox for manual registration/recovery. No HTTP diagnostics or real email delivery.
   var mailbox=Path.Combine(directory,"manual-mailbox.json");
   var mailWatcher=fullPreview?Task.Run(async()=>{while(!proxy.Lifetime.ApplicationStopping.IsCancellationRequested){
    var messages=smtp.Messages.Where(m=>m.To.Mailboxes.Any(a=>a.Address.EndsWith("@example.test",StringComparison.OrdinalIgnoreCase))).Select(m=>new{To=m.To.Mailboxes.Select(a=>a.Address).ToArray(),m.Subject,Code=Regex.Match(m.HtmlBody??"",@">(\d{6})</div>").Groups[1].Value,Link=WebUtility.HtmlDecode(Regex.Match(m.HtmlBody??"","href='([^']+)'",RegexOptions.IgnoreCase).Groups[1].Value)}).ToArray();
    await File.WriteAllTextAsync(mailbox+".pending",JsonSerializer.Serialize(messages));File.Move(mailbox+".pending",mailbox,true);await Task.Delay(500);
   }}):Task.CompletedTask;
   Console.WriteLine($"ISOLATED DEVELOPMENT UI {baseUrl} runtime {Environment.Version}; environment {environment}; PID {Environment.ProcessId}; stop marker {stop}");await proxy.RunAsync();await Task.WhenAll(watcher,mailWatcher);
  } finally {
   if(!Regex.IsMatch(database,"^AriesBookingVisual_[0-9a-f]{32}$"))throw new Exception("Unsafe teardown target.");
   await db.Database.EnsureDeletedAsync();if(File.Exists(statePath))File.Delete(statePath);
   if(fullPreview)foreach(var name in new[]{"manual-mailbox.json","manual-mailbox.json.pending"}){var file=Path.Combine(directory,name);if(File.Exists(file))File.Delete(file);}
  }
 }
}
