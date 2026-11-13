using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeAtlas.Traits.Core.AtlasBridge;

public sealed record InstallBeaconDto(
    [property: JsonPropertyName("CliExecutable")] string? CliExecutable,
    [property: JsonPropertyName("BaseDirectory")] string? BaseDirectory,
    [property: JsonPropertyName("Version")] string? Version,
    [property: JsonPropertyName("LastSeenUtc")] string? LastSeenUtc);

[JsonSerializable(typeof(InstallBeaconDto))]
internal sealed partial class LocatorJsonContext : JsonSerializerContext { }

/// <summary>
/// Resolves the cross-platform path to the <c>CodeAtlas</c> CLI executable using a 4-tier priority chain:
/// 1. Explicit override or <c>CODEATLAS_PATH</c> environment variable
/// 2. <c>&lt;LocalApplicationData&gt;/CodeAtlas/install.json</c> beacon written by CodeAtlas
/// 3. System <c>PATH</c> executable lookup
/// 4. Sibling repository build output fallback (<c>../CodeAtlas</c>)
/// </summary>
public static class CodeAtlasLocator
{
    public const string EnvVarCodeAtlasPath = "CODEATLAS_PATH";
    public const string BeaconFileName      = "install.json";
    public const string AppFolderName       = "CodeAtlas";

    /// <summary>
    /// Resolves the full file path to the <c>CodeAtlas</c> CLI executable or <c>.dll</c>, or returns <c>null</c> if not found.
    /// </summary>
    public static string? ResolveCliPath(string? explicitOverride = null, string? customAppDataDir = null)
    {
        // ── 1. Declarations ──
        string? envCandidate;
        string? beaconCandidate;
        string? pathCandidate;
        string? siblingCandidate;

        // ── 2. Preconditions & Guard Clauses ──
        if (!string.IsNullOrWhiteSpace(explicitOverride) && File.Exists(explicitOverride))
        {
            return Path.GetFullPath(explicitOverride);
        }

        // ── 3. Intent-First Narrative ──
        envCandidate = TryResolveFromEnvironment();
        if (envCandidate is not null)
        {
            return envCandidate;
        }

        beaconCandidate = TryResolveFromInstallBeaconQuietly(customAppDataDir);
        if (beaconCandidate is not null)
        {
            return beaconCandidate;
        }

        pathCandidate = TryResolveFromSystemPath();
        if (pathCandidate is not null)
        {
            return pathCandidate;
        }

        siblingCandidate = TryResolveFromSiblingRepository();

        // ── 4. Output / Return ──
        return siblingCandidate;
    }

    private static string? TryResolveFromEnvironment()
    {
        // ── 1. Declarations ──
        string? rawEnv;

        // ── 2. Preconditions & Guard Clauses ──
        rawEnv = Environment.GetEnvironmentVariable(EnvVarCodeAtlasPath);
        if (string.IsNullOrWhiteSpace(rawEnv))
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        if (File.Exists(rawEnv))
        {
            return Path.GetFullPath(rawEnv);
        }

        // ── 4. Output / Return ──
        return null;
    }

    private static string? TryResolveFromInstallBeaconQuietly(string? customAppDataDir)
    {
        // ── 1. Declarations ──
        string baseDataDir;
        string beaconPath;
        string jsonText;
        InstallBeaconDto? beacon;

        // ── 2. Preconditions & Guard Clauses ──
        baseDataDir = !string.IsNullOrWhiteSpace(customAppDataDir)
            ? Path.GetFullPath(customAppDataDir)
            : ResolveUserDataRoot();

        beaconPath = Path.Combine(baseDataDir, AppFolderName, BeaconFileName);
        if (!File.Exists(beaconPath))
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        try
        {
            jsonText = File.ReadAllText(beaconPath);
            beacon   = JsonSerializer.Deserialize(jsonText, LocatorJsonContext.Default.InstallBeaconDto);
            if (beacon?.CliExecutable is not null && File.Exists(beacon.CliExecutable))
            {
                return Path.GetFullPath(beacon.CliExecutable);
            }
        }
        catch
        {
            // Ignore malformed beacon and continue fallback resolution.
        }

        // ── 4. Output / Return ──
        return null;
    }

    private static string? TryResolveFromSystemPath()
    {
        // ── 1. Declarations ──
        string? pathVariable;
        string[] directories;
        string[] executableNames;

        // ── 2. Preconditions & Guard Clauses ──
        pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        directories     = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        executableNames = OperatingSystem.IsWindows()
            ? ["CodeAtlas.exe", "codeatlas.exe"]
            : ["CodeAtlas", "codeatlas"];

        foreach (var dir in directories)
        {
            foreach (var exeName in executableNames)
            {
                var candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        // ── 4. Output / Return ──
        return null;
    }

    private static string? TryResolveFromSiblingRepository()
    {
        // ── 1. Declarations ──
        string[] rootsToSearch;
        string exeName;

        // ── 2. Preconditions & Guard Clauses ──
        rootsToSearch =
        [
            AppContext.BaseDirectory,
            Environment.CurrentDirectory
        ];
        exeName = OperatingSystem.IsWindows() ? "CodeAtlas.exe" : "CodeAtlas";

        // ── 3. Intent-First Narrative ──
        foreach (var startDir in rootsToSearch)
        {
            var current = new DirectoryInfo(startDir);
            while (current is not null)
            {
                var siblingRepo = Path.Combine(current.FullName, "CodeAtlas");
                if (Directory.Exists(siblingRepo))
                {
                    var debugExe   = Path.Combine(siblingRepo, "src", "CodeAtlas.Cli", "bin", "Debug", "net10.0", exeName);
                    var releaseExe = Path.Combine(siblingRepo, "src", "CodeAtlas.Cli", "bin", "Release", "net10.0", exeName);
                    var debugDll   = Path.Combine(siblingRepo, "src", "CodeAtlas.Cli", "bin", "Debug", "net10.0", "CodeAtlas.dll");

                    if (File.Exists(releaseExe)) return Path.GetFullPath(releaseExe);
                    if (File.Exists(debugExe))   return Path.GetFullPath(debugExe);
                    if (File.Exists(debugDll))   return Path.GetFullPath(debugDll);
                }
                current = current.Parent;
            }
        }

        // ── 4. Output / Return ──
        return null;
    }

    private static string ResolveUserDataRoot()
    {
        // ── 1. Declarations ──
        string localAppData;
        string userProfile;

        // ── 2. Preconditions & Guard Clauses ──
        localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return localAppData;
        }

        // ── 3. Intent-First Narrative ──
        userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // ── 4. Output / Return ──
        return Path.Combine(userProfile, ".local", "share");
    }
}
