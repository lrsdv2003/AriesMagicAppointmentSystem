using System.ComponentModel;
using System.Security.Authentication;
using AriesMagicAppointmentSystem.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AriesMagicAppointmentSystem.Services;

public sealed record DatabaseStartupFailure(string Category, string SqlNumbers, int? NativeCode);

public static class DatabaseStartupValidator
{
    public static async Task ValidateAsync(DbContextOptions<ApplicationDbContext> options, ILogger logger)
    {
        var stage = "ConnectionOpen";
        bool pending;
        try
        {
            // Suppress EF's raw provider exceptions for this startup probe only.
            // Keep normal runtime context configuration and interceptors unchanged.
            var probeOptions = new DbContextOptionsBuilder<ApplicationDbContext>(options)
                .UseLoggerFactory(NullLoggerFactory.Instance)
                .LogTo(_ => { })
                .EnableSensitiveDataLogging(false)
                .EnableDetailedErrors(false)
                .Options;
            await using var probe = new ApplicationDbContext(probeOptions);
            await probe.Database.OpenConnectionAsync();
            stage = "MigrationHistory";
            pending = (await probe.Database.GetPendingMigrationsAsync()).Any();
        }
        catch (Exception error)
        {
            var failure = Classify(error, stage);
            Log(logger, stage, failure);
            // No inner exception: host startup logging must not reprint the provider message.
            throw new InvalidOperationException(
                $"Database startup validation failed. Category={failure.Category}; Stage={stage}; SQL numbers={failure.SqlNumbers}; NativeCode={failure.NativeCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}. Connection values are omitted. See MYASP_DEPLOYMENT_REPORT.md.");
        }

        if (pending)
        {
            Log(logger, "MigrationHistory", new("PendingMigrations", "none", null));
            throw new InvalidOperationException("Database schema has pending migrations. Back up the effective database, review and apply the forward migration script, then restart. No migrations were executed by startup validation. See MYASP_DEPLOYMENT_REPORT.md.");
        }
    }

    private static void Log(ILogger logger, string stage, DatabaseStartupFailure failure) =>
        logger.LogError(new EventId(6201, "DatabaseStartupValidationFailed"),
            "Database startup validation failed. Stage={Stage}; Category={Category}; SqlNumbers={SqlNumbers}; NativeCode={NativeCode}. Connection values and provider messages omitted.",
            stage, failure.Category, failure.SqlNumbers, failure.NativeCode);

    public static DatabaseStartupFailure Classify(Exception error, string stage)
    {
        var chain = new List<Exception>();
        for (Exception? item = error; item is not null && chain.Count < 8; item = item.InnerException)
            chain.Add(item);
        var sql = chain.OfType<SqlException>().FirstOrDefault();
        var numbers = sql is null ? Array.Empty<int>() : sql.Errors.Cast<SqlError>()
            .Select(e => e.Number).Append(sql.Number).Distinct().Take(8).ToArray();
        var native = chain.OfType<Win32Exception>().Select(e => (int?)e.NativeErrorCode).FirstOrDefault();
        var codes = numbers.Concat(native.HasValue ? new[] { native.Value } : Array.Empty<int>()).ToArray();
        bool Has(params int[] values) => codes.Any(values.Contains);
        string category;
        if (Has(18456, 18452, 18470, 18487, 18488)) category = "AuthenticationFailure";
        else if (Has(4060, 4064, 916)) category = "DatabaseAccessFailure";
        else if (Has(229, 230, 262, 297, 15151)) category = "PermissionFailure";
        else if (Has(unchecked((int)0x80090325), unchecked((int)0x80090322),
                     unchecked((int)0x80090328), unchecked((int)0x800B0109),
                     unchecked((int)0x800B010F), unchecked((int)0x800B0101))) category = "TlsCertificateValidationFailure";
        // Some SqlClient versions expose only a generic number and a message.
        // Inspect recognized phrases in memory; never emit the message or substrings.
        else if (chain.Any(e => e.Message.Contains("certificate chain", StringComparison.OrdinalIgnoreCase)
                 || e.Message.Contains("remote certificate", StringComparison.OrdinalIgnoreCase)
                 || e.Message.Contains("target principal name is incorrect", StringComparison.OrdinalIgnoreCase)))
            category = "TlsCertificateValidationFailure";
        else if (chain.Any(e => e is AuthenticationException)
                 || chain.Any(e => e.Message.Contains("SSL Provider", StringComparison.OrdinalIgnoreCase)))
            category = "TlsHandshakeFailure";
        else if (Has(-2, 258, 10060) || chain.Any(e => e is TimeoutException))
            category = stage == "ConnectionOpen" ? "ConnectionTimeout" : "MigrationHistoryTimeout";
        else if (Has(2, 26, 40, 53, 64, 121, 233, 10053, 10054, 10061, 11001)) category = "NetworkOrEndpointFailure";
        else if (stage == "MigrationHistory" && Has(207, 208, 195, 2812)) category = "MigrationHistoryCompatibilityFailure";
        else if (chain.Any(e => e is ArgumentException)) category = "InvalidConnectionConfiguration";
        else if (sql is not null) category = "UnclassifiedSqlFailure";
        else if (stage == "MigrationHistory") category = "MigrationMetadataFailure";
        else category = "ConnectionConfigurationOrProviderFailure";
        return new(category, numbers.Length == 0 ? "none" : string.Join(",", numbers), native);
    }
}
