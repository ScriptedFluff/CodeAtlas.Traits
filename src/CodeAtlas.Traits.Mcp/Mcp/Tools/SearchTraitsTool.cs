using System.ComponentModel;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;
using ModelContextProtocol.Server;

namespace CodeAtlas.Traits.Mcp.Tools;

public sealed record SearchTraitsResponse(
    string Status,
    string Message,
    ScanProgressSnapshot ScanProgress,
    TraitsCoverageSummary? Coverage,
    IReadOnlyList<TraitSearchMatch> Matches,
    LocalBuildingStyleSummary? LocalBuildingStyles = null);

public sealed record TopTagsResponse(
    string Status,
    string Message,
    ScanProgressSnapshot ScanProgress,
    TraitsCoverageSummary? Coverage,
    IReadOnlyList<TagUsageCount> TopTags,
    IReadOnlyList<TagCombinationCount>? TopCombinations = null);

[McpServerToolType]
public sealed class SearchTraitsTool(
    RepoContext repoContext,
    ScanOrchestrator scanOrchestrator)
{
    [McpServerTool(Name = "search_traits")]
    [Description("""
        Searches the CodeAtlas.Traits repository memory for classes, methods, and functions matching one or more capability, domain, or technology concepts (e.g. 'redis, sql, stored procedure' or 'retry, polly, c#').
        WORKFLOW 1 (Before Writing Code — Pattern & Reuse Discovery): Call this BEFORE implementing a new service, helper, or feature. Even if none of the returned candidates are directly reusable as-is, inspect the matches and 'LocalBuildingStyles' summary to learn the repository's local conventions (which directory/namespace similar code lives in, how dependencies like Redis/SQL/Retry are wrapped, and naming conventions) so new code feels like it belongs in the repository.
        Multi-concept queries automatically rank candidates by how many requested concepts they match (e.g. 3/3 -> 2/3 -> 1/3).
        Language names passed in tags (such as 'c#', 'csharp', 'typescript', 'powershell') automatically filter out non-matching language files via CodeAtlas.
        By default, completeOnlyResponse is false: if a cold repository scan is taking longer than 1.5 seconds, this tool returns 'scanning_in_progress' immediately so the AI knows the server has not timed out. Pass completeOnlyResponse: true to wait until scanning finishes.
        """)]
    public async Task<SearchTraitsResponse> SearchTraitsAsync(
        [Description("Comma-separated concepts/tags or JSON array to search for (e.g. 'redis, sql, stored procedure' or 'retry,polly,c#'). Multi-word concepts separated by commas are preserved automatically.")] string tags,
        [Description("Optional: filter by programming language (e.g. csharp, typescript, javascript, powershell).")] string? language = null,
        [Description("Optional: filter by symbol type (Class, Method, Function).")] string? symbolType = null,
        [Description("Optional: if true, returns only generic/reusable utility methods and classes.")] bool? onlyUtilities = null,
        [Description("Optional: if false (default), ranks candidates by number of matched concepts (e.g. 3/3 first, then 2/3, then 1/3); if true, requires all concepts when strict matches exist.")] bool matchAllTags = false,
        [Description("Optional: if true (default), expands tags using loaded thesaurus/dictionary synonym packs (e.g. 'sanitise' matches 'escape', 'clean', 'sanitize').")] bool useThesaurus = true,
        [Description("Optional: maximum number of matches to return (default 25).")] int limit = 25,
        [Description("Optional: if false (default), returns early with 'scanning_in_progress' if a cold scan takes >1.5s. Set true to block until scan completion.")] bool completeOnlyResponse = false,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        string[] parsedTagArray;
        string? resolvedLanguage;
        IReadOnlyList<string> capabilityTags;
        ScanProgressSnapshot scanSnapshot;
        ThesaurusEngine? thesaurus;
        IReadOnlyList<TraitSearchMatch> matches;
        LocalBuildingStyleSummary buildingStyles;
        TraitsCoverageSummary coverage;

        // ── 2. Preconditions & Guard Clauses ──
        effectiveRepo                      = ResolveTargetDirectory(targetDirectory, repoContext);
        workspace                          = WorkspacePathResolver.ResolveWorkspace(effectiveRepo, repoContext.CustomDbDir);
        parsedTagArray                     = ParseTagList(tags);
        (resolvedLanguage, capabilityTags) = LanguageNormalizer.SplitTagsAndLanguage(parsedTagArray, language);

        // ── 3. Intent-First Narrative ──
        // Forward the resolved language filter directly to CodeAtlas CLI so non-matching files are filtered out at the source.
        scanSnapshot = await scanOrchestrator.EnsureScannedAsync(
            workspace.WorkspaceRoot,
            completeOnlyResponse: completeOnlyResponse,
            forceRescan: false,
            customDbDir: repoContext.CustomDbDir,
            language: resolvedLanguage,
            ct: ct);

        if (scanSnapshot.State == ScanState.ScanningInProgress && !completeOnlyResponse)
        {
            return new SearchTraitsResponse(
                Status:       "scanning_in_progress",
                Message:      "CodeAtlas.Traits is currently scanning missing repository information in the background so this call does not time out. You may continue with other work or call again with completeOnlyResponse: true to wait for the full result.",
                ScanProgress: scanSnapshot,
                Coverage:     null,
                Matches:      []);
        }

        thesaurus = useThesaurus ? ThesaurusEngine.LoadForWorkspace(workspace.WorkspaceRoot) : null;

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        matches        = db.Traits.SearchByTraits(capabilityTags, resolvedLanguage, symbolType, onlyUtilities, matchAllTags, limit, useThesaurus, thesaurus);
        buildingStyles = CodeAtlas.Traits.Core.Db.Queries.TraitQueries.BuildLocalBuildingStyleSummary(matches, capabilityTags);
        coverage       = db.Traits.GetCoverageSummary(scanSnapshot.State == ScanState.ScanningInProgress, resolvedLanguage);

        // ── 4. Output / Return ──
        return new SearchTraitsResponse(
            Status:              "ok",
            Message:             $"Found {matches.Count} matching symbol(s) across {capabilityTags.Count} requested concept(s). {coverage.Notice}",
            ScanProgress:        scanSnapshot,
            Coverage:            coverage,
            Matches:             matches,
            LocalBuildingStyles: buildingStyles);
    }

    [McpServerTool(Name = "get_top_tags")]
    [Description("""
        Returns the most frequently used capability and technology tags (and recurring multi-trait combinations) across non-trivial symbols in the repository.
        Useful for discovering what capabilities, domain concepts, and libraries exist in the codebase, or spotting multi-trait capability duplication where multiple distinct bodies share the same combination of 3+ traits.
        """)]
    public async Task<TopTagsResponse> GetTopTagsAsync(
        [Description("Optional: filter by programming language (e.g. csharp, typescript, javascript, powershell).")] string? language = null,
        [Description("Optional: filter by symbol type (Class, Method, Function).")] string? symbolType = null,
        [Description("Optional: if true, counts tags only across generic/reusable utility methods and classes.")] bool? onlyUtilities = null,
        [Description("Optional: if true (default), also returns recurring multi-trait combinations (>= minCombinationTags).")] bool includeCombinations = true,
        [Description("Optional: minimum number of traits in a multi-trait combination (default 3).")] int minCombinationTags = 3,
        [Description("Optional: maximum number of tags to return (default 50, max 500).")] int limit = 50,
        [Description("Optional: if false (default), returns early with 'scanning_in_progress' if a cold scan takes >1.5s. Set true to block until scan completion.")] bool completeOnlyResponse = false,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        string? resolvedLanguage;
        ScanProgressSnapshot scanSnapshot;
        IReadOnlyList<TagUsageCount> topTags;
        IReadOnlyList<TagCombinationCount>? topCombinations;
        TraitsCoverageSummary coverage;

        // ── 2. Preconditions & Guard Clauses ──
        effectiveRepo    = ResolveTargetDirectory(targetDirectory, repoContext);
        workspace        = WorkspacePathResolver.ResolveWorkspace(effectiveRepo, repoContext.CustomDbDir);
        resolvedLanguage = LanguageNormalizer.Normalise(language);

        // ── 3. Intent-First Narrative ──
        scanSnapshot = await scanOrchestrator.EnsureScannedAsync(
            workspace.WorkspaceRoot,
            completeOnlyResponse: completeOnlyResponse,
            forceRescan: false,
            customDbDir: repoContext.CustomDbDir,
            language: resolvedLanguage,
            ct: ct);

        if (scanSnapshot.State == ScanState.ScanningInProgress && !completeOnlyResponse)
        {
            return new TopTagsResponse(
                Status:       "scanning_in_progress",
                Message:      "CodeAtlas.Traits is currently scanning missing repository information in the background. Call again with completeOnlyResponse: true to wait for the full result.",
                ScanProgress: scanSnapshot,
                Coverage:     null,
                TopTags:      []);
        }

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        topTags         = db.Traits.GetTopTags(resolvedLanguage, symbolType, onlyUtilities, limit);
        topCombinations = includeCombinations
            ? db.Traits.GetTopTagCombinations(resolvedLanguage, symbolType, onlyUtilities, minCombinationTags, limit)
            : null;
        coverage = db.Traits.GetCoverageSummary(scanSnapshot.State == ScanState.ScanningInProgress, resolvedLanguage);

        // ── 4. Output / Return ──
        return new TopTagsResponse(
            Status:          "ok",
            Message:         $"Returned top {topTags.Count} most used tag(s) and {topCombinations?.Count ?? 0} multi-trait combination(s). {coverage.Notice}",
            ScanProgress:    scanSnapshot,
            Coverage:        coverage,
            TopTags:         topTags,
            TopCombinations: topCombinations);
    }

    private static string ResolveTargetDirectory(string? targetDirectory, RepoContext repoContext)
    {
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            return targetDirectory;
        }

        if (!string.IsNullOrWhiteSpace(repoContext.RepoPath))
        {
            return repoContext.RepoPath;
        }

        return Environment.CurrentDirectory;
    }

    private static string[] ParseTagList(string? rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
        {
            return [];
        }

        var trimmed = rawTags.Trim().TrimStart('[').TrimEnd(']');
        if (trimmed.Contains(',') || trimmed.Contains(';'))
        {
            return trimmed
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.Trim('"', '\'', ' '))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToArray();
        }

        return trimmed
            .Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('"', '\'', ' '))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();
    }
}
