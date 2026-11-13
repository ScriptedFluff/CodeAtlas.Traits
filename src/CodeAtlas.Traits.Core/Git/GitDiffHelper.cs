using System.Diagnostics;
using CodeAtlas.Traits.Core.Extensions;

namespace CodeAtlas.Traits.Core.Git;

/// <summary>
/// Resolves modified, staged, and untracked file paths in a Git repository for diff-scoped trait audits.
/// </summary>
public static class GitDiffHelper
{
    /// <summary>
    /// Returns normalised relative file paths touched in the working tree or since <paramref name="sinceCommit"/>.
    /// </summary>
    public static IReadOnlyList<string> GetModifiedRelativeFiles(
        string workspaceRoot,
        string? sinceCommit = null)
    {
        // ── 1. Declarations ──
        HashSet<string> uniqueFiles;
        IReadOnlyList<string> diffLines;
        IReadOnlyList<string> statusLines;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        uniqueFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        diffLines = RunGitCommandQuietly(
            workspaceRoot,
            string.IsNullOrWhiteSpace(sinceCommit)
                ? ["diff", "--name-only", "HEAD"]
                : ["diff", "--name-only", sinceCommit.Trim()]);

        foreach (var line in diffLines)
        {
            var norm = line.NormaliseRelativePath();
            if (!string.IsNullOrWhiteSpace(norm))
            {
                uniqueFiles.Add(norm);
            }
        }

        statusLines = RunGitCommandQuietly(workspaceRoot, ["status", "--porcelain"]);
        foreach (var line in statusLines)
        {
            var parsedPath = ParsePorcelainPath(line);
            if (!string.IsNullOrWhiteSpace(parsedPath))
            {
                uniqueFiles.Add(parsedPath);
            }
        }

        // ── 4. Output / Return ──
        return uniqueFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ParsePorcelainPath(string rawLine)
    {
        // ── 1. Declarations ──
        string pathSlice;
        int renameArrowIndex;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(rawLine) || rawLine.Length < 4)
        {
            return string.Empty;
        }

        // ── 3. Intent-First Narrative ──
        pathSlice        = rawLine[3..].Trim().Trim('"');
        renameArrowIndex = pathSlice.IndexOf(" -> ", StringComparison.Ordinal);
        if (renameArrowIndex >= 0)
        {
            pathSlice = pathSlice[(renameArrowIndex + 4)..].Trim().Trim('"');
        }

        // ── 4. Output / Return ──
        return pathSlice.NormaliseRelativePath();
    }

    private static IReadOnlyList<string> RunGitCommandQuietly(string workingDir, IReadOnlyList<string> args)
    {
        // ── 1. Declarations ──
        ProcessStartInfo psi;
        string stdout;

        // ── 2. Intent-First Narrative ──
        try
        {
            psi = new ProcessStartInfo("git")
            {
                WorkingDirectory       = workingDir,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return [];
            }

            stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            if (proc.ExitCode != 0)
            {
                return [];
            }

            return stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch
        {
            return [];
        }
    }
}
