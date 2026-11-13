namespace CodeAtlas.Traits.Core.Config;

/// <summary>
/// Resolves workspace root paths and SQLite database paths for <c>traits.db</c> and <c>atlas.db</c>.
/// </summary>
public static class TraitsStorageConfig
{
    public const string CodeAtlasFolderName = ".codeatlas";
    public const string TraitsDbFileName    = "traits.db";
    public const string AtlasDbFileName     = "atlas.db";

    /// <summary>
    /// Resolves the path to <c>traits.db</c> (owned by <c>CodeAtlas.Traits</c>).
    /// </summary>
    public static string ResolveTraitsDbPath(string repoRoot, string? customDbDir = null)
    {
        // ── 1. Declarations ──
        string normalisedRoot;
        string targetDir;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            repoRoot = Environment.CurrentDirectory;
        }

        // ── 3. Intent-First Narrative ──
        normalisedRoot = WorkspacePathResolver.NormalisePath(repoRoot);
        targetDir      = !string.IsNullOrWhiteSpace(customDbDir)
            ? Path.GetFullPath(customDbDir)
            : Path.Combine(normalisedRoot, CodeAtlasFolderName);

        Directory.CreateDirectory(targetDir);

        // ── 4. Output / Return ──
        return Path.Combine(targetDir, TraitsDbFileName);
    }

    /// <summary>
    /// Resolves the expected path to <c>atlas.db</c> (owned by <c>CodeAtlas</c>) to check if it exists.
    /// </summary>
    public static string ResolveExpectedAtlasDbPath(string repoRoot, string? customDbDir = null)
    {
        // ── 1. Declarations ──
        string normalisedRoot;
        string targetDir;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(repoRoot))
        {
            repoRoot = Environment.CurrentDirectory;
        }

        // ── 3. Intent-First Narrative ──
        normalisedRoot = WorkspacePathResolver.NormalisePath(repoRoot);
        targetDir      = !string.IsNullOrWhiteSpace(customDbDir)
            ? Path.GetFullPath(customDbDir)
            : Path.Combine(normalisedRoot, CodeAtlasFolderName);

        // ── 4. Output / Return ──
        return Path.Combine(targetDir, AtlasDbFileName);
    }
}
