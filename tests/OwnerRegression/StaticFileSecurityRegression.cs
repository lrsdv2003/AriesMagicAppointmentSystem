using System.Net;
using System.Reflection;
using System.Runtime.Loader;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.Text.Json;

static class StaticFileSecurityRegression
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var frozen=Path.GetFullPath("publish-net10-production-hardened/AriesMagicAppointmentSystem.dll");
        if(File.Exists(frozen))
        {
        var load=new AssemblyLoadContext("FrozenStaticFilePolicy",isCollectible:true);
        try
        {
            var type=load.LoadFromAssemblyPath(frozen).GetType("AriesMagicAppointmentSystem.Services.PublicFileProtection")!;
            var method=type.GetMethod("IsSensitivePath",BindingFlags.Public|BindingFlags.Static)!;
            bool Old(string path)=>(bool)method.Invoke(null,[path])!;
            check(Old("/appsettings.Production.json") && Old("/APPSETTINGS.PRODUCTION.JSON"),"Frozen hardened artifact already denies the reported URL and alternate casing");
            check(!Old("/appsettings.Production.json.old") && PublicFileProtection.IsSensitivePath("/appsettings.Production.json.old"),"Verified backup-name gap is closed without replacing the frozen package");
            using var manifest=JsonDocument.Parse(await File.ReadAllTextAsync("publish-net10-production-hardened/AriesMagicAppointmentSystem.staticwebassets.endpoints.json"));
            var newlyDenied=manifest.RootElement.GetProperty("Endpoints").EnumerateArray().Select(e=>"/"+e.GetProperty("Route").GetString())
                .Count(path=>PublicFileProtection.IsSensitivePath(path) && !Old(path));
            check(newlyDenied==0,"Strengthening introduces no newly denied routes in the frozen static-asset catalog");
        }
        finally {load.Unload();}
        }
        else Console.WriteLine("SKIP frozen-package comparison: ignored delivery folder not present. Local policy/HTTP fixtures still run.");

        foreach(var path in new[] {
            "/appsettings.json.old", "/appsettings.Production.json~", "/.appsettings.Production.json.swp",
            "/web.config.old", "/WEB.CONFIG.backup", "/.web.config.swp", "/.env.production.old",
            "/NuGet.Config.save", "/Directory.Build.props.old", "/.config/dotnet-tools.json",
            "/%2561ppsettings%252eProduction%252ejson", "/a%5CAPPSETTINGS%2eJSON",
            "/%252eenv.production", "/appsettings.Production.json%3A%3A$DATA",
            "/%252525252561ppsettings.Production.json" })
            check(PublicFileProtection.IsSensitivePath(path),"Sensitive backup/encoded path denied: "+path);
        foreach(var path in new[]{"/api/config","/api/appsettings","/data/public.json","/images/logo%20one.png","/js/appsettings-guide.js","/css/site.css"})
            check(!PublicFileProtection.IsSensitivePath(path),"Unrelated API/static path preserved: "+path);

        var root=Path.GetFullPath(Path.Combine(".tmp-owner-build","static-security-"+Guid.NewGuid().ToString("N")));
        var appRoot=Path.Combine(root,"app");var webRoot=Path.Combine(appRoot,"wwwroot");var extraRoot=Path.Combine(root,"secondary");
        Directory.CreateDirectory(webRoot);Directory.CreateDirectory(extraRoot);
        await File.WriteAllTextAsync(Path.Combine(appRoot,"appsettings.Production.json"),"{\"FixtureOnly\":\"loaded-without-serving\"}");
        await File.WriteAllTextAsync(Path.Combine(webRoot,"site.css"),"body { color: blue; }");
        await File.WriteAllTextAsync(Path.Combine(webRoot,"public.json"),"{\"fixture\":true}");
        foreach(var file in new[]{"appsettings.Production.json","appsettings.Production.json.old",".env.production","web.config.old"})
            await File.WriteAllTextAsync(Path.Combine(extraRoot,file),file=="appsettings.Production.json"?"":"NON-SENSITIVE-FIXTURE");

        await using(var baseline=await Start(appRoot,webRoot,extraRoot,false,false))
        {
            using var client=Client(baseline);
            using var result=await client.GetAsync("/appsettings.Production.json");
            check(result.StatusCode==HttpStatusCode.NotFound,"Default static provider does not serve the synthetic private application-root config");
        }
        string tag;
        await using(var reproduction=await Start(appRoot,webRoot,extraRoot,false,true))
        {
            using var client=Client(reproduction);
            using var result=await client.GetAsync("/appsettings.Production.json");
            check(result.StatusCode==HttpStatusCode.OK && result.Content.Headers.ContentType?.MediaType=="application/json" && (await result.Content.ReadAsByteArrayAsync()).Length==0,
                "Secondary provider reproduces a zero-byte 200 JSON response outside wwwroot using only synthetic files");
            check(result.Headers.ETag is not null && result.Content.Headers.LastModified.HasValue && result.Headers.AcceptRanges.Contains("bytes") && result.Headers.Server.Any(v=>v.Product?.Name=="Kestrel"),
                "Synthetic static response reproduces ETag/Last-Modified/Accept-Ranges/Kestrel metadata");
            tag=result.Headers.ETag!.ToString();
        }
        await using(var hardened=await Start(appRoot,webRoot,extraRoot,true,true))
        {
            using var client=Client(hardened);
            foreach(var path in new[]{"/appsettings.Production.json","/APPSETTINGS.PRODUCTION.JSON","/%61ppsettings%2eProduction%2ejson","/%2561ppsettings%252eProduction%252ejson","/appsettings.Production.json.old","/.env.production","/web.config.old"})
                foreach(var verb in new[]{HttpMethod.Get,HttpMethod.Head})
                {
                    using var request=new HttpRequestMessage(verb,path);
                    using var result=await client.SendAsync(request);
                    check(result.StatusCode==HttpStatusCode.NotFound || path.Contains('%') && result.StatusCode==HttpStatusCode.BadRequest,
                        "Guard precedes both static providers for "+verb+" "+path);
                    if(result.StatusCode==HttpStatusCode.NotFound)
                        check(result.Headers.CacheControl?.NoStore==true && result.Headers.ETag is null && result.Content.Headers.LastModified is null && !result.Headers.AcceptRanges.Any(),
                            "Denied response has no static metadata and is not cacheable");
                }
            using(var conditional=new HttpRequestMessage(HttpMethod.Get,"/appsettings.Production.json"))
            {
                conditional.Headers.TryAddWithoutValidation("If-None-Match",tag);
                conditional.Headers.TryAddWithoutValidation("Range","bytes=0-0");
                using var result=await client.SendAsync(conditional);
                check(result.StatusCode==HttpStatusCode.NotFound,"Conditional/range requests cannot turn denial into 304 or 206");
            }
            foreach(var path in new[]{"/site.css","/public.json","/api/config","/api/ping?file=appsettings.Production.json"})
            {
                using var result=await client.GetAsync(path);
                check(result.StatusCode==HttpStatusCode.OK,"Normal static/API/query route remains usable: "+path);
            }
        }
        check(File.Exists(Path.Combine(appRoot,"appsettings.Production.json")),"Synthetic private application-root config remains in place");
    }

    static async Task<WebApplication> Start(string content,string web,string extra,bool guard,bool secondary)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=content,WebRootPath=web,EnvironmentName="Staging"});
        builder.Logging.ClearProviders();
        builder.Configuration.AddJsonFile("appsettings.Production.json",optional:false);
        if(builder.Configuration["FixtureOnly"]!="loaded-without-serving")throw new Exception("Synthetic config loading failed.");
        var app=builder.Build();app.Urls.Add("http://127.0.0.1:0");
        if(guard)app.Use(async(context,next)=>{if(PublicFileProtection.TryDeny(context))return;await next();});
        app.UseStaticFiles();
        if(secondary)
        {
            var files=new PhysicalFileProvider(extra);
            app.Lifetime.ApplicationStopped.Register(files.Dispose);
            // Deliberately permissive fixture only; not production configuration.
            app.UseStaticFiles(new StaticFileOptions{FileProvider=files,ServeUnknownFileTypes=true});
        }
        app.MapGet("/api/config",()=>Results.Json(new{ok=true}));
        app.MapGet("/api/ping",()=>Results.Json(new{ok=true}));
        await app.StartAsync();return app;
    }
    static HttpClient Client(WebApplication app)=>new(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false})
    {BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
}
