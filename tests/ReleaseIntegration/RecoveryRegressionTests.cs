using System.Security.Claims;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

static class RecoveryRegressionTests
{
    public static async Task SchemaAsync(string connection,int port,Uri map,string root,Action<bool,string> check)
    {
        var sql=new SqlConnectionStringBuilder(connection);
        var database=sql.InitialCatalog+"_startup";
        if(!Regex.IsMatch(database,"^AriesReleaseIntegration_[0-9a-f]{32}_startup$"))throw new Exception("Unsafe startup fixture name.");
        sql.InitialCatalog="master";
        await using var master=new SqlConnection(sql.ConnectionString);await master.OpenAsync();
        using(var create=new SqlCommand($"CREATE DATABASE [{database}]",master))await create.ExecuteNonQueryAsync();
        sql.InitialCatalog=database;
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(sql.ConnectionString).Options);
        try {
            var result=await new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext());
            check(result.Status==HealthStatus.Unhealthy&&result.Description=="Database schema requires migration.","Connected but unmigrated SQL database is not reported ready");
            await using(var factory=new ReleaseFactory(sql.ConnectionString,port,map,root)) {
                var rejected=false;
                try {using var client=factory.CreateClient();}
                catch(InvalidOperationException ex) when(ex.Message.Contains("pending migrations")){rejected=true;}
                check(rejected,"Startup rejects missing migrations with actionable guidance before serving requests or starting workers");
            }
            await db.Database.MigrateAsync();
            check((await new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext())).Status==HealthStatus.Healthy,"Migrated SQL database passes schema-aware readiness");
            await using(var factory=new ReleaseFactory(sql.ConnectionString,port,map,root)) {
                using var client=factory.CreateClient();
                check((await client.GetAsync("/")).IsSuccessStatusCode,"The same database serves the real homepage after forward migration");
            }
        } finally {await db.Database.EnsureDeletedAsync();}
    }

    public static async Task ChatAsync(string connection,Action<bool,string> check)
    {
        var counter=new MessageCounter();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).AddInterceptors(counter).Options);
        var client=await db.Users.SingleAsync(u=>u.Email=="client@example.test");
        var staff=await db.Users.SingleAsync(u=>u.Email=="staff@example.test");
        var conversation=new Conversation {Title="Recovery chat fixture",ConversationType=ConversationTypes.ClientSupport,CreatedByUserId=client.Id,
            Participants=[new(){UserId=client.Id},new(){UserId=staff.Id}]};
        var start=DateTime.UtcNow.AddDays(-2);
        conversation.Messages=Enumerable.Range(0,1000).Select(i=>new Message {SenderId=staff.Id,SentAt=start.AddSeconds(i),
            MessageContent=i==0?"historic-search-sentinel":"fixture-message-"+i,
            MessageType=i==0?MessageTypes.ActionRequest:MessageTypes.Text,RequestStatus=i==0?MessageRequestStatuses.Open:null}).ToList();
        db.Conversations.Add(conversation);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        counter.Reset();
        await db.ConversationParticipants.AsNoTracking().Where(p=>p.UserId==client.Id&&p.ConversationId==conversation.Id).Include(p=>p.Conversation!).ThenInclude(c=>c.Messages).AsSplitQuery().ToListAsync();
        await db.Conversations.AsNoTracking().Where(c=>c.Id==conversation.Id).Include(c=>c.Messages).AsSplitQuery().SingleAsync();
        var legacy=counter.Count;
        counter.Reset();
        var controller=new CommunicationsController(db) {ControllerContext=new ControllerContext {HttpContext=new DefaultHttpContext {
            User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,client.Id),new Claim(ClaimTypes.Role,"Client")],"Fixture"))}}};
        var result=(ViewResult)await controller.Index(conversation.Id);
        var model=(CommunicationCenterViewModel)result.Model!;
        var materialized=counter.Count;
        var item=model.Conversations.Single(c=>c.Id==conversation.Id);
        check(model.SelectedConversation!.Messages.Count==250&&model.SelectedConversation.Messages.First().Content=="fixture-message-750"&&model.SelectedConversation.Messages.Last().Content=="fixture-message-999","Conversation displays exactly the latest 250 messages in chronological order");
        check(item.UnreadCount==1000&&item.HasOpenRequest&&item.LatestMessage=="fixture-message-999","Inbox SQL aggregates preserve all unread messages and old open requests despite bounded previews");
        // Other small conversations already exist in this shared synthetic database.
        Console.WriteLine($"CHAT MATERIALIZATION: legacy fixture={legacy}; optimized whole inbox plus selected detail={materialized}; fixture messages=1000.");
        check(legacy>=2000&&materialized>0&&materialized<=legacy*0.15,"Measured chat message materializations fall by at least 85 percent against the legacy full-history queries");
        var searched=(CommunicationCenterViewModel)((ViewResult)await controller.Index(conversation.Id,q:"historic-search-sentinel")).Model!;
        check(searched.Conversations.Any(c=>c.Id==conversation.Id),"Search still finds historic messages outside the latest 250");
        var requests=(CommunicationCenterViewModel)((ViewResult)await controller.Index(conversation.Id,filter:"requests")).Model!;
        check(requests.Conversations.Any(c=>c.Id==conversation.Id),"Requests filter retains conversations with old open requests");
        check(await db.Messages.CountAsync(m=>m.ConversationId==conversation.Id)==1000,"Bounded chat loading preserves all historical message records");
    }
    sealed class MessageCounter : IMaterializationInterceptor
    {
        readonly HashSet<object> instances=new(ReferenceEqualityComparer.Instance);
        public int Count=>instances.Count;
        public void Reset()=>instances.Clear();
        public object InitializedInstance(MaterializationInterceptionData data,object entity){if(entity is Message)instances.Add(entity);return entity;}
    }
}
