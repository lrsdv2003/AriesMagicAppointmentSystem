// Technical fixtures only: these inclusion labels are not an Aries package catalog.
using System.Net;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class CatalogInclusionRegression
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        var connection = new SqlConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString);
        if (!connection.DataSource.StartsWith(@"(localdb)\", StringComparison.OrdinalIgnoreCase)
            || !connection.InitialCatalog.StartsWith("AriesOwnerRegression_", StringComparison.Ordinal)
            || !connection.IntegratedSecurity)
            throw new InvalidOperationException("Catalog fixtures require the isolated Owner regression database.");

        var fixtures = Enumerable.Range(0,3).Select(index => new Service {
            Name="Catalog SQL fixture " + (char)('A'+index), Price=11234.25m+index*1000.25m, DurationInHours=index+1,
            Description="Synthetic controller-to-SQL rendering verification; not published package content.",
            Inclusions=Enumerable.Range(1,index+4).Select(i=>new ServiceInclusion {Name="Inclusion "+i,IsRemovable=i!=1,DeductionAmount=i*100}).ToList()
        }).ToArray();
        db.Services.AddRange(fixtures);
        await db.SaveChangesAsync();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
        // A fresh context prevents tracked navigation fixup from hiding a missing Include.
        await using (var fresh=new ApplicationDbContext(options))
        {
            var controller=new HomeController(fresh);
            var catalog=(IEnumerable<Service>)((ViewResult)await controller.Packages()).Model!;
            foreach(var expected in fixtures)
            {
                var actual=catalog.Single(p=>p.Id==expected.Id);
                check(actual.Name==expected.Name&&actual.Price==expected.Price&&actual.DurationInHours==expected.DurationInHours,"Public query preserves SQL package identity, price and duration: "+expected.Name);
                check(actual.Inclusions.OrderBy(i=>i.Id).Select(i=>i.Id).SequenceEqual(expected.Inclusions.OrderBy(i=>i.Id).Select(i=>i.Id)),"Public query eagerly loads every matching SQL inclusion: "+expected.Name);
            }
            check(!fresh.ChangeTracker.Entries().Any(),"Public catalog remains read-only and untracked");
            check(await controller.Package(int.MaxValue) is NotFoundResult,"Missing package detail remains 404");
        }

        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName=typeof(HomeController).Assembly.GetName().Name, ContentRootPath=Directory.GetCurrentDirectory(),
            WebRootPath=Path.Combine(Directory.GetCurrentDirectory(),"wwwroot"), EnvironmentName="CatalogSqlFixture"
        });
        builder.Logging.ClearProviders();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(HomeController).Assembly);
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlServer(connection.ConnectionString));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        await using var app=builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.Use(async (context,next)=>{
            if(!HttpMethods.IsGet(context.Request.Method)||!context.Request.Path.StartsWithSegments("/Home")){context.Response.StatusCode=405;return;}
            await next();
        });
        app.MapControllerRoute("catalog-sql-fixture","{controller=Home}/{action=Index}/{id?}");
        await app.StartAsync();
        using var client=new HttpClient {BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
        var listing=await client.GetStringAsync("/Home/Packages");
        var output=Path.Combine(Path.GetTempPath(),"AriesOwnerSnapshots");Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"Home-Packages-SQL.html"),listing);
        foreach(var expected in fixtures)
        {
            var article=Regex.Match(listing,$"<article[^>]*aria-labelledby=\"package-{expected.Id}\".*?</article>",RegexOptions.Singleline).Value;
            check(!string.IsNullOrEmpty(article),"Real MVC listing renders SQL fixture package: "+expected.Name);
            var price=expected.Price.ToString("N2");
            check(WebUtility.HtmlDecode(article).Contains("₱"+price),"Real MVC listing renders exact SQL price: "+expected.Name);
            check(InclusionNames(article).SequenceEqual(expected.Inclusions.OrderBy(i=>i.Id).Select(i=>i.Name)),"Real MVC listing renders the complete matching inclusion list: "+expected.Name);
            var detail=await client.GetStringAsync("/Home/Package/"+expected.Id);
            await File.WriteAllTextAsync(Path.Combine(output,"Home-Package-SQL-"+expected.Name[^1]+".html"),detail);
            var text=WebUtility.HtmlDecode(detail);
            check(text.Contains("<h1>"+expected.Name+"</h1>")&&text.Contains("₱"+price),"Real MVC detail matches its SQL name and price: "+expected.Name);
            var inclusionList=Regex.Match(detail,"<ul class=\"we-detail-inclusions\">.*?</ul>",RegexOptions.Singleline).Value;
            check(InclusionNames(inclusionList).SequenceEqual(expected.Inclusions.OrderBy(i=>i.Id).Select(i=>i.Name)),"Real MVC detail renders all and only its matching SQL inclusions: "+expected.Name);
            check(text.Contains("serviceId="+expected.Id+"\""),"Real MVC detail preserves the selected package booking URL: "+expected.Name);
        }
        await app.StopAsync();
        await using var after=new ApplicationDbContext(options);
        foreach(var expected in fixtures)
        {
            var original=await after.Services.AsNoTracking().SingleAsync(p=>p.Id==expected.Id);
            check(original.Price==expected.Price&&await after.ServiceInclusions.CountAsync(i=>i.ServiceId==expected.Id)==expected.Inclusions.Count,"Catalog and detail GETs leave SQL price and inclusion counts unchanged: "+expected.Name);
        }
    }

    private static IEnumerable<string> InclusionNames(string html) => Regex.Matches(html,"<li>(.*?)</li>",RegexOptions.Singleline)
        .Select(match=>WebUtility.HtmlDecode(match.Groups[1].Value).Trim());
}
