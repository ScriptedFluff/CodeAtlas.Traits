using System.ComponentModel;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Models;
using ModelContextProtocol.Server;

namespace CodeAtlas.Traits.Mcp.Tools;

public sealed record UntaggedBatchResponse(
    string Status,
    string Message,
    ScanProgressSnapshot ScanProgress,
    TraitsCoverageSummary? Coverage,
    IReadOnlyList<UntaggedSymbolCandidate> Candidates);

public sealed record SymbolForAnalysisResponse(
    string Status,
    string Message,
    ScanProgressSnapshot ScanProgress,
    UntaggedSymbolCandidate? Candidate);

[McpServerToolType]
public sealed class GetUntaggedBatchTool(
    RepoContext repoContext,
    ScanOrchestrator scanOrchestrator)
{
    [McpServerTool]
    [Description("""
        Retrieves a batch of untagged (or baseline CPU auto-tagged) non-trivial classes, methods, and functions (deduplicated by content-addressable body_hash) that need detailed AI capability and technology tags.
        Classes are presented as compact contract skeletons (inheritance, constructors, and non-private method signatures only).
        After analysing the returned candidates, pass their body_hash, tags, summary, and isUtility flag to 'save_traits'.
        """)]
    public async Task<UntaggedBatchResponse> GetUntaggedBatchAsync(
        [Description("Optional: filter untagged symbols by language (e.g. csharp, typescript, powershell).")] string? language = null,
        [Description("Optional: number of untagged symbols to fetch in this batch (default 25, max 100).")] int batchSize = 25,
        [Description("Optional: if true (default), also includes symbols that only have baseline CPU auto-tags ('auto-cpu') so Gemini/Copilot can enrich them with detailed traits.")] bool includeAutoTagged = true,
        [Description("Optional: if true, triggers a fresh CodeAtlas delta scan before fetching untagged symbols.")] bool refreshScan = false,
        [Description("Optional: if false (default), returns early with 'scanning_in_progress' if a cold scan takes >1.5s. Set true to wait until scanning completes.")] bool completeOnlyResponse = false,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        ScanProgressSnapshot scanSnapshot;
        IReadOnlyList<UntaggedSymbolCandidate> candidates;
        TraitsCoverageSummary coverage;

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
            language: language,
            ct: ct);

        if (scanSnapshot.State == ScanState.ScanningInProgress && !completeOnlyResponse)
        {
            return new UntaggedBatchResponse(
                Status:       "scanning_in_progress",
                Message:      "CodeAtlas.Traits is scanning missing repository information in the background. Call again with completeOnlyResponse: true to wait for completion.",
                ScanProgress: scanSnapshot,
                Coverage:     null,
                Candidates:   []);
        }

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        candidates = db.Traits.GetUntaggedBatch(language, batchSize, includeAutoTagged);
        coverage   = db.Traits.GetCoverageSummary(scanSnapshot.State == ScanState.ScanningInProgress, language);

        // ── 4. Output / Return ──
        return new UntaggedBatchResponse(
            Status:       "ok",
            Message:      $"Returned {candidates.Count} symbol candidate(s) for AI analysis. {coverage.Notice}",
            ScanProgress: scanSnapshot,
            Coverage:     coverage,
            Candidates:   candidates);
    }

    [McpServerTool]
    [Description("""
        Retrieves the source body, signature, body_hash, and current traits of a specific symbol ('MyNamespace.MyClass:MyMethod', 'MyClass:MyMethod', or 'MyClass.cs:L35') so Gemini or GitHub Copilot can analyse it and insert detailed traits via 'tag_symbol' or 'save_traits'.
        """)]
    public async Task<SymbolForAnalysisResponse> GetSymbolForAnalysisAsync(
        [Description("Target symbol reference (e.g. 'MyNamespace.MyClass:MyMethod' or 'MyClass:MyMethod') or file:line ('MyClass.cs:L35').")] string target,
        [Description("Optional: if false (default), returns early with 'scanning_in_progress' if a cold scan takes >1.5s. Set true to wait until scanning completes.")] bool completeOnlyResponse = false,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        ScanProgressSnapshot scanSnapshot;
        UntaggedSymbolCandidate? candidate;

        // ── 2. Preconditions & Guard Clauses ──
        effectiveRepo = !string.IsNullOrWhiteSpace(targetDirectory)
            ? targetDirectory
            : (repoContext.RepoPath ?? Environment.CurrentDirectory);

        workspace = WorkspacePathResolver.ResolveWorkspace(effectiveRepo, repoContext.CustomDbDir);

        // ── 3. Intent-First Narrative ──
        scanSnapshot = await scanOrchestrator.EnsureScannedAsync(
            workspace.WorkspaceRoot,
            completeOnlyResponse: completeOnlyResponse,
            forceRescan: false,
            customDbDir: repoContext.CustomDbDir,
            ct: ct);

        if (scanSnapshot.State == ScanState.ScanningInProgress && !completeOnlyResponse)
        {
            return new SymbolForAnalysisResponse(
                Status:       "scanning_in_progress",
                Message:      "CodeAtlas.Traits is scanning missing repository information in the background. Call again with completeOnlyResponse: true to wait for completion.",
                ScanProgress: scanSnapshot,
                Candidate:    null);
        }

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        candidate = db.Traits.GetSymbolForAnalysis(target);
        if (candidate is null)
        {
            scanSnapshot = await scanOrchestrator.EnsureScannedAsync(
                workspace.WorkspaceRoot,
                completeOnlyResponse: true,
                forceRescan: true,
                customDbDir: repoContext.CustomDbDir,
                ct: ct);
            candidate = db.Traits.GetSymbolForAnalysis(target);
        }

        if (candidate is null)
        {
            return new SymbolForAnalysisResponse(
                Status:       "not_found",
                Message:      $"No symbol found matching '{target}'.",
                ScanProgress: scanSnapshot,
                Candidate:    null);
        }

        // ── 4. Output / Return ──
        return new SymbolForAnalysisResponse(
            Status:       "ok",
            Message:      $"Loaded symbol '{candidate.SymbolName}' (body_hash: {candidate.BodyHash}) for detailed AI trait analysis.",
            ScanProgress: scanSnapshot,
            Candidate:    candidate);
    }
}
