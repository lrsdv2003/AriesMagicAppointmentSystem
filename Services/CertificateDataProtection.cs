using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;

namespace AriesMagicAppointmentSystem.Services;

public enum DataProtectionFailure
{
    ConfigurationMissing, ConfigurationInvalid, DiscriminatorMismatch, PrivatePathRequired, PathLinkRejected,
    CertificateUnavailable, CertificatePasswordOrFormatInvalid, UnprotectedPfx, PrivateKeyUnavailable,
    CertificateNotValidNow, CertificateAlgorithmUnsupported, RepositoryUnavailable, EmptyKeyRing,
    UnprotectedExistingKey, RetainedKeyUnreadable, RepositoryNotWritable, ConfigurationOverridden, KeyContinuityMismatch, Validated
}

// No provider exception, configuration value, path, certificate, payload or secret is retained.
public sealed class DataProtectionConfigurationException(DataProtectionFailure failure)
    : InvalidOperationException($"Data Protection preparation failed: {failure}. Sensitive details omitted.")
{
    public DataProtectionFailure Failure { get; } = failure;
}

public static class CertificateDataProtection
{
    private const string Section = "DataProtection";
    private const string PasswordVariable = "DataProtection__CertificatePassword";
    private static readonly string[] OwnedSettings =
        ["KeyRingPath", "ApplicationName", "ExpectedDiscriminatorSha256", "RequiredEnvelopeSha256", "CertificatePath", "CertificatePassword", "DecryptionCertificates"];

