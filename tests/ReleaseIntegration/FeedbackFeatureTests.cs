using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

static class FeedbackFeatureTests
{
    static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    static async Task<string> GetToken(HttpClient client,string path) {
        using var response=await client.GetAsync(path);var html=await response.Content.ReadAsStringAsync();
        if(response.StatusCode!=HttpStatusCode.OK||Token(html)=="")throw new Exception("Feedback fixture token failed: "+path+" "+response.StatusCode);
        return Token(html);
    }
    static Task<HttpResponseMessage> Post(HttpClient c,string path,string token,params (string,string)[] values)=>c.PostAsync(path,
        new FormUrlEncodedContent(values.Select(v=>new KeyValuePair<string,string>(v.Item1,v.Item2)).Append(new("__RequestVerificationToken",token))));
    static async Task<HttpClient> Login(ReleaseFactory factory,string email,string password) {
        var client=factory.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true});
        var token=await GetToken(client,"/Account/Login");
        using var response=await Post(client,"/Account/Login",token,("Email",email),("Password",password));
        if(response.StatusCode!=HttpStatusCode.Redirect)throw new Exception("Feedback fixture login failed.");
        return client;
    }
    public static async Task RunAsync(string connection,int smtpPort,Uri mapUrl,string root,Dictionary<string,string> passwords,string otherPassword,Action<bool,string> check)
    {
        await using var factory=new ReleaseFactory(connection,smtpPort,mapUrl,root,cleanFeedbackMedia:true);
        using var client=await Login(factory,"client@example.test",passwords["Client"]);
        using var staff=await Login(factory,"staff@example.test",passwords["Staff"]);
        using var other=await Login(factory,"otherclient@example.test",otherPassword);
        using var owner=await Login(factory,"owner@example.test",passwords["Owner"]);
        using var anonymous=factory.CreateClient(new(){AllowAutoRedirect=false});
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var store=scope.ServiceProvider.GetRequiredService<FeedbackMediaService>();
        var clientId=await db.Users.Where(u=>u.Email=="client@example.test").Select(u=>u.Id).SingleAsync();
        var original=await db.Bookings.AsNoTracking().FirstAsync();
        Booking Make(string state=BookingStatus.Completed,bool locked=true,int days=-3)=>new() {ClientId=original.ClientId,ApplicationUserId=clientId,
            ServiceId=original.ServiceId,Status=state,IsCompletedLocked=locked,EventDate=FeedbackPolicy.BusinessNow.Date.AddDays(days),
            StartTime=FeedbackPolicy.BusinessNow.Date.AddDays(days).AddHours(10),EndTime=FeedbackPolicy.BusinessNow.Date.AddDays(days).AddHours(12),
            PackageName="Feedback fixture event",EventType="Birthday",PartyVenue="Private venue marker",ContactPerson="Private contact marker",ContactNumber="09000000000"};
        var completed=Make();var canceled=Make(BookingStatus.Cancelled);var pending=Make(BookingStatus.Pending);var future=Make(days:2);var unlocked=Make(locked:false);
        db.Bookings.AddRange(completed,canceled,pending,future,unlocked);await db.SaveChangesAsync();
        var token=await GetToken(client,"/Feedback/Create?bookingId="+completed.Id);
        var uploadOptions=scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FeedbackMediaOptions>>().Value;
        check(!new FeedbackMediaOptions().CanUpload,"Feedback uploads default disabled");
        uploadOptions.UploadsEnabled=false;
        var disabledPage=await client.GetStringAsync("/Feedback/Create?bookingId="+completed.Id);
        await System.IO.File.WriteAllTextAsync(".tmp-owner-build/feedback-uploads-disabled.synthetic.html",disabledPage);
        check(disabledPage.Contains("temporarily unavailable")&&!Regex.IsMatch(disabledPage,"<input[^>]*name=\"Files\""),"Disabled feedback notice replaces feedback file picker");
        foreach(var video in new[]{false,true}) {
            var file=new FormFile(new MemoryStream([1]),0,1,"Files",video?"event.mp4":"event.jpg");
            try {if(video)await store.PrepareVideoAsync(file,CancellationToken.None);else await store.PrepareAsync(file,CancellationToken.None);check(false,"Disabled service must reject");}
            catch(FeedbackUploadException ex){check(ex.Message==FeedbackMediaOptions.UploadsUnavailableMessage,"Disabled service rejects before file access or scan");}
        }
        using(var forged=new MultipartFormDataContent()) {
            forged.Add(new StringContent(token),"__RequestVerificationToken");forged.Add(new StringContent(completed.Id.ToString()),"BookingId");
            foreach(var rating in new[]{"OverallRating","PerformanceRating","BookingRating"})forged.Add(new StringContent("5"),rating);
            forged.Add(new StringContent("Private"),"SharingPreference");forged.Add(new ByteArrayContent([1]),"Files","forged.jpg");
            using var blocked=await client.PostAsync("/Feedback/Create",forged);var body=await blocked.Content.ReadAsStringAsync();
            check(body.Contains("temporarily unavailable")&&!await db.EventFeedbacks.AnyAsync(f=>f.BookingId==completed.Id),"Forged disabled upload cannot create feedback");
        }
        var textOnly=Make();db.Bookings.Add(textOnly);await db.SaveChangesAsync();
        var textToken=await GetToken(client,"/Feedback/Create?bookingId="+textOnly.Id);
        using(var response=await Post(client,"/Feedback/Create",textToken,("BookingId",textOnly.Id.ToString()),("OverallRating","5"),("PerformanceRating","5"),("BookingRating","5"),("Experience","Synthetic text feedback"),("SharingPreference","Private")))
            check(response.StatusCode==HttpStatusCode.Redirect&&await db.EventFeedbacks.AnyAsync(f=>f.BookingId==textOnly.Id&&!f.TextConsent&&!f.Media.Any()),"Disabled uploads preserve private text feedback and consent");
        uploadOptions.UploadsEnabled=true;var scannerHost=uploadOptions.ClamAvHost;uploadOptions.ClamAvHost=null;
        check(!uploadOptions.CanUpload,"Enable without configured scanner remains unavailable");uploadOptions.ClamAvHost=scannerHost;
        var staffToken=await GetToken(staff,"/Testimonials");
        var otherToken=await GetToken(other,"/Feedback");
        foreach(var b in new[]{canceled,pending,future,unlocked}) {
            using var response=await client.GetAsync("/Feedback/Create?bookingId="+b.Id);
            check(response.StatusCode==HttpStatusCode.BadRequest,"Feedback rejects canceled, pending, future, or unlocked completion "+b.Status+"/"+b.IsCompletedLocked+"/"+b.EventDate.Date);
        }
        using(var response=await other.GetAsync("/Feedback/Create?bookingId="+completed.Id))check(response.StatusCode==HttpStatusCode.NotFound,"Feedback form excludes another client's booking");
        using(var response=await client.GetAsync("/Testimonials"))check(response.StatusCode==HttpStatusCode.Redirect,"Client cannot access Staff testimonial management");
        using(var response=await owner.GetAsync("/Testimonials"))check(response.StatusCode==HttpStatusCode.Redirect,"Owner is not implicitly granted private Staff feedback access");
        using(var response=await anonymous.GetAsync("/Feedback"))check(response.StatusCode==HttpStatusCode.Redirect,"Anonymous visitor cannot read private feedback history");
        byte[] Image(SKEncodedImageFormat format) {using var bitmap=new SKBitmap(64,48);bitmap.Erase(new SKColor(90,40,60));using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(format,90);return data.ToArray();}
        var jpeg=Image(SKEncodedImageFormat.Jpeg);var png=Image(SKEncodedImageFormat.Png);var webp=Image(SKEncodedImageFormat.Webp);
        FormFile File(byte[] data,string name,string mime) => new(new MemoryStream(data),0,data.Length,"Files",name){Headers=new HeaderDictionary(),ContentType=mime};
        byte[] Mp4()
        {
            byte[] Box(string type,byte[] body){var result=new byte[8+body.Length];System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result,(uint)result.Length);Encoding.ASCII.GetBytes(type).CopyTo(result,4);body.CopyTo(result,8);return result;}
            var ftyp=Box("ftyp",Encoding.ASCII.GetBytes("isom\0\0\0\0mp42"));
            var hdlr=Box("hdlr",Encoding.ASCII.GetBytes("\0\0\0\0\0\0\0\0vide\0\0\0\0\0\0\0\0"));
            var moov=Box("moov",Box("trak",Box("mdia",hdlr)));
            var mdat=Box("mdat",[1,2,3,4]);
            return ftyp.Concat(moov).Concat(mdat).ToArray();
        }
        foreach(var file in new[]{File(jpeg,"photo.jpg","image/jpeg"),File(png,"photo.png","image/png"),File(webp,"photo.webp","image/webp")}) {
            var processed=await store.PrepareAsync(file,CancellationToken.None);
            var bytes=await store.ReadAsync(processed.StorageKey,CancellationToken.None);
            using var data=SKData.CreateCopy(bytes);using var codec=SKCodec.Create(data);
            check(processed.ProcessingStatus=="Ready"&&processed.ScannedAtUtc!=null&&codec.EncodedFormat==SKEncodedImageFormat.Jpeg&&processed.Width==64,"Native image decoder accepts and re-encodes "+file.ContentType);
            store.Delete(processed.StorageKey);
        }
        var preparedVideo=await store.PrepareVideoAsync(File(Mp4(),"event.mp4","video/mp4"),CancellationToken.None);
        check(preparedVideo.Kind=="Video"&&preparedVideo.ContentType=="video/mp4"&&preparedVideo.ProcessingStatus=="Ready"&&preparedVideo.StorageKey.EndsWith(".mp4"),"Valid, scanned MP4 is stored with a random private media key");
        check((await store.ReadAsync(preparedVideo.StorageKey,CancellationToken.None)).SequenceEqual(Mp4()),"Private MP4 processing preserves validated source bytes");
        store.Delete(preparedVideo.StorageKey);
        foreach(var file in new[]{
            File(jpeg,"photo.svg","image/jpeg"),File("MZ executable"u8.ToArray(),"photo.jpg","image/jpeg"),
            File(jpeg,"photo.jpg","image/png"),File(png[..12].Concat("<script>bad</script>"u8.ToArray()).ToArray(),"photo.png","image/png"),
            File(jpeg,"../escape.jpg","image/jpeg"),File(jpeg,"..\\escape.jpg","image/jpeg"),
            File(jpeg,"photo.mp4","video/mp4"),File(new byte[10*1024*1024+1],"large.jpg","image/jpeg")}) {
            var rejected=false;try{await store.PrepareAsync(file,CancellationToken.None);}catch(FeedbackUploadException){rejected=true;}
            check(rejected,"Unsafe/unsupported media rejected: "+file.FileName+"/"+file.ContentType);
        }
        async Task<HttpResponseMessage> Submit(Booking b,bool consent=true,string experience="A memorable fixture celebration.",string suggestions="PRIVATE SUGGESTIONS MARKER",bool photo=true,int overall=5,bool video=false,bool omitSharingChoice=false,string? sharingPreference=null) {
            var form=new MultipartFormDataContent();
            var fields=new Dictionary<string,string>{{"BookingId",b.Id.ToString()},{"OverallRating",overall.ToString()},{"PerformanceRating","4"},{"BookingRating","5"},{"Experience",experience},{"Suggestions",suggestions},
                {"TextConsent","true"},{"NameConsent",consent.ToString()},{"PublicDisplayName","Consented first name"},{"MediaConsent",consent.ToString()},{"__RequestVerificationToken",token}};
            if(!omitSharingChoice)fields["SharingPreference"]=sharingPreference??(consent?"Public":"Private");
            foreach(var pair in fields)
                form.Add(new StringContent(pair.Value),pair.Key);
            // Overposted privileged fields must never bind.
            form.Add(new StringContent("true"),"Featured");form.Add(new StringContent("Approved"),"ModerationStatus");
            form.Add(new StringContent("forged-owner"),"ClientId");
            if(photo){var content=new ByteArrayContent(png);content.Headers.ContentType=new("image/png");form.Add(content,"Files","event.png");}
            if(video){var content=new ByteArrayContent(Mp4());content.Headers.ContentType=new("video/mp4");form.Add(content,"Files","event.mp4");}
            return await client.PostAsync("/Feedback/Create",form);
        }
        var noChoice=Make();db.Bookings.Add(noChoice);await db.SaveChangesAsync();
        using(var response=await Submit(noChoice,consent:false,photo:false,omitSharingChoice:true))check(response.StatusCode==HttpStatusCode.OK&&!await db.EventFeedbacks.AnyAsync(f=>f.BookingId==noChoice.Id),"Feedback cannot submit until Public or Private sharing preference is explicitly selected");
        using(var response=await Submit(completed,video:true))check(response.StatusCode==HttpStatusCode.Redirect,"Client feedback, image, and private video persist through real multipart MVC request");
        var feedback=await db.EventFeedbacks.AsNoTracking().Include(f=>f.Media).SingleAsync(f=>f.BookingId==completed.Id);
        check(feedback.ClientId==clientId&&!feedback.Featured&&feedback.ModerationStatus=="Pending"&&feedback.SharingPreference=="Public"&&feedback.TextConsent&&feedback.Media.Count==2&&feedback.Media.Any(m=>m.Kind=="Image")&&feedback.Media.Any(m=>m.Kind=="Video"),"Server ignores forged consent/ownership fields and records explicit sharing choice with validated image/video");
        using(var response=await client.GetAsync("/Feedback/Details/"+feedback.Id))check(response.StatusCode==HttpStatusCode.OK&&(await response.Content.ReadAsStringAsync()).Contains("PRIVATE SUGGESTIONS MARKER"),"Client can review their own full feedback");
        using(var response=await other.GetAsync("/Feedback/Details/"+feedback.Id))check(response.StatusCode==HttpStatusCode.NotFound,"Other client cannot read feedback details");
        var mediaId=feedback.Media.Single(m=>m.Kind=="Image").Id;
        var videoMediaId=feedback.Media.Single(m=>m.Kind=="Video").Id;
        using(var response=await client.GetAsync("/FeedbackMedia/Private/"+mediaId))check(response.StatusCode==HttpStatusCode.OK&&response.Headers.TryGetValues("X-Content-Type-Options",out var headers)&&headers.Contains("nosniff"),"Owner client can read scanned media through a nosniff endpoint");
        using(var response=await client.GetAsync("/FeedbackMedia/Private/"+videoMediaId))check(response.StatusCode==HttpStatusCode.OK&&response.Content.Headers.ContentType?.MediaType=="video/mp4"&&response.Headers.TryGetValues("X-Content-Type-Options",out var videoHeaders)&&videoHeaders.Contains("nosniff"),"Client receives scanned private video through a protected video/mp4 endpoint");
        using(var response=await other.GetAsync("/FeedbackMedia/Private/"+mediaId))check(response.StatusCode==HttpStatusCode.NotFound,"Other client cannot read private media");
        using(var response=await anonymous.GetAsync("/FeedbackMedia/Public/"+mediaId))check(response.StatusCode==HttpStatusCode.NotFound,"Unapproved media is not public");
        using(var response=await Submit(completed))check(response.StatusCode==HttpStatusCode.Redirect&&await db.EventFeedbacks.CountAsync(f=>f.BookingId==completed.Id)==1,"Duplicate submission redirects to the single existing record");
        async Task<HttpResponseMessage> Review(int id,string action,string? version=null,bool confirm=true,bool chooseMedia=false) {
            var current=await db.EventFeedbacks.AsNoTracking().SingleAsync(f=>f.Id==id);
            var values=new List<(string,string)>{("Id",id.ToString()),("Version",version??Convert.ToBase64String(current.Version)),("Decision",action),("ConfirmPublication",confirm.ToString()),("Reason","Fixture privacy review"),
                ("OverallRating","1"),("Experience","Staff overwrite attempt"),("Suggestions","Changed private answer")};
            if(chooseMedia)values.Add(("PublicMediaIds",mediaId.ToString()));
            return await Post(staff,"/Testimonials/Review",staffToken,values.ToArray());
        }
        using(var response=await Review(feedback.Id,"Approve",confirm:false))check(response.StatusCode==HttpStatusCode.BadRequest,"Publishing requires explicit moderation confirmation");
        using(var response=await Review(feedback.Id,"Approve",chooseMedia:true))check(response.StatusCode==HttpStatusCode.Redirect,"Staff approves consented testimonial and ready media");
        check(await db.EventFeedbacks.AsNoTracking().AnyAsync(f=>f.Id==feedback.Id&&f.OverallRating==5&&f.Experience=="A memorable fixture celebration."&&f.Suggestions=="PRIVATE SUGGESTIONS MARKER"),"Staff moderation cannot overpost ratings or written answers");
        var approved=await db.EventFeedbacks.AsNoTracking().SingleAsync(f=>f.Id==feedback.Id);
        var stale=Convert.ToBase64String(approved.Version);
        using(var response=await Review(feedback.Id,"Feature",chooseMedia:true))check(response.StatusCode==HttpStatusCode.Redirect,"Staff features an approved testimonial");
        using(var response=await Review(feedback.Id,"Unfeature",version:stale))check(response.StatusCode==HttpStatusCode.Conflict,"Stale rowversion cannot overwrite a newer moderation decision");
        using(var response=await anonymous.GetAsync("/")) {
            var html=await response.Content.ReadAsStringAsync();
            check(html.Contains("A memorable fixture celebration.")&&html.Contains("Consented first name"),"Public homepage displays the real approved consented story");
            check(!html.Contains("PRIVATE SUGGESTIONS MARKER")&&!html.Contains("Private venue marker")&&!html.Contains("client@example.test"),"Public projection excludes suggestions, venue, and account email");
        }
        using(var response=await anonymous.GetAsync("/FeedbackMedia/Public/"+mediaId))check(response.StatusCode==HttpStatusCode.OK&&!response.Headers.CacheControl!.Public&&response.Headers.CacheControl.NoStore,"Only published selected media is served with no-store");
        using(var response=await anonymous.GetAsync("/Home/About")) {
            var html=await response.Content.ReadAsStringAsync();
            check(response.StatusCode==HttpStatusCode.OK&&response.Headers.CacheControl?.NoStore==true&&html.Contains("A memorable fixture celebration.")&&html.Contains("Consented first name"),"About displays only real approved consented feedback with no-store");
            check(!html.Contains("PRIVATE SUGGESTIONS MARKER")&&!html.Contains("Private venue marker")&&!html.Contains("client@example.test"),"About public feedback excludes private suggestions, venue and email");
        }
        using(var response=await Review(feedback.Id,"Unfeature"))check(response.StatusCode==HttpStatusCode.Redirect,"Unfeature action succeeds");
        using(var response=await anonymous.GetAsync("/Home/About"))check(!(await response.Content.ReadAsStringAsync()).Contains("A memorable fixture celebration."),"Unfeatured feedback disappears from About");
        using(var response=await anonymous.GetAsync("/"))check(!(await response.Content.ReadAsStringAsync()).Contains("A memorable fixture celebration."),"Unfeatured content disappears from the showcase");
        using(var response=await Review(feedback.Id,"Feature",chooseMedia:true))check(response.StatusCode==HttpStatusCode.Redirect,"Approved content can be featured again before withdrawal");
        var withdrawalRace=await Task.WhenAll(Post(client,"/Feedback/Withdraw",token,("id",feedback.Id.ToString())),Review(feedback.Id,"Feature",chooseMedia:true));
        check(withdrawalRace[0].StatusCode==HttpStatusCode.Redirect,"Client withdrawal succeeds despite concurrent moderation");
        check(withdrawalRace[1].StatusCode is HttpStatusCode.Redirect or HttpStatusCode.BadRequest or HttpStatusCode.Conflict,"Concurrent feature action resolves safely");
        foreach(var result in withdrawalRace)result.Dispose();
        using(var response=await anonymous.GetAsync("/FeedbackMedia/Public/"+mediaId))check(response.StatusCode==HttpStatusCode.NotFound,"Withdrawal immediately blocks public media");
        using(var response=await anonymous.GetAsync("/Home/About"))check(!(await response.Content.ReadAsStringAsync()).Contains("A memorable fixture celebration."),"Withdrawn feedback disappears from About immediately");
        using(var response=await Review(feedback.Id,"Feature",chooseMedia:true))check(response.StatusCode==HttpStatusCode.BadRequest,"Staff cannot override withdrawn consent");
        check(await db.FeedbackPublicationEvents.CountAsync(e=>e.FeedbackId==feedback.Id)>=5,"Publication and withdrawal history persists with actor and timestamp");
        var privateBooking=Make();db.Bookings.Add(privateBooking);await db.SaveChangesAsync();
        using(var response=await Submit(privateBooking,consent:false,photo:false))check(response.StatusCode==HttpStatusCode.Redirect,"Feedback submission is saved privately when the Client explicitly chooses Private");
        var privateFeedback=await db.EventFeedbacks.AsNoTracking().SingleAsync(f=>f.BookingId==privateBooking.Id);
        check(privateFeedback.SharingPreference=="Private"&&!privateFeedback.TextConsent,"Forged legacy TextConsent cannot override the explicit private choice");
        using(var response=await Review(privateFeedback.Id,"Approve"))check(response.StatusCode==HttpStatusCode.BadRequest,"Staff cannot approve a private-only submission");
        var unsafeBooking=Make();db.Bookings.Add(unsafeBooking);await db.SaveChangesAsync();
        using(var response=await Submit(unsafeBooking,experience:"Email me at private@example.test <script>bad</script>",photo:false))check(response.StatusCode==HttpStatusCode.Redirect,"Potentially sensitive answers may be saved privately for review");
        var unsafeFeedback=await db.EventFeedbacks.AsNoTracking().SingleAsync(f=>f.BookingId==unsafeBooking.Id);
        using(var response=await Review(unsafeFeedback.Id,"Approve"))check(response.StatusCode==HttpStatusCode.BadRequest,"Contact details and script-like content cannot be approved");
        using(var response=await anonymous.GetAsync("/Home/About"))check(!(await response.Content.ReadAsStringAsync()).Contains("private@example.test"),"Unsafe private feedback remains absent from About");
        using(var response=await other.PostAsync("/Feedback/Withdraw",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",feedback.Id.ToString()}})))check(response.StatusCode==HttpStatusCode.BadRequest,"Feedback changes require anti-forgery token");
        using(var response=await Post(other,"/Feedback/Withdraw",otherToken,("id",feedback.Id.ToString())))check(response.StatusCode==HttpStatusCode.NotFound,"Another client cannot withdraw consent");
        using(var response=await Post(client,"/Testimonials/Review",token,("Id",feedback.Id.ToString()),("Decision","Feature"),("Version",stale),("ConfirmPublication","true")))check(response.StatusCode==HttpStatusCode.Redirect,"Client cannot call Staff moderation POST");
        await using var unavailableFactory=new ReleaseFactory(connection,smtpPort,mapUrl,root);
        using var unavailableScope=unavailableFactory.Services.CreateScope();
        var unavailableOptions=unavailableScope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FeedbackMediaOptions>>().Value;
        unavailableOptions.UploadsEnabled=true;unavailableOptions.ClamAvHost="127.0.0.1";unavailableOptions.ClamAvPort=1;
        var quarantine=await unavailableScope.ServiceProvider.GetRequiredService<FeedbackMediaService>().PrepareAsync(File(jpeg,"pending.jpg","image/jpeg"),CancellationToken.None);
        quarantine.FeedbackId=feedback.Id;db.FeedbackMedia.Add(quarantine);await db.SaveChangesAsync();
        check(quarantine.ProcessingStatus=="Quarantined"&&quarantine.ScannedAtUtc==null,"Configured scanner outage leaves processed photos quarantined");
        using(var response=await client.GetAsync("/FeedbackMedia/Private/"+quarantine.Id))check(response.StatusCode==HttpStatusCode.NotFound,"Even the owner cannot retrieve unscanned media");
        using(var response=await anonymous.GetAsync("/FeedbackMedia/Public/"+quarantine.Id))check(response.StatusCode==HttpStatusCode.NotFound,"Unprocessed media never becomes public");
        quarantine.PublicApproved=true;var unscannedBlocked=false;
        try{await db.SaveChangesAsync();}catch(DbUpdateException){unscannedBlocked=true;await db.Entry(quarantine).ReloadAsync();}
        check(unscannedBlocked,"SQL check constraint prevents approval of an unscanned file");
        var racedBooking=Make();db.Bookings.Add(racedBooking);await db.SaveChangesAsync();
        using(var response=await Submit(racedBooking,photo:false,overall:6)) {
            var html=await response.Content.ReadAsStringAsync();
            check(response.StatusCode==HttpStatusCode.OK&&html.Contains("A memorable fixture celebration.")&&!await db.EventFeedbacks.AnyAsync(f=>f.BookingId==racedBooking.Id),"Server rejects invalid ratings and preserves entered answers");
        }
        var duplicates=await Task.WhenAll(Submit(racedBooking,photo:false),Submit(racedBooking,photo:false));
        check(duplicates.All(r=>r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Conflict)&&await db.EventFeedbacks.CountAsync(f=>f.BookingId==racedBooking.Id)==1,"Concurrent HTTP submissions preserve exactly one feedback record");
        foreach(var result in duplicates)result.Dispose();
        db.Bookings.Add(Make());await db.SaveChangesAsync();
        var notifyBefore=await db.FeedbackInvitations.CountAsync();
        await FeedbackInvitationService.QueueAsync(db);var notifyOnce=await db.FeedbackInvitations.CountAsync();
        await FeedbackInvitationService.QueueAsync(db);
        check(notifyOnce>notifyBefore&&await db.FeedbackInvitations.CountAsync()==notifyOnce,"Eligible-event invitations are idempotent and skip submitted feedback");
        var duplicate=new EventFeedback {BookingId=completed.Id,ClientId=clientId,OverallRating=5,PerformanceRating=5,BookingRating=5};
        db.EventFeedbacks.Add(duplicate);var prevented=false;
        try{await db.SaveChangesAsync();}catch(DbUpdateException){prevented=true;db.Entry(duplicate).State=EntityState.Detached;}
        check(prevented,"SQL unique index independently prevents duplicate feedback records");
        var badRatingBooking=Make();db.Bookings.Add(badRatingBooking);await db.SaveChangesAsync();
        var badRating=new EventFeedback {BookingId=badRatingBooking.Id,ClientId=clientId,OverallRating=6,PerformanceRating=5,BookingRating=5};
        db.EventFeedbacks.Add(badRating);var ratingBlocked=false;
        try{await db.SaveChangesAsync();}catch(DbUpdateException){ratingBlocked=true;db.Entry(badRating).State=EntityState.Detached;}
        check(ratingBlocked,"SQL independently enforces the 1-to-5 rating range");
        var missing=new FeedbackMedia {FeedbackId=feedback.Id,StorageKey=Guid.NewGuid().ToString("N")+".jpg",Kind="Image",ContentType="image/jpeg",ProcessingStatus="Quarantined",ProcessingAttempts=4};
        var expiredMedia=new FeedbackMedia {FeedbackId=feedback.Id,StorageKey=Guid.NewGuid().ToString("N")+".jpg",Kind="Image",ContentType="image/jpeg",ProcessingStatus="Processing",ProcessingAttempts=5,ProcessingStartedAtUtc=DateTime.UtcNow.AddMinutes(-21)};
        db.FeedbackMedia.AddRange(missing,expiredMedia);await db.SaveChangesAsync();
        using(var worker=new FeedbackMediaWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),Microsoft.Extensions.Logging.Abstractions.NullLogger<FeedbackMediaWorker>.Instance)){
            await worker.StartAsync(CancellationToken.None);
            for(var attempt=0;attempt<100;attempt++){
                if(await db.FeedbackMedia.AsNoTracking().AnyAsync(m=>m.Id==missing.Id&&m.ProcessingStatus=="Failed"))break;
                await Task.Delay(100);
            }
            await worker.StopAsync(CancellationToken.None);
        }
        check(await db.FeedbackMedia.AsNoTracking().AnyAsync(m=>m.Id==quarantine.Id&&m.ProcessingStatus=="Ready"&&m.ScannedAtUtc!=null),"Actual media worker promotes quarantine only after a clean scanner result");
        check(await db.FeedbackMedia.AsNoTracking().AnyAsync(m=>m.Id==missing.Id&&m.ProcessingStatus=="Failed"&&m.ProcessingAttempts==5),"Actual worker bounds missing-file failures instead of leaving Processing indefinitely");
        check(await db.FeedbackMedia.AsNoTracking().AnyAsync(m=>m.Id==expiredMedia.Id&&m.ProcessingStatus=="Failed"&&m.ProcessingStartedAtUtc==null),"Actual worker recovers expired Processing leases with exhausted retries");
        var retryToken=await GetToken(client,"/Feedback/Details/"+feedback.Id);
        // Extra negative upload probes consume the real limiter; allow its unchanged window to expire.
        await Task.Delay(TimeSpan.FromSeconds(61));
        using(var retry=await Post(client,"/Feedback/RetryMedia/"+missing.Id,retryToken))
            check(retry.StatusCode==HttpStatusCode.Redirect&&await db.FeedbackMedia.AsNoTracking().AnyAsync(m=>m.Id==missing.Id&&m.ProcessingStatus=="Quarantined"&&m.ProcessingAttempts==0),"Owned failed media can be safely requeued through the antiforgery-protected retry route");
        await FeedbackScannerProtocolTests.RunAsync(check);
    }
}
