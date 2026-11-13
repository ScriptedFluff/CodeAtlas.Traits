using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeAtlas.Traits.Core.Config;

/// <summary>
/// Loads workspace and application-level file filtering rules (<c>ignorePatterns</c>, <c>ignoreDirectories</c>,
/// and <c>includePatterns</c>) from <c>.codeatlas/settings.json</c> and <c>appsettings.json</c>, matching
/// the configuration conventions of <c>CodeAtlas</c> (e.g. <c>**/*Tests/**/*</c>).
/// </summary>
public sealed class TraitsAppConfig
{
    private readonly List<Regex> _ignoreRegexes = [];
    private readonly List<Regex> _includeRegexes = [];

    public List<string> IgnorePatterns { get; } =
    [
        "package-lock.json",
        "pnpm-lock.yaml",
        "yarn.lock",
        "*.min.json",
        "*.min.js",
        "*.min.css",
        "*.map",
        "**/wwwroot/lib/**",
        "**/wwwroot/lib/*"
    ];

    public List<string> IgnoreDirectories { get; } =
    [
        ".git", ".codeatlas", "bin", "obj",
        "node_modules", "dist", ".next", "coverage", ".vs",
        "packages", "TestResults", "publish", "out", "temp", "tmp", "vendor", ".idea",
        "lib", "wwwroot/lib"
    ];

    public List<string> IncludePatterns { get; } = [];

    public bool WildcardIgnoreAll { get; private set; }

    /// <summary>
    /// Loads configuration from executable <c>appsettings.json</c> and workspace <c>.codeatlas/settings.json</c>.
    /// </summary>
    public static TraitsAppConfig Load(string? repoPath = null)
    {
        // ── 1. Declarations ──
        TraitsAppConfig config;
        string exeDir;
        string exeConfigPath;
        string[] repoConfigFiles;

        // ── 2. Preconditions & Guard Clauses ──
        config        = new TraitsAppConfig();
        exeDir        = AppContext.BaseDirectory;
        exeConfigPath = Path.Combine(exeDir, "appsettings.json");

        // ── 3. Intent-First Narrative ──
        MergeConfigFileQuietly(config, exeConfigPath);

        if (!string.IsNullOrWhiteSpace(repoPath) && Directory.Exists(repoPath))
        {
            repoConfigFiles =
            [
                Path.Combine(repoPath, ".codeatlas", "settings.json"),
                Path.Combine(repoPath, "appsettings.json")
            ];

            foreach (var cfgPath in repoConfigFiles)
            {
                MergeConfigFileQuietly(config, cfgPath);
            }
        }

        NormaliseAndCompilePatterns(config);

        // ── 4. Output / Return ──
        return config;
    }

    /// <summary>
    /// Evaluates whether a relative file path should be excluded according to the loaded
    /// <see cref="IgnorePatterns"/>, <see cref="IgnoreDirectories"/>, and <see cref="IncludePatterns"/>.
    /// </summary>
    public bool IsPathIgnored(string? relativeFilePath)
    {
        // ── 1. Declarations ──
        string normalisedPath;
        string[] segments;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(relativeFilePath))
        {
            return false;
        }

        if (WildcardIgnoreAll)
        {
            return true;
        }