    // Opt-in only. An absent configuration preserves the already deployed framework selection.
    // There is intentionally no empty-ring bootstrap, import/export or migration switch here.
    public static bool Configure(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var section = configuration.GetSection(Section);
        var mode = section["Mode"];
        var hasSettings = OwnedSettings.Any(name => section.GetSection(name).Exists());
        if (string.IsNullOrEmpty(mode) || mode == "FrameworkDefault")
        {
            if (hasSettings) throw Fail(DataProtectionFailure.ConfigurationMissing);
            return false;
        }
        if (mode != "Certificate") throw Fail(DataProtectionFailure.ConfigurationInvalid);

        var applicationName = Required(section["ApplicationName"]);
        var pin = Required(section["ExpectedDiscriminatorSha256"]);
        if (pin.Length != 64 || !pin.All(Uri.IsHexDigit)) throw Fail(DataProtectionFailure.ConfigurationInvalid);
        if (!FingerprintMatches(applicationName, pin)) throw Fail(DataProtectionFailure.DiscriminatorMismatch);
        var requiredEnvelopes = Required(section["RequiredEnvelopeSha256"]).Split(';', StringSplitOptions.TrimEntries);
        if (requiredEnvelopes.Length is < 1 or > 512 || requiredEnvelopes.Any(h => h.Length != 64 || !h.All(Uri.IsHexDigit)))
            throw Fail(DataProtectionFailure.ConfigurationInvalid);
        var repository = PrivatePath(Required(section["KeyRingPath"]), environment, directory: true);
        var certificatePath = PrivatePath(Required(section["CertificatePath"]), environment, directory: false);
        if (Within(certificatePath, repository)) throw Fail(DataProtectionFailure.PrivatePathRequired);

        var certificates = new List<X509Certificate2>();
        try
        {
            certificates.Add(LoadCertificate(certificatePath, ReadPassword(PasswordVariable), active: true));
            var previous = section.GetSection("DecryptionCertificates").GetChildren().ToArray();
            if (previous.Length > 16) throw Fail(DataProtectionFailure.ConfigurationInvalid);
            foreach (var item in previous)
            {
                if (!int.TryParse(item.Key, out var index) || index is < 0 or > 15 || item.Key != index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    throw Fail(DataProtectionFailure.ConfigurationInvalid);
                var path = PrivatePath(Required(item["Path"]), environment, directory: false);
                if (Within(path, repository)) throw Fail(DataProtectionFailure.PrivatePathRequired);
                certificates.Add(LoadCertificate(path,
                    ReadPassword($"DataProtection__DecryptionCertificates__{index}__Password"), active: false));
            }

            var owned = new CertificateLifetime(certificates.ToArray());
            // The factory registration gives DI ownership. ValidateStartup eagerly resolves it.
            services.AddSingleton(_ => owned);
            services.AddSingleton(new PreparedConfiguration(repository, applicationName, pin, requiredEnvelopes));
            services.AddDataProtection()
                .SetApplicationName(applicationName) // Exact existing value, including case/separators.
                .PersistKeysToFileSystem(new DirectoryInfo(repository))
                .ProtectKeysWithCertificate(certificates[0])
                .UnprotectKeysWithAnyCertificate(certificates.Skip(1).ToArray());
            return true;
        }
        catch
        {
            foreach (var certificate in certificates) certificate.Dispose();
            throw;
        }
    }

    public static void ValidateStartup(IServiceProvider services)
    {
        var prepared = services.GetService<PreparedConfiguration>();
        if (prepared is null) return; // Do not initialize or interrogate the default ring.
        _ = services.GetRequiredService<CertificateLifetime>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CertificateDataProtection));
        try
        {
            var discriminator = services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator;
            if (discriminator != prepared.ApplicationName || !FingerprintMatches(discriminator, prepared.Fingerprint))
                throw Fail(DataProtectionFailure.DiscriminatorMismatch);
            if (services.GetRequiredService<IDataProtectionProvider>() is EphemeralDataProtectionProvider)
                throw Fail(DataProtectionFailure.ConfigurationOverridden);
            var options = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
            if (options.XmlRepository is not FileSystemXmlRepository repository
                || !PathEquals(repository.Directory.FullName, prepared.Repository)
                || options.XmlEncryptor is not CertificateXmlEncryptor)
                throw Fail(DataProtectionFailure.ConfigurationOverridden);

            // Read existing envelopes only; never call Protect/CreateNewKey during validation.
            IReadOnlyCollection<System.Xml.Linq.XElement> elements;
            try { elements = repository.GetAllElements(); }
            catch { throw Fail(DataProtectionFailure.RepositoryUnavailable); }
            var storedKeys = elements.Where(e => e.Name.LocalName == "key").ToArray();
            if (storedKeys.Length == 0) throw Fail(DataProtectionFailure.EmptyKeyRing);
            foreach (var key in storedKeys)
                if (!key.Descendants().Any(e => e.Name.LocalName == "encryptedSecret")
                    || key.Descendants().Attributes().Any(a => a.Name.LocalName == "requiresEncryption" && a.Value == "true"))
                    throw Fail(DataProtectionFailure.UnprotectedExistingKey);

            IReadOnlyCollection<IKey> keys;
            try
            {
                keys = services.GetRequiredService<IKeyManager>().GetAllKeys();
                if (keys.Count < storedKeys.Length || !keys.Any(k => !k.IsRevoked))
                    throw Fail(DataProtectionFailure.RetainedKeyUnreadable);
                foreach (var key in keys)
                    if (key.CreateEncryptor() is null) throw Fail(DataProtectionFailure.RetainedKeyUnreadable);
            }
            catch { throw Fail(DataProtectionFailure.RetainedKeyUnreadable); }

            HashSet<string> presentEnvelopes;
            try
            {
                presentEnvelopes = Directory.EnumerateFiles(prepared.Repository, "*.xml")
                    .Select(file => { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); })
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch { throw Fail(DataProtectionFailure.RepositoryUnavailable); }
            if (!prepared.RequiredEnvelopes.All(presentEnvelopes.Contains)) throw Fail(DataProtectionFailure.KeyContinuityMismatch);

            // A non-key delete-on-close capability probe; no XML/key/marker is created or replaced.
            try
            {
                using var probe = new FileStream(Path.Combine(prepared.Repository, ".aries-dp-check-" + Guid.NewGuid().ToString("N") + ".tmp"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose | FileOptions.WriteThrough);
                probe.WriteByte(0);
                probe.Flush(flushToDisk: true);
            }
            catch { throw Fail(DataProtectionFailure.RepositoryNotWritable); }
            logger.LogInformation(new EventId(6203), "Data Protection validation; Category={Category}; Count={Count}.",
                DataProtectionFailure.Validated, keys.Count);
        }
        catch (DataProtectionConfigurationException failure)
        {
            logger.LogCritical(new EventId(6202), "Data Protection startup failed; Category={Category}.", failure.Failure);
            throw;
        }
        catch
        {
            logger.LogCritical(new EventId(6202), "Data Protection startup failed; Category={Category}.", DataProtectionFailure.ConfigurationOverridden);
            throw Fail(DataProtectionFailure.ConfigurationOverridden);
        }
    }

