using System.Globalization;
using System.Text.RegularExpressions;

namespace AriesMagicAppointmentSystem.Services;

// Preserve providers, level filters and event IDs, but never send arbitrary values,
// provider messages, exception stacks, query strings or scopes to production sinks.
public sealed class ProductionSafeLoggerFactory(ILoggerFactory inner, bool ownsInner = true) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new SafeLogger(inner.CreateLogger(categoryName), categoryName);
    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
    public void Dispose() { if (ownsInner) inner.Dispose(); }

    private sealed class SafeLogger(ILogger innerLogger, string categoryName) : ILogger
    {
        public bool IsEnabled(LogLevel level) => innerLogger.IsEnabled(level);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            innerLogger.BeginScope("Scope values omitted.");

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var fields = new List<string>();
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
                foreach (var item in values.Take(64))
                {
                    if (item.Key == "{OriginalFormat}" && item.Value is string template
                        && categoryName.StartsWith("Microsoft.AspNetCore.DataProtection", StringComparison.Ordinal))
                    {
                        if (template.Contains("ephemeral", StringComparison.OrdinalIgnoreCase)
                            || template.Contains("not be persisted", StringComparison.OrdinalIgnoreCase)) fields.Add("Condition=EphemeralDataProtection");
                        if (template.Contains("unencrypted", StringComparison.OrdinalIgnoreCase)) fields.Add("Condition=UnprotectedDataProtectionStorage");
                    }
                    var safe = SafeValue(item.Key, item.Value);
                    if (safe is not null) fields.Add(item.Key + "=" + safe);
                }
            if (exception is not null)
            {
                // Never invoke the original formatter or pass the exception downstream.
                var failure = DatabaseStartupValidator.Classify(exception, "Runtime");
                fields.Add("FailureType=" + exception.GetType().Name);
                if (failure.SqlNumbers != "none") fields.Add("SqlNumbers=" + failure.SqlNumbers);
                if (failure.NativeCode.HasValue) fields.Add("NativeCode=" + failure.NativeCode.Value.ToString(CultureInfo.InvariantCulture));
            }
            var text = "Operational event; " + (fields.Count == 0 ? "details omitted." : string.Join("; ", fields));
            innerLogger.Log(level, new EventId(id.Id), text, null, static (message, _) => message);
        }

        private static string? SafeValue(string name, object? value) => name switch
        {
            "Count" or "Attempt" or "RuntimeVersion" when value is int or long or Version =>
                Convert.ToString(value, CultureInfo.InvariantCulture),
            "NativeCode" when value is int => ((int)value).ToString(CultureInfo.InvariantCulture),
            "EnvironmentName" when value is "Production" or "Staging" or "Development" => (string)value,
            "Stage" when value is "ConnectionOpen" or "MigrationHistory" => (string)value,
            "SqlNumbers" when value is string numbers && Regex.IsMatch(numbers, @"\A(?:none|-?\d{1,11}(?:,-?\d{1,11}){0,7})\z") => numbers,
            "Category" when value is DataProtectionFailure protection && Enum.IsDefined(protection) => protection.ToString(),
            "Category" when value is string category && Categories.Contains(category) => category,
            "FailureType" when value is string failure && FailureTypes.Contains(failure) => failure,
            _ => null
        };
        private static readonly HashSet<string> Categories = [
            "AuthenticationFailure", "DatabaseAccessFailure", "PermissionFailure", "TlsCertificateValidationFailure",
            "TlsHandshakeFailure", "ConnectionTimeout", "MigrationHistoryTimeout", "NetworkOrEndpointFailure",
            "MigrationHistoryCompatibilityFailure", "InvalidConnectionConfiguration", "UnclassifiedSqlFailure",
            "MigrationMetadataFailure", "ConnectionConfigurationOrProviderFailure", "PendingMigrations"
        ];
        private static readonly HashSet<string> FailureTypes = [
            "SmtpCommandException", "SmtpProtocolException", "AuthenticationException", "SocketException", "IOException",
            "TimeoutException", "TaskCanceledException", "EmailDeliveryException", "SqlException", "DbUpdateException",
            "InvalidOperationException", "OperationCanceledException", "UnauthorizedAccessException"
        ];
    }
}

public static class ProductionSafeLogging
{
    public static void Configure(IServiceCollection services)
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(ILoggerFactory));
        services.Remove(descriptor);
        services.AddSingleton<ILoggerFactory>(provider =>
        {
            var original = (ILoggerFactory)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
            return new ProductionSafeLoggerFactory(original, descriptor.ImplementationInstance is null);
        });
        // Automatic scopes must not add arbitrary Activity baggage behind the wrapper.
        services.Configure<LoggerFactoryOptions>(o => o.ActivityTrackingOptions = ActivityTrackingOptions.None);
    }
}
