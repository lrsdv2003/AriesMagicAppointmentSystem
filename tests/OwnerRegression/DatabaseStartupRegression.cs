using System.ComponentModel;
using System.Reflection;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

static class DatabaseStartupRegression
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        const string secret = "SENTINEL-password-user-token-connection";
        foreach (var (number, stage, category) in new[] {
            (18456,"ConnectionOpen","AuthenticationFailure"),
            (4060,"ConnectionOpen","DatabaseAccessFailure"),
            (229,"MigrationHistory","PermissionFailure"),
            (-2,"ConnectionOpen","ConnectionTimeout"),
            (-2,"MigrationHistory","MigrationHistoryTimeout"),
            (53,"ConnectionOpen","NetworkOrEndpointFailure"),
            (208,"MigrationHistory","MigrationHistoryCompatibilityFailure"),
            (50000,"MigrationHistory","UnclassifiedSqlFailure") })
        {
            var result = DatabaseStartupValidator.Classify(SqlFailure(number, secret), stage);
            check(result.Category == category && result.SqlNumbers.Contains(number.ToString()), "Startup SQL classification " + category);
            check(!result.ToString().Contains(secret), "Startup classification omits provider secrets for " + category);
        }
        check(DatabaseStartupValidator.Classify(new Win32Exception(unchecked((int)0x80090325), secret), "ConnectionOpen").Category == "TlsCertificateValidationFailure", "TLS certificate native code classified without message logging");
        check(DatabaseStartupValidator.Classify(new Exception("certificate chain " + secret), "ConnectionOpen").Category == "TlsCertificateValidationFailure", "TLS message-only fallback does not output message");
        check(DatabaseStartupValidator.Classify(new ArgumentException(secret), "ConnectionOpen").Category == "InvalidConnectionConfiguration", "Invalid connection syntax classified separately from SQL errors");

        // Closed loopback endpoint only. No production SQL, credentials or schema writes.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var logger = new CaptureLogger();
        var providerLogger = new CaptureLogger();
        using var factory = LoggerFactory.Create(b => b.AddProvider(new CaptureProvider(providerLogger)));
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=tcp:127.0.0.1,{port};Database={secret};User ID={secret};Password={secret};Encrypt=True;TrustServerCertificate=False;Connect Timeout=1;ConnectRetryCount=0")
            .UseLoggerFactory(factory).EnableSensitiveDataLogging().Options;
        Exception? caught = null;
        try { await DatabaseStartupValidator.ValidateAsync(options, logger); }
        catch (InvalidOperationException error) { caught = error; }
        check(caught is not null && caught.InnerException is null, "Actual failed startup remains fatal without raw inner exception");
        check(logger.Lines.Count == 1 && logger.Lines[0].Contains("ConnectionOpen") && logger.Lines[0].Contains("SqlNumbers="), "Actual startup emits one structured diagnostic with numeric evidence");
        check(!string.Join("", logger.Lines).Contains(secret) && !caught!.ToString().Contains(secret), "Actual diagnostic and fatal exception omit connection/password/user/token sentinels");
        check(providerLogger.Lines.Count == 0, "Startup probe suppresses raw EF provider logs even if source options enable sensitive logging");
    }

    // SqlClient has no public constructor for SqlException. Reflection is confined to tests.
    public static SqlException SqlFailure(int number, string secret)
    {
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderBy(c => c.GetParameters().Length).First();
        var firstInt = true;
        var values = constructor.GetParameters().Select(p => {
            if (p.ParameterType == typeof(string)) return (object)secret;
            if (p.ParameterType == typeof(int)) { var value = firstInt ? number : 1; firstInt = false; return value; }
            return p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
        }).ToArray();
        var error = (SqlError)constructor.Invoke(values);
        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(collection, [error]);
        var create = typeof(SqlException).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == "CreateException" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(string));
        return (SqlException)create.Invoke(null, [collection, secret])!;
    }

    sealed class CaptureProvider(CaptureLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }
    sealed class CaptureLogger : ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) throw new Exception("Raw exception passed to logger.");
            Lines.Add(formatter(state, exception));
        }
    }
}
