using System.ComponentModel;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Core.Git;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;
using ModelContextProtocol.Server;

namespace CodeAtlas.Traits.Mcp.Tools;

public sealed record AuditDiffTraitsResponse(
    string Status,
    string Message,
    ScanProgressSnapshot ScanProgress,
    DiffTraitAuditReport? AuditReport);

[McpServerToolType]
public sealed class AuditDiffTraitsTool(
    RepoContext repoContext,
    ScanOrchestrator scanOrchestrator)
{
    [McpServerTool(Name = "audit_diff_traits")]
    [Description("""
        Audits the current git diff (or explicit file paths) for post-implementation code review and cleanup:
        1. Lists any newly added/modified non-trivial symbols in the diff that are still untagged so the AI can tag them.
        2. Flags any tagged symbols in the diff that duplicate an existing body_hash or overlap in capability/synonym traits with existing shared services or utilities.
        """)]
    public async Task<AuditDiffTraitsResponse> AuditDiffTraitsAsync(
        [Description("Optional: git commit or ref to diff against (defaults to working tree + HEAD).")] string? sinceCommit = null,
        [Description("Optional: comma-separated relative file paths to audit instead of querying git diff.")] string? explicitFiles = null,
        [Description("Optional: if true, triggers a fresh CodeAtlas delta scan before auditing.")] bool refreshScan = true,
        [Description("Optional: if false (default), returns early with 'scanning_in_progress' if a scan takes >1.5s. Set true to wait until scanning completes.")] bool completeOnlyResponse = false,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        ScanProgressSnapshot scanSnapshot;
        IReadOnlyList<string> targetFiles;
        ThesaurusEngine thesaurus;
        DiffTraitAuditReport report;

        // ── 2. Preconditions & Guard Clauses ──
        effectiveRepo = !string.IsNullOrWhiteSpace(targetDirectory)
            ? targetDirectory
            : (repoContext.RepoPath ?? Environment.CurrentDirectory);

        workspace = WorkspacePathResolver.ResolveWorkspace(effectiveRepo, repoContext.CustomDbDir);

        // ── 3. Intent-First Narrative ──
        scanSnapshot = await scanOrchestrator.EnsureScannedAsync(
            workspace.WorkspaceRoot,
            completeOnlyResponse: completeOnlyResponse,
            forceRescan: refreshScan,
            customDbDir: repoContext.CustomDbDir,
            ct: ct);

        if (scanSnapshot.State == ScanState.ScanningInProgress && !completeOnlyResponse)
        {
            return new AuditDiffTraitsResponse(
                Status:       "scanning_in_progress",
                Message:      "CodeAtlas.Traits is scanning missing repository information in the background. Call again with completeOnlyResponse: true to wait for completion.",
                ScanProgress: scanSnapshot,
                AuditReport:  null);
        }

        targetFiles = ResolveFilesToAudit(workspace.WorkspaceRoot, sinceCommit, explicitFiles);
        thesaurus   = ThesaurusEngine.LoadForWorkspace(workspace.WorkspaceRoot);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        report = db.Traits.AuditFilesTraits(targetFiles, thesaurus);

        // ── 4. Output / Return ──
        return new AuditDiffTraitsResponse(
            Status:       "ok",
            Message:      $"Audited {report.ModifiedFiles.Count} file(s): {report.UntaggedSymbolsInDiff.Count} untagged symbol(s), {report.OverlappingSymbolsInDiff.Count} symbol(s) with existing capability overlaps.",
            ScanProgress: scanSnapshot,
            AuditReport:  report);
    }

    private static IReadOnlyList<string> ResolveFilesToAudit(
        string workspaceRoot,
        string? sinceCommit,
        string? explicitFiles)
    {
        if (!string.IsNullOrWhiteSpace(explicitFiles))
        {
            return explicitFiles
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        return GitDiffHelper.GetModifiedRelativeFiles(workspaceRoot, sinceCommit);
    }
}
