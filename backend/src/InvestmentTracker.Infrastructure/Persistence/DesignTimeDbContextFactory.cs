using System.Text.Json;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InvestmentTracker.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for generating PostgreSQL-compatible migrations.
/// This ensures migrations work correctly in production (PostgreSQL) environment.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        var connectionString = ResolveConnectionString();
        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }

    private static string ResolveConnectionString()
    {
        var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(envConnectionString))
            return envConnectionString.Trim();

        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                              ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                              ?? "Development";

        var apiProjectDir = FindApiProjectDirectory();

        var fromUserSecrets = TryGetUserSecretsConnectionString(apiProjectDir);
        if (!string.IsNullOrWhiteSpace(fromUserSecrets))
            return fromUserSecrets.Trim();

        var fromAppsettings = TryGetAppsettingsConnectionString(apiProjectDir, environmentName);
        if (!string.IsNullOrWhiteSpace(fromAppsettings))
            return fromAppsettings.Trim();

        throw new InvalidOperationException(
            "Cannot resolve ConnectionStrings:DefaultConnection for design-time DbContext. " +
            "Set env var ConnectionStrings__DefaultConnection, or configure it via user-secrets/appsettings in InvestmentTracker.API.");
    }

    private static string FindApiProjectDirectory()
    {
        var current = Directory.GetCurrentDirectory();

        var candidates = new[]
        {
            current,
            Path.Combine(current, "InvestmentTracker.API"),
            Path.Combine(current, "src", "InvestmentTracker.API"),
            Path.Combine(current, "backend", "src", "InvestmentTracker.API"),
            Path.GetFullPath(Path.Combine(current, "..", "InvestmentTracker.API")),
            Path.GetFullPath(Path.Combine(current, "..", "src", "InvestmentTracker.API")),
            Path.GetFullPath(Path.Combine(current, "..", "..", "InvestmentTracker.API")),
            Path.GetFullPath(Path.Combine(current, "..", "..", "src", "InvestmentTracker.API"))
        };

        foreach (var candidate in candidates.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(candidate, "InvestmentTracker.API.csproj")))
                return candidate;
        }

        return current;
    }

    private static string? TryGetUserSecretsConnectionString(string apiProjectDir)
    {
        var csprojPath = Path.Combine(apiProjectDir, "InvestmentTracker.API.csproj");
        if (!File.Exists(csprojPath))
            return null;

        var userSecretsId = TryGetUserSecretsIdFromCsproj(csprojPath);
        if (string.IsNullOrWhiteSpace(userSecretsId))
            return null;

        var secretsPath = GetUserSecretsJsonPath(userSecretsId);
        if (!File.Exists(secretsPath))
            return null;

        return TryGetJsonValueByFlatKey(secretsPath, "ConnectionStrings:DefaultConnection");
    }

    private static string? TryGetAppsettingsConnectionString(string apiProjectDir, string environmentName)
    {
        var envFile = Path.Combine(apiProjectDir, $"appsettings.{environmentName}.json");
        var baseFile = Path.Combine(apiProjectDir, "appsettings.json");

        var envValue = File.Exists(envFile)
            ? TryGetJsonValueByPath(envFile, "ConnectionStrings", "DefaultConnection")
            : null;

        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        var baseValue = File.Exists(baseFile)
            ? TryGetJsonValueByPath(baseFile, "ConnectionStrings", "DefaultConnection")
            : null;

        return baseValue;
    }

    private static string? TryGetUserSecretsIdFromCsproj(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            var element = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "UserSecretsId");
            return element?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string GetUserSecretsJsonPath(string userSecretsId)
    {
        // Windows default: %APPDATA%\Microsoft\UserSecrets\<id>\secrets.json
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            var windowsPath = Path.Combine(appData, "Microsoft", "UserSecrets", userSecretsId, "secrets.json");
            if (File.Exists(windowsPath) || Directory.Exists(Path.GetDirectoryName(windowsPath)!))
                return windowsPath;
        }

        // Linux/macOS default: ~/.microsoft/usersecrets/<id>/secrets.json
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".microsoft", "usersecrets", userSecretsId, "secrets.json");
    }

    private static string? TryGetJsonValueByPath(string filePath, params string[] path)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var doc = JsonDocument.Parse(stream);

            var current = doc.RootElement;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                    return null;
            }

            return current.ValueKind == JsonValueKind.String
                ? current.GetString()?.Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetJsonValueByFlatKey(string filePath, string key)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty(key, out var value))
                return null;

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
