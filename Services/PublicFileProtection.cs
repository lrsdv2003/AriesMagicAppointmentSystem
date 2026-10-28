namespace AriesMagicAppointmentSystem.Services;

public static class PublicFileProtection
{
    public static bool TryDeny(HttpContext context)
    {
        if (!IsSensitivePath(context.Request.Path.Value ?? "")) return false;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return true;
    }

    private static readonly HashSet<string> PrivateSegments = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".svn", ".hg", ".config", ".vs", "App_Data", "logs" };
    private static readonly string[] ConfigurationFiles = [
        "web.config", "app.config", "nuget.config", "packages.config", "global.json",
        "launchSettings.json", "dotnet-tools.json", "secrets.json", "Directory.Build.props",
        "Directory.Build.targets", "Directory.Packages.props"
    ];
    private static readonly HashSet<string> PrivateExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".csproj", ".sln", ".sql", ".bak", ".mdf", ".ldf", ".log", ".pfx", ".p12", ".pem", ".key", ".ps1", ".pubxml", ".md", ".dll", ".exe", ".pdb", ".zip", ".7z", ".publishsettings", ".user" };

    public static bool IsSensitivePath(string path)
    {
        var decoded = path;
        try
        {
            // Bound canonicalization work and fail closed on excessive nested escaping.
            for (var round = 0; round < 4; round++)
            {
                var next = Uri.UnescapeDataString(decoded);
                if (next == decoded) break;
                decoded = next;
            }
            if (Uri.UnescapeDataString(decoded) != decoded) return true;
            decoded = decoded.Replace('\\', '/');
        }
        catch (UriFormatException) { return true; }
        foreach (var raw in decoded.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = raw.TrimEnd(' ', '.');
            // Include hidden/editor backup names, without blocking ordinary JSON assets.
            var configName = segment.TrimStart('.', '#', '~');
            if (segment.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
                || configName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) && configName.Contains(".json", StringComparison.OrdinalIgnoreCase)
                || PrivateSegments.Contains(segment)
                || ConfigurationFiles.Any(name => configName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || configName.StartsWith(name, StringComparison.OrdinalIgnoreCase) && configName.Length > name.Length
                        && ".~_-:".Contains(configName[name.Length]))
                || (segment.StartsWith("key-", StringComparison.OrdinalIgnoreCase) || segment.StartsWith("revocation-", StringComparison.OrdinalIgnoreCase))
                    && segment.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || PrivateExtensions.Contains(Path.GetExtension(segment))) return true;
        }
        return false;
    }
}
