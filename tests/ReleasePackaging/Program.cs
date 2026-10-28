using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

// Metadata/registration inspection only: never execute Program.Main, initialize a key
// provider, load a certificate, connect to SQL or accept production configuration.
if (args.Length != 2) return 2;
var root = Directory.GetCurrentDirectory();
var package = Path.GetFullPath(args[0]);
var evidence = Path.GetFullPath(args[1]);
if (Path.GetDirectoryName(package) != root || !Path.GetFileName(package).StartsWith("publish-net10-alpha-beta-", StringComparison.Ordinal)
    || !evidence.StartsWith(Path.Combine(root, ".tmp-owner-build") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 2;
var checks = 0;
void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Release packaging assertion: " + name); checks++; Console.WriteLine("PASS: " + name); }
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(package, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(package, name.Name + ".dll")) : null;
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(package, "AriesMagicAppointmentSystem.dll"));
var views = new RazorCompiledItemLoader().LoadItems(assembly).Select(item => item.Identifier).ToHashSet(StringComparer.Ordinal);
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Views"), "*.cshtml", SearchOption.AllDirectories))
{
    if (Path.GetFileName(file) == "_ViewImports.cshtml") continue;
    Check(views.Contains("/" + Path.GetRelativePath(root, file).Replace('\\', '/')), "Current Razor view compiled: " + Path.GetRelativePath(root, file).Replace('\\', '/'));
}
var settings = new ConfigurationBuilder().AddJsonFile(Path.Combine(package, "appsettings.json"))
    .AddJsonFile(Path.Combine(package, "appsettings.Production.json")).Build();
Check(!settings.GetSection("DataProtection").Exists(), "Package JSON has no DataProtection activation/configuration");
var feature = assembly.GetType("AriesMagicAppointmentSystem.Services.CertificateDataProtection", throwOnError: true)!;
var services = new ServiceCollection();
var enabled = (bool)feature.GetMethod("Configure")!.Invoke(null, [services, settings, new InspectionEnvironment(package)])!;
Check(!enabled && services.Count == 0, "Actual packaged opt-in method is inactive and registers no services with shipped settings");
using (var provider = services.BuildServiceProvider())
{
    feature.GetMethod("ValidateStartup")!.Invoke(null, [provider]);
    Check(services.Count == 0, "Packaged disabled preflight returns without resolving or initializing a key provider");
}
var policy = assembly.GetType("AriesMagicAppointmentSystem.Services.PublicFileProtection", throwOnError: true)!.GetMethod("IsSensitivePath")!;
foreach (var path in new[] { "/appsettings.Production.json", "/APPSETTINGS.PRODUCTION.JSON", "/appsettings.Production.json.old", "/%2561ppsettings%252eProduction%252ejson", "/.env", "/App_Data/key-a.xml", "/wrapping.PFX", "/key-a.xml", "/revocation-all.xml" })
    Check((bool)policy.Invoke(null, [path])!, "Packaged policy blocks sensitive path: " + path);
foreach (var path in new[] { "/Payments/PaymentProof/3", "/Account/Login", "/api/config", "/css/client-hub.css" })
    Check(!(bool)policy.Invoke(null, [path])!, "Packaged policy preserves legitimate route: " + path);
Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
File.WriteAllText(evidence, JsonSerializer.Serialize(new { Checks = checks, Runtime = Environment.Version.ToString(), Views = views.Order().ToArray(), H4Configured = enabled, ServicesRegisteredByDisabledH4 = services.Count, ProductionWorkerVerified = false }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"SUCCESS: {checks} actual-package metadata/default-state checks; {views.Count} compiled Razor items. No application startup or key provider initialized.");
return 0;

sealed class InspectionEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "AriesMagicAppointmentSystem";
    public string EnvironmentName { get; set; } = "Production";
    public string ContentRootPath { get; set; } = root;
    public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
