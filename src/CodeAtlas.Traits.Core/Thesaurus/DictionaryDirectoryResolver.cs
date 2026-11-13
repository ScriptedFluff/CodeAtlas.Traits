namespace CodeAtlas.Traits.Core.Thesaurus;

/// <summary>
/// Identifies the discovery tier of a drop-in dictionary folder.
/// </summary>
public enum DictionarySourceTier
{
    App,
    User,
    Environment,
    Workspace
}

/// <summary>
/// Multi-tier directory resolver for discovering drop-in <c>*.json</c> dictionary/thesaurus packs across:
/// 1. App Tier (<c>&lt;AppBaseDirectory&gt;/Dictionaries</c> — ships with <c>en.json</c>)
/// 2. User Tier (<c>&lt;LocalApplicationData&gt;/CodeAtlas.Traits/Dictionaries</c>)
/// 3. Environment Tier (<c>CODEATLAS_TRAITS_DICTIONARIES_DIR</c>)
/// 4. Workspace Tier (<c>&lt;repoRoot&gt;/.codeatlas/dictionaries</c>)
/// </summary>
public sealed class DictionaryDirectoryResolver
{
    public const string DictionariesFolderName     = "Dictionaries";
    public const string RepoDictionariesFolderName = "dictionaries";
    public const string AppFolderName              = "CodeAtlas.Traits";
    public const string EnvVarDictionariesDir      = "CODEATLAS_TRAITS_DICTIONARIES_DIR";

    private readonly string? _customEnvDir;
    private readonly string? _customUserDir;
    private readonly string? _customAppDir;

    public DictionaryDirectoryResolver(
        string? customEnvDir = null,
        string? customUserDir = null,
        string? customAppDir = null)
    {
        _customEnvDir  = customEnvDir;
        _customUserDir = customUserDir;
        _customAppDir  = customAppDir;
    }

    /// <summary>
    /// Returns all active search directories in merge order (App -> User -> Environment -> Workspace).
    /// </summary>
    public IReadOnlyList<(DictionarySourceTier Tier, string DirectoryPath)> GetSearchDirectories(string? workspaceRoot = null)
    {
        // ── 1. Declarations ──
        List<(DictionarySourceTier Tier, string DirectoryPath)> directories;
        string appDir;
        string userDir;
        string? envDir;
        string? workspaceDir;

        // ── 2. Intent-First Narrative ──
        directories = [];

        appDir = GetAppDictionariesDirectory();
        directories.Add((DictionarySourceTier.App, appDir));

        userDir = GetUserDictionariesDirectory();
        if (!string.Equals(userDir, appDir, StringComparison.OrdinalIgnoreCase))
        {
            directories.Add((DictionarySourceTier.User, userDir));
        }

        envDir = GetEnvDictionariesDirectory();
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            directories.Add((DictionarySourceTier.Environment, envDir));
        }

        workspaceDir = GetWorkspaceDictionariesDirectory(workspaceRoot);
        if (!string.IsNullOrWhiteSpace(workspaceDir))
        {
            directories.Add((DictionarySourceTier.Workspace, workspaceDir));
        }

        // ── 3. Output / Return ──
        return directories;
    }

    public string GetAppDictionariesDirectory()
    {
        // ── 1. Preconditions & Guard Clauses ──
        if (!string.IsNullOrWhiteSpace(_customAppDir))
        {
            return Path.GetFullPath(_customAppDir);
        }

        // ── 2. Output / Return ──
        return Path.Combine(AppContext.BaseDirectory, DictionariesFolderName);
    }

    public string GetUserDictionariesDirectory()
    {
        // ── 1. Declarations ──
        string basePath;

        // ── 2. Preconditions & Guard Clauses ──
        if (!string.IsNullOrWhiteSpace(_customUserDir))
        {
            return Path.GetFullPath(_customUserDir);
        }

        // ── 3. Intent-First Narrative ──
        basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        // ── 4. Output / Return ──
        return Path.Combine(basePath, AppFolderName, DictionariesFolderName);
    }

    public string? GetEnvDictionariesDirectory()
    {
        // ── 1. Declarations ──
        string? rawEnv;

        // ── 2. Preconditions & Guard Clauses ──
        rawEnv = _customEnvDir ?? Environment.GetEnvironmentVariable(EnvVarDictionariesDir);
        if (string.IsNullOrWhiteSpace(rawEnv))
        {
            return null;
        }

        // ── 3. Output / Return ──
        return Path.GetFullPath(rawEnv);
    }

    public static string? GetWorkspaceDictionariesDirectory(string? workspaceRoot)
    {
        // ── 1. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }

        // ── 2. Output / Return ──
        return Path.Combine(Path.GetFullPath(workspaceRoot), ".codeatlas", RepoDictionariesFolderName);
    }
}
