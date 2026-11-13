namespace CodeAtlas.Traits.Core.Config;

public sealed record WorkspaceInfo(
    string TargetDirectory,
    string WorkspaceRoot,
    string TraitsDbPath,
    string ExpectedAtlasDbPath);

/// <summary>
/// Resolves workspace boundaries (.codeatlas, .git, .sln, .slnx, package.json) matching CodeAtlas semantics.
/// </summary>
public static class WorkspacePathResolver
{
    private static readonly string[] BoundaryFiles =
    [
        "package.json",
        "tsconfig.json"
    ];

    /// <summary>
    /// Normalises a raw directory path, stripping surrounding quotes and trailing separators.
    /// </summary>
    public static string NormalisePath(string? rawPath)
    {
        // ── 1. Declarations ──
        string cleaned;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return Environment.CurrentDirectory;
        }

        // ── 3. Intent-First Narrative ──
        cleaned = rawPath.Trim().Trim('"', '\'', ' ', '\t');
        while (cleaned.Length > 0 && (cleaned[^1] == '"' || cleaned[^1] == '\''))
        {
            cleaned = cleaned[..^1].Trim();
        }

        // ── 4. Output / Return ──
        return Path.GetFullPath(cleaned).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Resolves the workspace root and database paths for a target directory.
    /// </summary>
    public static WorkspaceInfo ResolveWorkspace(string? targetDirectory, string? customDbDir = null)
    {
        // ── 1. Declarations ──
        string target;
        string workspaceRoot;
        string traitsDbPath;
        string expectedAtlasDbPath;

        // ── 2. Preconditions & Guard Clauses ──
        target = NormalisePath(targetDirectory);
        if (!Directory.Exists(target) && File.Exists(target))
        {
            target = Path.GetDirectoryName(target) ?? target;
        }

        // ── 3. Intent-First Narrative ──
        workspaceRoot       = FindWorkspaceRoot(target);
        traitsDbPath        = TraitsStorageConfig.ResolveTraitsDbPath(workspaceRoot, customDbDir);
        expectedAtlasDbPath = TraitsStorageConfig.ResolveExpectedAtlasDbPath(workspaceRoot, customDbDir);

        // ── 4. Output / Return ──
        return new WorkspaceInfo(target, workspaceRoot, traitsDbPath, expectedAtlasDbPath);
    }

    /// <summary>
    /// Walks up the directory tree to locate the highest boundary root.
    /// </summary>
    public static string FindWorkspaceRoot(string startDirectory)
    {
        // ── 1. Declarations ──
        string current;
        string highestMatch;

        // ── 2. Preconditions & Guard Clauses ──
        current      = NormalisePath(startDirectory);
        highestMatch = current;

        // ── 3. Intent-First Narrative ──
        while (!string.IsNullOrEmpty(current))
        {
            if (HasBoundaryMarker(current))
            {
                highestMatch = current;
            }

            var nextParent = Path.GetDirectoryName(current);
            if (nextParent is null || string.Equals(nextParent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = nextParent;
        }

        // ── 4. Output / Return ──
        return highestMatch;
    }

    private static bool HasBoundaryMarker(string dir)
    {
        // ── 1. Declarations ──
        bool hasSolutionFile;

        // ── 2. Preconditions & Guard Clauses ──
        if (!Directory.Exists(dir))
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        if (Directory.Exists(Path.Combine(dir, ".codeatlas")) ||
            Directory.Exists(Path.Combine(dir, ".git")) ||
            File.Exists(Path.Combine(dir, ".git")))
        {
            return true;
        }

        hasSolutionFile = TryHasSolutionFileQuietly(dir);
        if (hasSolutionFile)
        {
            return true;
        }

        foreach (var file in BoundaryFiles)
        {
            if (File.Exists(Path.Combine(dir, file)))
            {
                return true;
            }
        }

        // ── 4. Output / Return ──
        return false;
    }

    private static bool TryHasSolutionFileQuietly(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.sln").Any() ||
                   Directory.EnumerateFiles(dir, "*.slnx").Any();
        }
        catch
        {
            return false;
        }
    }
}