        // ── 3. Intent-First Narrative ──
        normalisedPath = relativeFilePath.Trim().Replace('\\', '/').TrimStart('/');
        segments       = normalisedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var dirRule in IgnoreDirectories)
        {
            var normDir = dirRule.Trim().Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(normDir))
            {
                continue;
            }

            if (normDir.Contains('/'))
            {
                if (normalisedPath.StartsWith(normDir + "/", StringComparison.OrdinalIgnoreCase) ||
                    normalisedPath.Contains("/" + normDir + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (segments.SkipLast(1).Any(s => s.Equals(normDir, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (_includeRegexes.Count > 0 && !_includeRegexes.Any(rx => rx.IsMatch(normalisedPath)))
        {
            return true;
        }

        if (_ignoreRegexes.Any(rx => rx.IsMatch(normalisedPath)))
        {
            return true;
        }

        // ── 4. Output / Return ──
        return false;
    }

    private static void NormaliseAndCompilePatterns(TraitsAppConfig config)
    {
        // ── 1. Intent-First Narrative ──
        if (config.IncludePatterns.Count == 0 || config.IncludePatterns.Any(p => p.Trim() == "*"))
        {
            config.IncludePatterns.Clear();
        }

        if (config.IgnorePatterns.Any(p => p.Trim() is "*" or "**"))
        {
            config.WildcardIgnoreAll = true;
        }

        foreach (var pattern in config.IgnorePatterns)
        {
            var regex = CompileGlobRegex(pattern);
            if (regex is not null)
            {
                config._ignoreRegexes.Add(regex);
            }
        }

        foreach (var pattern in config.IncludePatterns)
        {
            var regex = CompileGlobRegex(pattern);
            if (regex is not null)
            {
                config._includeRegexes.Add(regex);
            }
        }
    }

    private static void MergeConfigFileQuietly(TraitsAppConfig config, string filePath)
    {
        // ── 1. Declarations ──
        string json;
        JsonElement root;

        // ── 2. Preconditions & Guard Clauses ──
        if (!File.Exists(filePath))
        {
            return;
        }

        // ── 3. Intent-First Narrative ──
        try
        {
            json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling     = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            root = doc.RootElement;

            ApplyJsonSection(config, root);

            string[] sectionNames = ["CodeAtlas", "codeAtlas", "CodeAtlas.Traits", "codeAtlas.Traits", "traits", "Traits"];
            foreach (var secName in sectionNames)
            {
                if (root.TryGetProperty(secName, out var section) && section.ValueKind == JsonValueKind.Object)
                {
                    ApplyJsonSection(config, section);
                }
            }
        }
        catch
        {
            // Ignore malformed config files and fall back to defaults.
        }
    }

    private static void ApplyJsonSection(TraitsAppConfig config, JsonElement element)
    {
        if ((element.TryGetProperty("Include", out var inc) ||
             element.TryGetProperty("include", out inc) ||
             element.TryGetProperty("IncludePatterns", out inc) ||
             element.TryGetProperty("includePatterns", out inc)) &&
            inc.ValueKind == JsonValueKind.Array)
        {
            AddPatterns(config.IncludePatterns, inc);
        }

        if ((element.TryGetProperty("Ignore", out var ign) ||
             element.TryGetProperty("ignore", out ign) ||
             element.TryGetProperty("IgnorePatterns", out ign) ||
             element.TryGetProperty("ignorePatterns", out ign)) &&
            ign.ValueKind == JsonValueKind.Array)
        {
            AddPatterns(config.IgnorePatterns, ign);
        }

        if ((element.TryGetProperty("IgnoreDirectories", out var dirs) ||
             element.TryGetProperty("ignoreDirectories", out dirs)) &&
            dirs.ValueKind == JsonValueKind.Array)
        {
            AddPatterns(config.IgnoreDirectories, dirs);
        }
    }

    private static void AddPatterns(List<string> targetList, JsonElement jsonArray)
    {
        foreach (var item in jsonArray.EnumerateArray())
        {
            var val = item.GetString();
            if (!string.IsNullOrWhiteSpace(val) && !targetList.Contains(val, StringComparer.OrdinalIgnoreCase))
            {
                targetList.Add(val.Trim());
            }
        }
    }

    private static Regex? CompileGlobRegex(string? rawGlob)
    {
        // ── 1. Declarations ──
        string glob;
        bool anchored;
        StringBuilder sb;
        int i;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(rawGlob))
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        glob     = rawGlob.Trim().Replace('\\', '/').TrimEnd('/');
        anchored = glob.StartsWith('/');
        if (anchored)
        {
            glob = glob[1..];
        }

        sb = new StringBuilder();
        sb.Append('^');

        if (!anchored && !glob.StartsWith("**/"))
        {
            sb.Append("(?:.*/)?");
        }

        i = 0;
        while (i < glob.Length)
        {
            var ch = glob[i];
            if (ch == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                if (i + 2 < glob.Length && glob[i + 2] == '/')
                {
                    sb.Append("(?:.*/)?");
                    i += 3;
                }
                else
                {
                    sb.Append(".*");
                    i += 2;
                }
            }
            else if (ch == '*')
            {
                sb.Append("[^/]*");
                i++;
            }
            else if (ch == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else
            {
                sb.Append(Regex.Escape(ch.ToString()));
                i++;
            }
        }

        sb.Append("(?:/.*)?$");

        // ── 4. Output / Return ──
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
