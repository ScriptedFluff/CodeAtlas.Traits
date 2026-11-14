using System.ComponentModel;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;
using ModelContextProtocol.Server;

namespace CodeAtlas.Traits.Mcp.Tools;

public sealed record FindDuplicatesResponse(
    string Status,
    string Message,
    ScanProgressSnapshot ScanProgress,
    IReadOnlyList<DuplicateBodyGroup> DuplicateGroups,
    IReadOnlyList<SubsystemDuplicateCluster>? SubsystemClusters = null);

public sealed record FindSimilarTraitsResponse(
    string Status,
    string Message,
    ScanProgressSnapshot ScanProgress,
    IReadOnlyList<SemanticSimilarityPair> SimilarPairs);

public sealed record FindSimilarToLocationResponse(
    string Status,
    string Message,
    ScanProgressSnapshot ScanProgress,
    SimilarToLocationResult? Result);

[McpServerToolType]
public sealed class FindDuplicatesTool(
    RepoContext repoContext,
    ScanOrchestrator scanOrchestrator)
{
    [McpServerTool(Name = "find_duplicate_bodies")]
    [Description("""
        Detects exact duplicate and Type-2 renamed-clone methods, functions, or class skeletons across the codebase, plus file-level Subsystem Duplication Clusters.
        WORKFLOW 2 (Post-Generation 'Compiler Pass' Consolidation): Run this AFTER generating and verifying a working feature to consolidate duplicated private helpers, DTOs, or subsystem stubs into shared extensions or services.
        """)]
    public async Task<FindDuplicatesResponse> FindDuplicateBodiesAsync(
        [Description("Optional: filter by programming language (e.g. csharp, typescript, powershell).")] string? language = null,
        [Description("Optional: maximum number of duplicate groups to return (default 50).")] int limit = 50,
        [Description("Optional: if false (default), excludes interface and abstract method contract signatures without executable bodies.")] bool includeContracts = false,
        [Description("Optional: minimum statement/member weight required for standalone duplicate groups not belonging to a multi-symbol subsystem cluster (default 2).")] int minStatements = 2,
        [Description("Optional: if false (default), returns early with 'scanning_in_progress' if a cold scan takes >1.5s. Set true to wait until scanning completes.")] bool completeOnlyResponse = false,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        ScanProgressSnapshot scanSnapshot;
        IReadOnlyList<DuplicateBodyGroup> duplicateGroups;
        IReadOnlyList<SubsystemDuplicateCluster> subsystemClusters;

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
            return new FindDuplicatesResponse(
                Status:            "scanning_in_progress",
                Message:           "CodeAtlas.Traits is scanning missing repository information in the background. Call again with completeOnlyResponse: true to wait for completion.",
                ScanProgress:      scanSnapshot,
                DuplicateGroups:   [],
                SubsystemClusters: []);
        }

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        duplicateGroups   = db.Traits.FindExactBodyDuplicates(language, limit, includeContracts, minStatements, onlyActionableDebt: true, workspace.WorkspaceRoot);
        subsystemClusters = db.Traits.DetectSubsystemDuplicateClusters(language, includeContracts, onlyActionableDebt: true, workspace.WorkspaceRoot, limit);

        // ── 4. Output / Return ──
        return new FindDuplicatesResponse(
            Status:            "ok",
            Message:           $"Found {duplicateGroups.Count} duplicate body group(s) and {subsystemClusters.Count} subsystem cluster(s).",
            ScanProgress:      scanSnapshot,
            DuplicateGroups:   duplicateGroups,
            SubsystemClusters: subsystemClusters);
    }

    [McpServerTool(Name = "find_similar_traits")]
    [Description("""
        Finds semantic near-duplicate classes, methods, or functions that have DIFFERENT implementations (different body_hash) yet solve a very similar problem (sharing multiple overlapping capability and technology tags, e.g. GetCustomerCached vs LoadCustomerFromCache vs RetrieveCustomerUsingRedis).
        WORKFLOW 2 (Post-Generation 'Compiler Pass' Consolidation): Use this to surface semantic overlap across the repository that humans and symbol-name searches miss.
        """)]
    public async Task<FindSimilarTraitsResponse> FindSimilarTraitsAsync(
        [Description("Optional: filter by programming language (e.g. csharp, typescript, powershell).")] string? language = null,
        [Description("Optional: minimum number of shared capability tags required to flag a pair (default 3, min 2).")] int minSharedTags = 3,
        [Description("Optional: maximum number of similar pairs to return (default 50).")] int limit = 50,
        [Description("Optional: if false (default), returns early with 'scanning_in_progress' if a cold scan takes >1.5s. Set true to wait until scanning completes.")] bool completeOnlyResponse = false,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        ScanProgressSnapshot scanSnapshot;
        IReadOnlyList<SemanticSimilarityPair> similarPairs;

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
            return new FindSimilarTraitsResponse(
                Status:       "scanning_in_progress",
                Message:      "CodeAtlas.Traits is scanning missing repository information in the background. Call again with completeOnlyResponse: true to wait for completion.",
                ScanProgress: scanSnapshot,
                SimilarPairs: []);
        }

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        similarPairs = db.Traits.FindSemanticSimilarPairs(language, minSharedTags, limit);

        // ── 4. Output / Return ──
        return new FindSimilarTraitsResponse(
            Status:       "ok",
            Message:      $"Found {similarPairs.Count} semantically similar symbol pair(s) sharing >= {Math.Max(2, minSharedTags)} tags.",
            ScanProgress: scanSnapshot,
            SimilarPairs: similarPairs);
    }

    [McpServerTool(Name = "find_similar_to_location")]
    [Description("""
        Finds symbols in the repository that are similar in size, CPU ONNX semantic embedding, and capability/synonym traits to a target symbol ('MyNamespace.MyClass:MyMethod' or 'MyClass:MyMethod') or file:line location ('MyClass.cs:L35').
        Use this in BOTH workflows:
        - WORKFLOW 1 (Pattern Discovery): Point at an existing class/method to discover sibling implementations and learn the repository's local building style (where similar classes live, how dependencies are wrapped).
        - WORKFLOW 2 (Post-Generation Consolidation): Point at a newly written method or class after proving it works to check if the repository already has a semantically similar implementation to consolidate with.
        """)]
    public async Task<FindSimilarToLocationResponse> FindSimilarToLocationAsync(
        [Description("Target symbol reference (e.g. 'MyNamespace.MyClass:MyMethod', 'MyClass:MyMethod') or file and line reference ('MyClass.cs:L35').")] string location,
        [Description("Optional: maximum number of similar matches to return (default 25).")] int limit = 25,
        [Description("Optional: if false (default), returns early with 'scanning_in_progress' if a cold scan takes >1.5s. Set true to wait until scanning completes.")] bool completeOnlyResponse = false,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        ScanProgressSnapshot scanSnapshot;
        ThesaurusEngine thesaurus;
        SimilarToLocationResult result;

        // ── 2. Preconditions & Guard Clauses ──
        if (!location.TryParseFileLineReference(out _, out _) &&
            !location.TryParseSymbolTargetReference(out _, out _))
        {
            throw new ArgumentException(
                $"Invalid target '{location}'. Expected symbol reference 'MyNamespace.MyClass:MyMethod', 'MyClass:MyMethod', or location 'MyClass.cs:L35'.",
                nameof(location));
        }

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
            return new FindSimilarToLocationResponse(
                Status:       "scanning_in_progress",
                Message:      "CodeAtlas.Traits is scanning missing repository information in the background. Call again with completeOnlyResponse: true to wait for completion.",
                ScanProgress: scanSnapshot,
                Result:       null);
        }

        thesaurus = ThesaurusEngine.LoadForWorkspace(workspace.WorkspaceRoot);
        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        result = db.Traits.FindSimilarToTarget(location, thesaurus, limit);
        if (!result.TargetFound)
        {
            scanSnapshot = await scanOrchestrator.EnsureScannedAsync(
                workspace.WorkspaceRoot,
                completeOnlyResponse: true,
                forceRescan: true,
                customDbDir: repoContext.CustomDbDir,
                ct: ct);
            result = db.Traits.FindSimilarToTarget(location, thesaurus, limit);
        }

        // ── 4. Output / Return ──
        return new FindSimilarToLocationResponse(
            Status:       "ok",
            Message:      result.Message,
            ScanProgress: scanSnapshot,
            Result:       result);
    }
}