    // Matches the existing H4 metadata convention. Never trim/normalize the application value.
    public static string DiscriminatorFingerprint(string? value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value is null ? "NULL" : "VALUE:" + value)));

    private static bool FingerprintMatches(string? value, string pin) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(DiscriminatorFingerprint(value)), Convert.FromHexString(pin));
    private static string Required(string? value) => !string.IsNullOrWhiteSpace(value) ? value : throw Fail(DataProtectionFailure.ConfigurationMissing);
    private static string ReadPassword(string variable) =>
        Required(Environment.GetEnvironmentVariable(variable)); // Never JSON/CLI or command-line passwords.
    private static DataProtectionConfigurationException Fail(DataProtectionFailure failure) => new(failure);
    private static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Within(string path, string parent) => PathEquals(path, parent) ||
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string PrivatePath(string path, IWebHostEnvironment environment, bool directory)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) throw Fail(DataProtectionFailure.PrivatePathRequired);
            var full = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows() && (full.StartsWith(@"\\?\", StringComparison.Ordinal)
                || full.StartsWith(@"\\.\", StringComparison.Ordinal) || full.IndexOf(':', 2) >= 0))
                throw Fail(DataProtectionFailure.PrivatePathRequired);
            if (PathEquals(full, Path.GetPathRoot(full)!) || Within(full, environment.ContentRootPath)
                || (!string.IsNullOrEmpty(environment.WebRootPath) && Within(full, environment.WebRootPath)))
                throw Fail(DataProtectionFailure.PrivatePathRequired);
            if (directory ? !Directory.Exists(full) : !File.Exists(full))
                throw Fail(directory ? DataProtectionFailure.RepositoryUnavailable : DataProtectionFailure.CertificateUnavailable);
            if (!directory && Path.GetExtension(full).ToLowerInvariant() is not (".pfx" or ".p12"))
                throw Fail(DataProtectionFailure.ConfigurationInvalid);
            // Reject links/junctions in every ancestor. Native/static aliases still require host verification.
            for (var current = full; current is not null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw Fail(DataProtectionFailure.PathLinkRejected);
            // Compare resolved Windows paths as well: short names/alternate root spellings
            // must not turn application content into an apparently private sibling directory.
            full = CanonicalExistingPath(full);
            var contentRoot = CanonicalExistingPath(environment.ContentRootPath);
            var webRoot = !string.IsNullOrEmpty(environment.WebRootPath) && Directory.Exists(environment.WebRootPath)
                ? CanonicalExistingPath(environment.WebRootPath) : environment.WebRootPath;
            if (Within(full, contentRoot) || (!string.IsNullOrEmpty(webRoot) && Within(full, webRoot)))
                throw Fail(DataProtectionFailure.PrivatePathRequired);
            return full;
        }
        catch (DataProtectionConfigurationException) { throw; }
        catch { throw Fail(directory ? DataProtectionFailure.RepositoryUnavailable : DataProtectionFailure.CertificateUnavailable); }
    }

    private static string CanonicalExistingPath(string path)
    {
        if (!OperatingSystem.IsWindows()) return Path.GetFullPath(path);
        // Metadata-only, OPEN_EXISTING, no read/write/create access; directories require BACKUP_SEMANTICS.
        using var handle = OpenPath(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException();
        var buffer = new StringBuilder(32768);
        var length = FinalPath(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new IOException();
        var full = buffer.ToString();
        if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) full = @"\\" + full[8..];
        else if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) full = full[4..];
        return Path.GetFullPath(full);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenPath(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint FinalPath(SafeFileHandle handle, StringBuilder buffer, uint capacity, uint flags);

    private static X509Certificate2 LoadCertificate(string path, string password, bool active)
    {
        byte[]? bytes = null;
        X509Certificate2? certificate = null;
        try
        {
            if (new FileInfo(path).Length is <= 0 or > 4 * 1024 * 1024) throw Fail(DataProtectionFailure.CertificateUnavailable);
            bytes = File.ReadAllBytes(path);
            // Modern bounded loader; no persistent OS certificate-store/private-key import.
            certificate = X509CertificateLoader.LoadPkcs12(bytes, password, X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey) throw Fail(DataProtectionFailure.PrivateKeyUnavailable);
            var pfx = Pkcs12Info.Decode(bytes, out var read, skipCopy: true);
            if (read != bytes.Length || pfx.IntegrityMode != Pkcs12IntegrityMode.Password || !pfx.VerifyMac(password))
                throw Fail(DataProtectionFailure.UnprotectedPfx);
            foreach (var contents in pfx.AuthenticatedSafe) ValidatePfxBags(contents, 0);
            var now = DateTime.UtcNow;
            if (active && (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now))
                throw Fail(DataProtectionFailure.CertificateNotValidNow);
            using var publicKey = certificate.GetRSAPublicKey();
            using var privateKey = certificate.GetRSAPrivateKey();
            if (publicKey is null || privateKey is null || publicKey.KeySize < 2048)
                throw Fail(DataProtectionFailure.CertificateAlgorithmUnsupported);
            if (active && certificate.Extensions.OfType<X509KeyUsageExtension>().Any(e => !e.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment)))
                throw Fail(DataProtectionFailure.CertificateAlgorithmUnsupported);
            var nonce = RandomNumberGenerator.GetBytes(32);
            byte[]? recovered = null;
            try
            {
                recovered = privateKey.Decrypt(publicKey.Encrypt(nonce, RSAEncryptionPadding.Pkcs1), RSAEncryptionPadding.Pkcs1);
                if (!CryptographicOperations.FixedTimeEquals(nonce, recovered)) throw Fail(DataProtectionFailure.PrivateKeyUnavailable);
            }
            finally { CryptographicOperations.ZeroMemory(nonce); if (recovered is not null) CryptographicOperations.ZeroMemory(recovered); }
            return certificate;
        }
        catch (DataProtectionConfigurationException) { certificate?.Dispose(); throw; }
        catch (IOException) { certificate?.Dispose(); throw Fail(DataProtectionFailure.CertificateUnavailable); }
        catch (UnauthorizedAccessException) { certificate?.Dispose(); throw Fail(DataProtectionFailure.CertificateUnavailable); }
        catch { certificate?.Dispose(); throw Fail(DataProtectionFailure.CertificatePasswordOrFormatInvalid); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void ValidatePfxBags(Pkcs12SafeContents contents, int depth)
    {
        if (depth > 4) throw Fail(DataProtectionFailure.UnprotectedPfx);
        if (contents.ConfidentialityMode == Pkcs12ConfidentialityMode.Password) return;
        if (contents.ConfidentialityMode != Pkcs12ConfidentialityMode.None) throw Fail(DataProtectionFailure.UnprotectedPfx);
        foreach (var bag in contents.GetBags())
        {
            if (bag is Pkcs12KeyBag) throw Fail(DataProtectionFailure.UnprotectedPfx);
            if (bag is Pkcs12SafeContentsBag nested && nested.SafeContents is not null) ValidatePfxBags(nested.SafeContents, depth + 1);
        }
    }

    private sealed class PreparedConfiguration(string repository, string applicationName, string fingerprint, string[] requiredEnvelopes)
    {
        public string Repository { get; } = repository;
        public string ApplicationName { get; } = applicationName;
        public string Fingerprint { get; } = fingerprint;
        public string[] RequiredEnvelopes { get; } = requiredEnvelopes;
    }
    private sealed class CertificateLifetime(X509Certificate2[] certificates) : IDisposable
    {
        public void Dispose() { foreach (var certificate in certificates) certificate.Dispose(); }
    }
}
