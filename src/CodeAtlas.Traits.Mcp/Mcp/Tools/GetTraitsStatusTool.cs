using System.ComponentModel;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Core.Onnx;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;
using ModelContextProtocol.Server;

namespace CodeAtlas.Traits.Mcp.Tools;

public sealed record TraitsStatusResponse(
    string Status,
    string WorkspaceRoot,
    string TraitsDbPath,
    ScanProgressSnapshot ScanProgress,
    TraitsCoverageSummary Coverage,
    CpuModelStatusDto CpuSemanticEngine,
    IReadOnlyList<LoadedDictionaryInfo> LoadedDictionaries);

[McpServerToolType]
public sealed class GetTraitsStatusTool(
    RepoContext repoContext,
    ScanOrchestrator scanOrchestrator)
{
    [McpServerTool(Name = "get_traits_status")]
    [Description("""
        Returns the current synchronisation state, scan progress, and tagging coverage summary for the workspace.
        Can also trigger a rescan from CodeAtlas when forceRescan is true.
        """)]
    public async Task<TraitsStatusResponse> GetTraitsStatusAsync(
        [Description("Optional: filter coverage statistics by language (e.g. csharp, typescript, powershell).")] string? language = null,
        [Description("Optional: if true, triggers a fresh scan from CodeAtlas.")] bool forceRescan = false,
        [Description("Optional: if false (default), returns early with 'scanning_in_progress' if a scan takes >1.5s. Set true to wait until scanning completes.")] bool completeOnlyResponse = false,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        ScanProgressSnapshot scanSnapshot;
        TraitsCoverageSummary coverage;
        ThesaurusEngine thesaurus;
        string statusLabel;

        // ── 2. Preconditions & Guard Clauses ──
        effectiveRepo = !string.IsNullOrWhiteSpace(targetDirectory)
            ? targetDirectory
            : (repoContext.RepoPath ?? Environment.CurrentDirectory);

        workspace = WorkspacePathResolver.ResolveWorkspace(effectiveRepo, repoContext.CustomDbDir);

        // ── 3. Intent-First Narrative ──
        scanSnapshot = await scanOrchestrator.EnsureScannedAsync(
            workspace.WorkspaceRoot,
            completeOnlyResponse: completeOnlyResponse,
            forceRescan: forceRescan,
            customDbDir: repoContext.CustomDbDir,
            ct: ct);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        coverage    = db.Traits.GetCoverageSummary(scanSnapshot.State == ScanState.ScanningInProgress, language);
        thesaurus   = ThesaurusEngine.LoadForWorkspace(workspace.WorkspaceRoot);
        var cpuEng  = CpuSemanticEngine.LoadForWorkspace(workspace.WorkspaceRoot);
        statusLabel = scanSnapshot.State == ScanState.ScanningInProgress ? "scanning_in_progress" : "ok";

        // ── 4. Output / Return ──
        return new TraitsStatusResponse(
            Status:             statusLabel,
            WorkspaceRoot:      workspace.WorkspaceRoot,
            TraitsDbPath:       workspace.TraitsDbPath,
            ScanProgress:       scanSnapshot,
            Coverage:           coverage,
            CpuSemanticEngine:  cpuEng.GetStatus(),
            LoadedDictionaries: thesaurus.LoadedDictionaries);
    }
}
