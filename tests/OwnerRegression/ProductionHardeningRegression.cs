using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using SkiaSharp;

static class ProductionHardeningRegression
{
    public static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 2)
    {
        using var bitmap = new SKBitmap(width, 2);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        const string secret = "SENTINEL-password-cookie-token-user-payment-file";
        var sink = new Capture();
        var services = new ServiceCollection().AddLogging(b => b.AddProvider(sink).SetMinimumLevel(LogLevel.Warning));
        ProductionSafeLogging.Configure(services);
        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("ProductionAuditFixture");
        using (logger.BeginScope(secret))
        {
            logger.LogInformation("Do not bypass configured filters {Secret}", secret);
            check(sink.Messages.Count == 0, "Production log wrapper preserves level filtering");
            logger.LogError(new InvalidOperationException(secret, DatabaseStartupRegression.SqlFailure(18456, secret)),
                "Sensitive {UserId} {Password} {Token} {Cookie} {PaymentAmount} {Count}", secret, secret, secret, secret, 2000m, 3);
        }
        check(sink.Messages.Count == 1 && !sink.Messages[0].Contains(secret) && !sink.Messages[0].Contains("2000"), "Production logs omit message, exception, user, cookie, token, payment and scope data");
        check(sink.Messages[0].Contains("SqlNumbers=18456") && sink.Messages[0].Contains("Count=3"), "Production logging preserves safe SQL codes and operational counts");
        check(sink.Scopes.All(s => !s.Contains(secret)) && sink.Exceptions.All(e => e is null), "Raw exceptions and scopes never reach production sinks");
        logger.Log(LogLevel.Error, new EventId(6201, secret),
            new Dictionary<string, object?> { ["Stage"]="MigrationHistory", ["Category"]="PermissionFailure", ["SqlNumbers"]="229", ["Password"]=secret },
            null, (_, _) => throw new Exception("Unsafe formatter must not be invoked."));
        check(sink.LastId.Id == 6201 && sink.LastId.Name is null && sink.Messages.Last().Contains("Category=PermissionFailure"), "Structured startup event ID/classification retained without arbitrary event names/formatters");
        logger.LogError(secret);
        check(!sink.Messages.Last().Contains(secret), "Unstructured and original-format messages are redacted");
        var keyLogger=provider.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager");
        keyLogger.LogWarning("Using an ephemeral key repository at {Path}",secret);
        check(sink.Messages.Last().Contains("Condition=EphemeralDataProtection") && !sink.Messages.Last().Contains(secret), "Data Protection persistence warning retained without key paths");
        logger.LogError("SMTP failure {FailureType} {Password}","AuthenticationException",secret);
        check(sink.Messages.Last().Contains("FailureType=AuthenticationException") && !sink.Messages.Last().Contains(secret), "Safe operational SMTP failure type retained");

        IFormFile File(byte[] bytes, string extension, string mime) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "fixture"+extension) { Headers=new HeaderDictionary(),ContentType=mime };
        foreach(var (format, extension, mime) in new[] { (SKEncodedImageFormat.Png,".png","image/png"), (SKEncodedImageFormat.Jpeg,".jpg","image/jpeg"), (SKEncodedImageFormat.Webp,".webp","image/webp") })
            check(await ImageUploadValidation.HasMatchingSignatureAsync(File(Image(format),extension,mime)), "Valid fully decoded upload supported: "+extension);
        byte[] forged = [137,80,78,71,13,10,26,10,0,0,0,0];
        check(!await ImageUploadValidation.HasMatchingSignatureAsync(File(forged,".png","image/png")), "Forged header without decodable image rejected");
        check(!await ImageUploadValidation.HasMatchingSignatureAsync(File(Image(width:6001),".png","image/png")), "Image dimension bound enforced before pixel decoding");
        foreach(var path in new[] { "/appsettings.Production.json", "/assets/APPSETTINGS.json", "/.env", "/.git/HEAD", "/logs/stdout.log", "/App_Data/proof.jpg", "/backup.bak", "/release.sql", "/%61ppsettings.Production.json", "/appsettings.json. ", "/RECOVERY_REPORT.md" })
            check(PublicFileProtection.IsSensitivePath(path), "Sensitive public path blocked: "+path);
        foreach(var path in new[] { "/Account/Login", "/health", "/Payments/PaymentProof/1", "/uploads/profiles/photo.jpg", "/images/gcash-qr.jpeg", "/js/site.js", "/data/public.json" })
            check(!PublicFileProtection.IsSensitivePath(path), "Legitimate public or authorized path retained: "+path);
        var environment=new AuditEnvironment();
        var method=typeof(AriesMagicAppointmentSystem.Controllers.PaymentsController).GetMethod("PrivateProofRoot",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
        foreach(var root in new[] { environment.WebRootPath,Path.Combine(environment.WebRootPath,"insecure") })
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["PrivateStorage:RootPath"]=root }).Build();
            var controller=new AriesMagicAppointmentSystem.Controllers.PaymentsController(null!,environment,null!,null!,null!,null!,null!,config,null!);
            var blocked=false;
            try {method.Invoke(controller,null);}catch(System.Reflection.TargetInvocationException error) when(error.InnerException is InvalidOperationException){blocked=true;}
            check(blocked,"Configured financial proof storage under web root rejected without file operations");
        }
    }

    sealed class Capture : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];
        public List<string> Scopes { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public EventId LastId { get; private set; }
        public ILogger CreateLogger(string name) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull { Scopes.Add(state.ToString()!);return null; }
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState,Exception?,string> formatter)
        { Messages.Add(formatter(state,error));Exceptions.Add(error);LastId=id; }
    }
    sealed class AuditEnvironment : IWebHostEnvironment
    {
        public string ApplicationName {get;set;}="SecurityFixture";
        public string EnvironmentName {get;set;}="Production";
        public string ContentRootPath {get;set;}=Path.GetTempPath();
        public string WebRootPath {get;set;}=Path.Combine(Path.GetTempPath(),"SecurityFixture-wwwroot");
        public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
}
