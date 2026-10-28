using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

static class DeploymentReadinessRegression
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var root=Path.GetFullPath(Path.Combine(".tmp-owner-build","readiness-fixture-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var prefix="ARIES_READINESS_"+Guid.NewGuid().ToString("N")+"_";
        var variable=prefix+"EmailSettings__Username";
        File.WriteAllText(Path.Combine(root,"appsettings.json"),"{\"EmailSettings\":{\"Username\":\"base-fixture\"}}");
        File.WriteAllText(Path.Combine(root,"appsettings.Production.json"),"{\"EmailSettings\":{\"Username\":\"production-fixture\"}}");
        try
        {
            Environment.SetEnvironmentVariable(variable,"environment-fixture");
            var builder=new ConfigurationBuilder().SetBasePath(root).AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Production.json").AddEnvironmentVariables(prefix);
            var config=builder.Build();
            using var configLifetime=(IDisposable)config;
            check(config["EmailSettings:Username"]=="environment-fixture","Double-underscore SMTP environment variable overrides both JSON layers");
            var winning=config.Providers.Reverse().First(p=>p.TryGet("EmailSettings:Username",out _));
            check(winning.GetType().Name=="EnvironmentVariablesConfigurationProvider","Winning SMTP provider identified without printing its value");
            var cli=builder.AddCommandLine(["--EmailSettings:Username=command-fixture"]).Build();
            using var cliLifetime=(IDisposable)cli;
            check(cli["EmailSettings:Username"]=="command-fixture","Command-line override outranks environment and must be checked on the host");
        }
        finally {Environment.SetEnvironmentVariable(variable,null);}

        // New isolated keys only: never use, export, delete or reset application/production keys.
        var keyDirectory=Path.Combine(root,"isolated-test-keys");
        Directory.CreateDirectory(keyDirectory);
        var appName="ReadinessFixture-"+Guid.NewGuid().ToString("N");
        var payload=Guid.NewGuid().ToString("N");
        string encrypted;
        using(var first=Protection(keyDirectory,appName,true))
            encrypted=first.GetRequiredService<IDataProtectionProvider>().CreateProtector("continuity-test").Protect(payload);
        var keyFiles=Directory.GetFiles(keyDirectory,"key-*.xml");
        check(keyFiles.Length>0,"Isolated key repository persists keys to disk");
        check(keyFiles.All(f=>XDocument.Load(f).Descendants().Any(e=>e.Name.LocalName=="encryptedSecret")),"Isolated Windows test keys are protected at rest with DPAPI");
        using(var second=Protection(keyDirectory,appName,false))
        {
            check(second.GetRequiredService<IDataProtectionProvider>().CreateProtector("continuity-test").Unprotect(encrypted)==payload,
                "Second provider reads persisted protected keys with generation disabled");
            check(Directory.GetFiles(keyDirectory,"key-*.xml").Length==keyFiles.Length,"Continuity test does not replace or reset keys");
        }
        using(var other=Protection(keyDirectory,appName+"-different",false))
        {
            var rejected=false;
            try {other.GetRequiredService<IDataProtectionProvider>().CreateProtector("continuity-test").Unprotect(encrypted);}
            catch(CryptographicException){rejected=true;}
            check(rejected,"Changing the application discriminator breaks token compatibility");
        }

        var environment=new FixtureEnvironment(root);
        var privateBase=Path.Combine(root,"private-proofs");
        var relative="payments/"+Guid.NewGuid().ToString("N")+".jpg";
        var privatePath=Path.Combine(privateBase,"financial-proofs",relative.Replace('/',Path.DirectorySeparatorChar));
        var legacy="/uploads/payments/"+Guid.NewGuid().ToString("N")+".png";
        var legacyPath=Path.Combine(environment.WebRootPath,legacy.TrimStart('/').Replace('/',Path.DirectorySeparatorChar));
        // A deliberately non-decodable historic fixture demonstrates read-time preservation.
        byte[] bytes=[137,80,78,71,13,10,26,10,0,0,0,0];
        foreach(var path in new[]{privatePath,legacyPath}){Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes);}
        var settings=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["PrivateStorage:RootPath"]=privateBase}).Build();
        var controller=new PaymentsController(null!,environment,null!,null!,null!,null!,null!,settings,null!)
        { ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()} };
        var serve=typeof(PaymentsController).GetMethod("ServeFinancialProof",BindingFlags.Instance|BindingFlags.NonPublic)!;
        foreach(var (stored,path) in new[]{(relative,privatePath),(legacy,legacyPath)})
        {
            var result=serve.Invoke(controller,[stored]) as PhysicalFileResult;
            check(result is not null && result.FileName==path,"Existing proof path resolves without relocation: "+(stored.StartsWith('/')?"legacy":"private"));
            check((await File.ReadAllBytesAsync(path)).SequenceEqual(bytes),"Existing proof bytes are neither revalidated nor modified on read");
            check(controller.Response.Headers.CacheControl.ToString().Contains("no-store"),"Existing proof response remains private and non-cacheable");
        }
        var mediaRoot=Path.Combine(root,"private-media");Directory.CreateDirectory(mediaRoot);
        var key=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()+".jpg";
        await File.WriteAllBytesAsync(Path.Combine(mediaRoot,key),bytes);
        var store=new FeedbackMediaService(Options.Create(new FeedbackMediaOptions{Root=mediaRoot}),new NoScanner(),environment);
        check((await store.ReadAsync(key,CancellationToken.None)).SequenceEqual(bytes),"Existing feedback-media storage key reads original bytes without a new decode or scan");
        check(!await ImageUploadValidation.HasMatchingSignatureAsync(new FormFile(new MemoryStream(bytes),0,bytes.Length,"photo","historic.png"){Headers=new HeaderDictionary(),ContentType="image/png"}),
            "Historic reads remain separate from stricter validation of new uploads");
        // Retain isolated fixtures/DPAPI-protected test keys as private evidence; no key deletion.
    }

    static ServiceProvider Protection(string path,string appName,bool generate)
    {
        var services=new ServiceCollection().AddLogging(b=>b.ClearProviders());
        var builder=services.AddDataProtection().SetApplicationName(appName).PersistKeysToFileSystem(new DirectoryInfo(path));
        if(OperatingSystem.IsWindows())builder.ProtectKeysWithDpapi();
        else throw new PlatformNotSupportedException("This Windows deployment-readiness test requires DPAPI.");
        if(!generate)builder.DisableAutomaticKeyGeneration();
        return services.BuildServiceProvider();
    }
    public static async Task ValidateInventoryAsync(ApplicationDbContext database, Action<bool,string> check)
    {
        await database.Database.OpenConnectionAsync();
        try
        {
            using var command=database.Database.GetDbConnection().CreateCommand();
            command.CommandText=await File.ReadAllTextAsync("scripts/Readiness-UploadInventory.sql");
            await using var reader=await command.ExecuteReaderAsync();
            var resultSets=0;
            do
            {
                resultSets++;
                while(await reader.ReadAsync()) { /* aggregate data stays in the fixture */ }
            }while(await reader.NextResultAsync());
            check(resultSets==3,"Read-only aggregate inventory SQL matches the current isolated SQL schema");
        }
        finally {await database.Database.CloseConnectionAsync();}
    }
    sealed class NoScanner:IFeedbackMalwareScanner
    {public Task<MediaScan> ScanAsync(byte[] content,CancellationToken ct)=>throw new Exception("Historic media read must not trigger a scanner.");}
    sealed class FixtureEnvironment(string root):IWebHostEnvironment
    {
        public string ApplicationName{get;set;}="ReadinessFixture";
        public string EnvironmentName{get;set;}="Production";
        public string ContentRootPath{get;set;}=Path.Combine(root,"app");
        public string WebRootPath{get;set;}=Path.Combine(root,"app","wwwroot");
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
}
