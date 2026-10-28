using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

// Standalone local process tests: no SQL, HTTP diagnostics, application sessions or external services.
internal static class H4PfxChecks
{
    private const string Purpose = "AriesMagic.H4.IsolatedPfxProbe.v1";
    private const string Scheme = "AriesMagic.H4.SyntheticCookieOnly";
    private static int checks;
    private static readonly List<int> processIds = [];

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--worker") return Worker(args[1], args[2], args[3]);
        if (args.Length != 0) return 2;
        try { await Run(); return 0; }
        catch (Exception failure) { Console.Error.WriteLine("FAIL: isolated H4 verification; FailureType=" + failure.GetType().Name + "; details kept out of logs."); return 1; }
    }
    private static void Check(bool pass, string label)
    {
        if (!pass) { Console.Error.WriteLine("ASSERTION: " + label); throw new InvalidOperationException("Test failed."); }
        checks++;
        Console.WriteLine("PASS: " + label);
    }
    private static void Guard(string root)
    {
        var full = Path.GetFullPath(root);
        if (Path.GetDirectoryName(full) != Path.TrimEndingDirectorySeparator(Path.GetTempPath())
            || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), "^AriesH4Pfx_[0-9a-f]{32}$"))
            throw new InvalidOperationException("Unexpected isolated fixture.");
    }
    private static IConfiguration Settings(string root, string label) => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(root, label + ".json"), optional: false, reloadOnChange: false).Build();
    private static Dictionary<string, string?> Config(string root, string certificate = "a", string repository = "keys")
    {
        var appName = "H4-synthetic-" + Path.GetFileName(root) + "\\";
        return new()
        {
            ["DataProtection:Mode"] = "Certificate",
            ["DataProtection:ApplicationName"] = appName,
            ["DataProtection:ExpectedDiscriminatorSha256"] = CertificateDataProtection.DiscriminatorFingerprint(appName),
            ["DataProtection:KeyRingPath"] = Path.Combine(root, repository),
            ["DataProtection:CertificatePath"] = Path.Combine(root, "certificates", certificate + ".pfx"),
            ["DataProtection:RequiredEnvelopeSha256"] = Directory.Exists(Path.Combine(root, repository)) && Directory.GetFiles(Path.Combine(root, repository), "*.xml").Length > 0 ? string.Join(';', Directory.GetFiles(Path.Combine(root, repository), "*.xml").Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))) : new string('0',64)
        };
    }
    private static void Save(string root, string label, Dictionary<string, string?> values) =>
        File.WriteAllText(Path.Combine(root, label + ".json"), JsonSerializer.Serialize(values));
    private static ServiceCollection Services(SafeCapture capture)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new ProductionSafeLoggerFactory(capture, ownsInner: false));
        services.AddLogging();
        services.AddAuthentication(Scheme).AddCookie(Scheme);
        return services;
    }
    private static string[] KeyHashes(string directory) => Directory.GetFiles(directory, "*.xml")
        .Order().Select(p => Path.GetFileName(p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    private static void WriteMarker(string root, string label, IDataProtectionProvider provider, IServiceProvider services)
    {
        var nonce = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllBytes(Path.Combine(root, label + ".probe"), provider.CreateProtector(Purpose).Protect(nonce));
            File.WriteAllBytes(Path.Combine(root, label + ".digest"), SHA256.HashData(nonce));
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("synthetic-non-business", Convert.ToHexString(SHA256.HashData(nonce)))], Scheme)), Scheme);
            var format = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme).TicketDataFormat;
            File.WriteAllText(Path.Combine(root, label + ".ticket"), format.Protect(ticket));
        }
        finally { CryptographicOperations.ZeroMemory(nonce); }
    }
    private static bool ReadMarker(string root, string label, IDataProtectionProvider provider, IServiceProvider services)
    {
        var result = provider.CreateProtector(Purpose).Unprotect(File.ReadAllBytes(Path.Combine(root, label + ".probe")));
        try
        {
            var digest = File.ReadAllBytes(Path.Combine(root, label + ".digest"));
            var format = services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CookieAuthenticationOptions>>().Get(Scheme).TicketDataFormat;
            var ticket = format.Unprotect(File.ReadAllText(Path.Combine(root, label + ".ticket")));
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(result), digest)
                && ticket?.Principal.FindFirst("synthetic-non-business")?.Value == Convert.ToHexString(digest);
        }
        finally { CryptographicOperations.ZeroMemory(result); }
    }

    private static int Worker(string root, string operation, string label)
    {
        var capture = new SafeCapture();
        bool LogsAreSafe()
        {
            var secret = Environment.GetEnvironmentVariable("DataProtection__CertificatePassword");
            return !capture.Messages.Any(m => (!string.IsNullOrEmpty(secret) && m.Contains(secret, StringComparison.Ordinal))
                || m.Contains(root, StringComparison.Ordinal) || m.Contains("H4_RAW_KEY_SENTINEL", StringComparison.Ordinal));
        }
        try
        {
            Guard(root);
            if (!System.Text.RegularExpressions.Regex.IsMatch(label, "^[a-z0-9-]+$")) return 2;
            var config = Settings(root, label);
            var services = Services(capture);
            var environment = new FixtureEnvironment(Path.Combine(root, "app"));
            if (operation.StartsWith("seed-", StringComparison.Ordinal))
            {
                var directory = config["DataProtection:KeyRingPath"]!;
                Directory.CreateDirectory(directory);
                var builder = services.AddDataProtection().SetApplicationName(config["DataProtection:ApplicationName"]!)
                    .PersistKeysToFileSystem(new DirectoryInfo(directory));
                using var cert = X509CertificateLoader.LoadPkcs12FromFile(config["DataProtection:CertificatePath"]!,
                    Environment.GetEnvironmentVariable("DataProtection__CertificatePassword"), X509KeyStorageFlags.EphemeralKeySet);
                if (operation == "seed-dpapi")
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                    builder.ProtectKeysWithDpapi();
                }
                else if (operation != "seed-plaintext") builder.ProtectKeysWithCertificate(cert);
                using var provider = services.BuildServiceProvider();
                WriteMarker(root, label, provider.GetRequiredService<IDataProtectionProvider>(), provider);
            }
            else
            {
                CertificateDataProtection.Configure(services, config, environment);
                if (operation == "override-provider") services.AddDataProtection().UseEphemeralDataProtectionProvider();
                using var provider = services.BuildServiceProvider();
                string[]? before = null;
                try { before = KeyHashes(config["DataProtection:KeyRingPath"]!); } catch (UnauthorizedAccessException) { }
                CertificateDataProtection.ValidateStartup(provider);
                if (before is not null && !before.SequenceEqual(KeyHashes(config["DataProtection:KeyRingPath"]!))) return 3;
                var protection = provider.GetRequiredService<IDataProtectionProvider>();
                if (operation == "wrong-scope")
                {
                    try { _ = ReadMarker(root, "a", protection, provider); return 3; }
                    catch (CryptographicException) { }
                }
                else if (operation is "read-a" or "rotate" or "read-both")
                {
                    if (!ReadMarker(root, "a", protection, provider)) return 3;
                    if (operation == "rotate")
                    {
                        // Explicit rollover is isolated test behavior, never production startup code.
                        provider.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
                        WriteMarker(root, "b", protection, provider);
                    }
                    if (operation == "read-both" && !ReadMarker(root, "b", protection, provider)) return 3;
                }
                else if (operation == "read-dpapi" && !ReadMarker(root, "dpapi", protection, provider)) return 3;
                else if (operation == "read-expired" && !ReadMarker(root, "legacy-expired", protection, provider)) return 3;
            }
            if (!LogsAreSafe()) return 3;
            Console.WriteLine("RESULT=PASS; PID=" + Environment.ProcessId);
            return 0;
        }
        catch (DataProtectionConfigurationException failure)
        {
            if (!LogsAreSafe()) return 3;
            Console.WriteLine("RESULT=REJECTED; CATEGORY=" + failure.Failure);
            return 2;
        }
        catch { Console.WriteLine("RESULT=REJECTED; CATEGORY=UnexpectedFixtureFailure"); return 3; }
    }

    private static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "AriesH4Pfx_" + Guid.NewGuid().ToString("N"));
        Guard(root);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "app", "wwwroot"));
        Directory.CreateDirectory(Path.Combine(root, "certificates"));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        CreateCertificate(root, "a", password);
        CreateCertificate(root, "b", password);
        CreateCertificate(root, "expired", password, expired: true);
        CreateCertificate(root, "ecdsa", password, ecdsa: true);
        using (var cert = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(root, "certificates", "a.pfx"), password, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable))
        {
            using var publicOnly = X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));
            File.WriteAllBytes(Path.Combine(root, "certificates", "public.pfx"), publicOnly.Export(X509ContentType.Pfx, password));
            using var rsa = cert.GetRSAPrivateKey()!;
            var unsafeContents = new Pkcs12SafeContents(); unsafeContents.AddCertificate(cert); unsafeContents.AddKeyUnencrypted(rsa);
            var unsafePfx = new Pkcs12Builder(); unsafePfx.AddSafeContentsUnencrypted(unsafeContents); unsafePfx.SealWithMac(password, HashAlgorithmName.SHA256, 1000);
            File.WriteAllBytes(Path.Combine(root, "certificates", "unencrypted.pfx"), unsafePfx.Encode());
        }
        Save(root, "a", Config(root));
        var b = Config(root, "b"); b["DataProtection:DecryptionCertificates:0:Path"] = Path.Combine(root, "certificates", "a.pfx"); Save(root, "b", b);

        async Task<(int Exit, string Output)> Child(string operation, string label, string? pass = null, bool previous = false)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (var argument in new[] { "--worker", root, operation, label }) start.ArgumentList.Add(argument);
            start.Environment["DataProtection__CertificatePassword"] = pass ?? password;
            start.Environment["DataProtection__DecryptionCertificates__0__Password"] = previous ? password : "";
            using var process = Process.Start(start)!;
            processIds.Add(process.Id);
            var outputTask = process.StandardOutput.ReadToEndAsync(); var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask + await errorTask;
            Check(!output.Contains(password, StringComparison.Ordinal) && !output.Contains(root, StringComparison.Ordinal), "Child diagnostics omit password and private paths");
            return (process.ExitCode, output);
        }
        async Task Reject(string label, DataProtectionFailure expected, string? pass = null)
        {
            var result = await Child("validate", label, pass);
            Check(result.Exit == 2 && result.Output.Contains("CATEGORY=" + expected), "Safe rejection: " + label);
        }

        using (var provider = new ServiceCollection().AddLogging().BuildServiceProvider())
        {
            var services = new ServiceCollection();
            Check(!CertificateDataProtection.Configure(services, new ConfigurationBuilder().Build(), new FixtureEnvironment(Path.Combine(root, "app"))) && services.Count == 0,
                "Absent settings do not register/initialize or change framework Data Protection defaults");
        }
        Check((await Child("seed-cert", "a")).Exit == 0, "Fresh ring generated only by isolated bootstrap, certificate A");
        Save(root, "a", Config(root)); b["DataProtection:RequiredEnvelopeSha256"] = Config(root)["DataProtection:RequiredEnvelopeSha256"]; Save(root, "b", b);
        var first = KeyHashes(Path.Combine(root, "keys"));
        var xml = XDocument.Load(Directory.GetFiles(Path.Combine(root, "keys"), "key-*.xml").Single());
        Check(xml.Descendants().Any(e => e.Name.LocalName == "encryptedSecret") && !xml.Descendants().Any(e => e.Name.LocalName == "masterKey"), "Persisted key material is encrypted, no plaintext master key");
        Check(xml.Descendants().Attributes().Any(a => a.Name.LocalName == "decryptorType" && a.Value.Contains("EncryptedXmlDecryptor")), "Stored envelope uses certificate XML decryption");
        Check((await Child("read-a", "a")).Exit == 0, "Distinct restarted process decrypts original non-business marker and synthetic cookie format");
        Check(first.SequenceEqual(KeyHashes(Path.Combine(root, "keys"))), "Read/startup validation preserves every original key byte");
        Check(processIds.Distinct().Count() == processIds.Count, "Continuity exercised in distinct OS processes");

        var partial = new Dictionary<string, string?> { ["DataProtection:CertificatePath"] = Path.Combine(root, "certificates", "a.pfx") }; Save(root, "partial", partial);
        await Reject("partial", DataProtectionFailure.ConfigurationMissing);
        var missing = Config(root); missing.Remove("DataProtection:ApplicationName"); Save(root, "missing-name", missing); await Reject("missing-name", DataProtectionFailure.ConfigurationMissing);
        var badMode = Config(root); badMode["DataProtection:Mode"] = "Unexpected"; Save(root, "bad-mode", badMode); await Reject("bad-mode", DataProtectionFailure.ConfigurationInvalid);
        await Reject("a", DataProtectionFailure.CertificatePasswordOrFormatInvalid, "wrong-password-not-a-secret");
        await Reject("a", DataProtectionFailure.ConfigurationMissing, "");
        var jsonPassword = Config(root); jsonPassword["DataProtection:CertificatePassword"] = "fixture-json-password-must-be-ignored";
        Save(root, "json-password", jsonPassword);
        await Reject("json-password", DataProtectionFailure.ConfigurationMissing, "");
        Check((await Child("read-a", "json-password")).Exit == 0, "Process environment password works while a JSON password cannot supply a missing environment secret");
        var overrideResult = await Child("override-provider", "a");
        Check(overrideResult.Exit == 2 && overrideResult.Output.Contains("CATEGORY=ConfigurationOverridden"), "Later ephemeral-provider override fails startup validation");
        var missingEnvelope = Config(root); missingEnvelope["DataProtection:RequiredEnvelopeSha256"] += ";" + new string('0', 64);
        Save(root, "missing-envelope", missingEnvelope); await Reject("missing-envelope", DataProtectionFailure.KeyContinuityMismatch);
        Save(root, "alien", Config(root, repository: "alien"));
        Check((await Child("seed-cert", "alien")).Exit == 0, "Different nonempty ring fixture uses the same certificate but different DP keys");
        var wrongRing = Config(root, repository: "alien"); wrongRing["DataProtection:RequiredEnvelopeSha256"] = Config(root)["DataProtection:RequiredEnvelopeSha256"];
        Save(root, "wrong-ring", wrongRing); await Reject("wrong-ring", DataProtectionFailure.KeyContinuityMismatch);
        var missingPfx = Config(root, "not-present"); Save(root, "missing-pfx", missingPfx); await Reject("missing-pfx", DataProtectionFailure.CertificateUnavailable);
        foreach (var (name, failure) in new[] { ("public", DataProtectionFailure.PrivateKeyUnavailable), ("expired", DataProtectionFailure.CertificateNotValidNow), ("ecdsa", DataProtectionFailure.CertificateAlgorithmUnsupported), ("unencrypted", DataProtectionFailure.UnprotectedPfx) })
        { Save(root, name, Config(root, name)); await Reject(name, failure); }
        var changed = Config(root); changed["DataProtection:ApplicationName"] += "different"; Save(root, "changed-pin", changed); await Reject("changed-pin", DataProtectionFailure.DiscriminatorMismatch);
        changed["DataProtection:ExpectedDiscriminatorSha256"] = CertificateDataProtection.DiscriminatorFingerprint(changed["DataProtection:ApplicationName"]); Save(root, "changed-scope", changed);
        Check((await Child("wrong-scope", "changed-scope")).Exit == 0, "Changing discriminator and its pin still cannot decrypt old payloads");
        var publicPath = Config(root); publicPath["DataProtection:CertificatePath"] = Path.Combine(root, "app", "wwwroot", "unsafe.pfx"); Save(root, "public-path", publicPath); await Reject("public-path", DataProtectionFailure.PrivatePathRequired);
        var relativePath = Config(root); relativePath["DataProtection:KeyRingPath"] = "relative-keys"; Save(root, "relative-path", relativePath); await Reject("relative-path", DataProtectionFailure.PrivatePathRequired);
        if (OperatingSystem.IsWindows())
        {
            var devicePath = Config(root); devicePath["DataProtection:CertificatePath"] = @"\\?\" + devicePath["DataProtection:CertificatePath"];
            Save(root, "device-path", devicePath); await Reject("device-path", DataProtectionFailure.PrivatePathRequired);
        }
        Directory.CreateDirectory(Path.Combine(root, "empty")); Save(root, "empty", Config(root, repository: "empty")); await Reject("empty", DataProtectionFailure.EmptyKeyRing);
        Check(Directory.GetFiles(Path.Combine(root, "empty")).Length == 0, "Empty repository never bootstraps a replacement key ring");

        Save(root, "plaintext", Config(root, repository: "plaintext")); Check((await Child("seed-plaintext", "plaintext")).Exit == 0, "Plaintext key negative fixture created only in disposable test store"); await Reject("plaintext", DataProtectionFailure.UnprotectedExistingKey);
        Directory.CreateDirectory(Path.Combine(root, "corrupt")); File.WriteAllText(Path.Combine(root, "corrupt", "key-bad.xml"), "invalid"); Save(root, "corrupt", Config(root, repository: "corrupt")); await Reject("corrupt", DataProtectionFailure.RepositoryUnavailable);

        Check((await Child("rotate", "b", previous: true)).Exit == 0, "Certificate B writes a new test key while retaining certificate A reads");
        Check(first.All(h => KeyHashes(Path.Combine(root, "keys")).Contains(h)), "Rotation preserves original key IDs/files/bytes");
        Check((await Child("read-both", "b", previous: true)).Exit == 0, "Restart after rotation decrypts A and B markers and synthetic tickets");
        await Reject("a", DataProtectionFailure.RetainedKeyUnreadable);
        var rollback = Config(root); rollback["DataProtection:DecryptionCertificates:0:Path"] = Path.Combine(root, "certificates", "b.pfx"); Save(root, "rollback", rollback);
        Check((await Child("read-both", "rollback", previous: true)).Exit == 0, "Compatible rollback configuration retains both decryption certificates and full current ring");

        Save(root, "legacy-expired", Config(root, "expired", "expired-ring"));
        Check((await Child("seed-cert", "legacy-expired")).Exit == 0, "Expired certificate fixture generated only for legacy decryption testing");
        var expiredReader = Config(root, repository: "expired-ring"); expiredReader["DataProtection:DecryptionCertificates:0:Path"] = Path.Combine(root, "certificates", "expired.pfx");
        Save(root, "expired-reader", expiredReader);
        Check((await Child("read-expired", "expired-reader", previous: true)).Exit == 0, "Retained expired certificate can decrypt historical data without becoming the active encryptor");

        if (OperatingSystem.IsWindows())
        {
            Save(root, "dpapi", Config(root, repository: "dpapi")); Check((await Child("seed-dpapi", "dpapi")).Exit == 0, "Legacy DPAPI fixture generated only under test identity/machine");
            Save(root, "dpapi", Config(root, repository: "dpapi"));
            Check((await Child("read-dpapi", "dpapi")).Exit == 0, "PFX configuration reads old DPAPI key on the same Windows identity/machine without rewriting it");
            var keys = Path.Combine(root, "keys"); var directory = new DirectoryInfo(keys);
            var original = directory.GetAccessControl().GetSecurityDescriptorBinaryForm();
            void RestoreAccess()
            {
                // A fresh descriptor marks the Access section as modified; reusing an
                // unmodified FileSystemSecurity instance can leave the deny ACE in place.
                var restored = new DirectorySecurity();
                restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
                directory.SetAccessControl(restored);
            }
            var identity = WindowsIdentity.GetCurrent().User!;
            try
            {
                var denied = directory.GetAccessControl(); denied.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.ListDirectory, AccessControlType.Deny)); directory.SetAccessControl(denied);
                await Reject("a", DataProtectionFailure.RepositoryUnavailable);
            }
            finally { RestoreAccess(); }
            try
            {
                var denied = directory.GetAccessControl(); denied.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.CreateFiles, AccessControlType.Deny)); directory.SetAccessControl(denied);
                // Previous certificate is also required; use rollback config and pass the earlier certificate secret.
                var result = await Child("validate", "rollback", previous: true);
                Check(result.Exit == 2 && result.Output.Contains("CATEGORY=RepositoryNotWritable"), "Read-only ring fails sanitized rollover-write capability validation");
            }
            finally { RestoreAccess(); }
        }
        Check(!Directory.GetFiles(Path.Combine(root, "keys"), ".aries-dp-check-*").Any(), "Capability probes leave no temporary files");
        foreach (var path in new[] { "/key-00000000-0000-0000-0000-000000000000.xml", "/KEY-A.XML", "/revocation-all.xml", "/%6bey-a.xml", "/wrap.pfx", "/WRAP.P12" })
            Check(PublicFileProtection.IsSensitivePath(path), "Sensitive Data Protection file URL denied");
        foreach (var path in new[] { "/api/key-search", "/assets/catalog.xml", "/images/logo.webp" })
            Check(!PublicFileProtection.IsSensitivePath(path), "Normal endpoint/static URL preserved");
        var evidence = new { Checks = checks, Runtime = Environment.Version.ToString(), ProcessCount = processIds.Count, ProcessIds = processIds, RepositoryProtected = true, RestartContinuity = true, CertificateRotation = true, ProductionVerified = false };
        Directory.CreateDirectory(".tmp-owner-build"); File.WriteAllText(".tmp-owner-build/h4-pfx-results.json", JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"SUCCESS: {checks} H4 PFX checks passed on {Environment.Version}; {processIds.Count} isolated worker processes. Production H4 remains unverified.");
        // New disposable fixture only, retained privately; never delete/reset production or prior keys.
    }

    private static void CreateCertificate(string root, string name, string password, bool expired = false, bool ecdsa = false)
    {
        using AsymmetricAlgorithm key = ecdsa ? ECDsa.Create(ECCurve.NamedCurves.nistP256) : RSA.Create(2048);
        var request = key is RSA rsa ? new CertificateRequest("CN=Aries-H4-Synthetic", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            : new CertificateRequest("CN=Aries-H4-Synthetic", (ECDsa)key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, critical: true));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(expired ? -730 : -1), DateTimeOffset.UtcNow.AddDays(expired ? -365 : 365));
        File.WriteAllBytes(Path.Combine(root, "certificates", name + ".pfx"), cert.Export(X509ContentType.Pfx, password));
    }
    private sealed class FixtureEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "H4Isolated";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = root;
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class SafeCapture : ILoggerFactory
    {
        public List<string> Messages { get; } = [];
        public void Dispose() { }
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => new Sink(Messages);
        private sealed class Sink(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
        }
    }
}
