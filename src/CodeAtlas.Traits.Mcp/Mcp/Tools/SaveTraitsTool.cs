using System.ComponentModel;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Models;
using ModelContextProtocol.Server;

namespace CodeAtlas.Traits.Mcp.Tools;

public sealed record TraitSubmissionItem(
    string BodyHash,
    string Summary,
    bool IsUtility,
    IReadOnlyList<string> Tags,
    string? Category = null,
    bool IsActionableDebt = true,
    string? ClusterName = null);

public sealed record SaveTraitsResponse(
    string Status,
    int SavedEntriesCount,
    TraitsCoverageSummary Coverage,
    string Message);

public sealed record TagSymbolResponse(
    string Status,
    string Message,
    TraitSearchMatch? TaggedSymbol,
    TraitsCoverageSummary? Coverage);

[McpServerToolType]
public sealed class SaveTraitsTool(
    RepoContext repoContext,
    ScanOrchestrator scanOrchestrator)
{
    [McpServerTool(Name = "save_traits")]
    [Description("""
        Saves detailed AI capability and technology tags, a concise 1-sentence behavioural summary, and an isUtility flag for a batch of symbols keyed by their content-addressable body_hash.
        Once saved, traits survive full CodeAtlas re-indexes and file moves automatically.
        """)]
    public SaveTraitsResponse SaveTraits(
        [Description("Batch of trait entries to save, each containing BodyHash, Summary, IsUtility, and Tags (e.g. ['retry', 'polly', 'async']).")] IReadOnlyList<TraitSubmissionItem> entries,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        List<TraitEntry> domainEntries;
        int savedCount;
        TraitsCoverageSummary coverage;

        // ── 2. Preconditions & Guard Clauses ──
        effectiveRepo = !string.IsNullOrWhiteSpace(targetDirectory)
            ? targetDirectory
            : (repoContext.RepoPath ?? Environment.CurrentDirectory);

        workspace     = WorkspacePathResolver.ResolveWorkspace(effectiveRepo, repoContext.CustomDbDir);
        domainEntries = new List<TraitEntry>(entries?.Count ?? 0);

        // ── 3. Intent-First Narrative ──
        if (entries is not null)
        {
            foreach (var item in entries)
            {
                if (string.IsNullOrWhiteSpace(item.BodyHash))
                {
                    continue;
                }

                domainEntries.Add(new TraitEntry
                {
                    BodyHash         = item.BodyHash.Trim(),
                    Summary          = item.Summary?.Trim() ?? string.Empty,
                    IsUtility        = item.IsUtility,
                    Tags             = item.Tags ?? [],
                    TagSource        = TraitEntry.SourceAiDetailed,
                    Category         = string.IsNullOrWhiteSpace(item.Category) ? null : item.Category.Trim(),
                    IsActionableDebt = item.IsActionableDebt,
                    ClusterName      = string.IsNullOrWhiteSpace(item.ClusterName) ? null : item.ClusterName.Trim()
                });
            }
        }

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        savedCount = db.Traits.SaveTraitEntries(domainEntries);
        coverage   = db.Traits.GetCoverageSummary(isScanInProgress: false);

        // ── 4. Output / Return ──
        return new SaveTraitsResponse(
            Status:            "ok",
            SavedEntriesCount: savedCount,
            Coverage:          coverage,
            Message:           $"Saved {savedCount} detailed AI trait entry/entries into traits.db. {coverage.Notice}");
    }

    [McpServerTool(Name = "tag_symbol")]
    [Description("""
        Directly inserts or upgrades detailed AI traits (tags, 1-sentence summary, and isUtility flag) for a specific method or class by its symbol reference ('MyNamespace.MyClass:MyMethod' or 'MyClass:MyMethod') or location ('MyClass.cs:L35') without needing to look up its body_hash first.
        Ideal for Gemini, GitHub Copilot, or Claude to tag methods as they write or review them.
        """)]
    public async Task<TagSymbolResponse> TagSymbolAsync(
        [Description("Target symbol reference (e.g. 'MyNamespace.MyClass:MyMethod', 'MyClass:MyMethod') or file:line ('MyClass.cs:L35').")] string target,
        [Description("Capability, behaviour, and technology tags (e.g. ['retry', 'backoff', 'jitter', 'polly']).")] IReadOnlyList<string> tags,
        [Description("Concise 1-sentence behavioural summary of what the symbol does.")] string summary,
        [Description("True if this symbol is a reusable utility, extension, or helper method/class.")] bool isUtility = false,
        [Description("Optional: semantic classification category ('contract', 'boilerplate', 'subsystem_duplicate', 'divergent_clone', 'utility').")] string? category = null,
        [Description("Optional: if false, marks this symbol/hash as intentional mirroring or boilerplate so future diff/duplicate audits suppress warnings (default true).")] bool isActionableDebt = true,
        [Description("Optional: subsystem cluster name linking related classes or methods (e.g. 'GraphQlStubCluster').")] string? clusterName = null,
        [Description("Optional: target repository/workspace directory (defaults to active workspace).")] string? targetDirectory = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string effectiveRepo;
        WorkspaceInfo workspace;
        TraitSearchMatch? taggedSymbol;
        TraitsCoverageSummary coverage;

        // ── 2. Preconditions & Guard Clauses ──
        effectiveRepo = !string.IsNullOrWhiteSpace(targetDirectory)
            ? targetDirectory
            : (repoContext.RepoPath ?? Environment.CurrentDirectory);

        workspace = WorkspacePathResolver.ResolveWorkspace(effectiveRepo, repoContext.CustomDbDir);

        // ── 3. Intent-First Narrative ──
        await scanOrchestrator.EnsureScannedAsync(
            workspace.WorkspaceRoot,
            completeOnlyResponse: true,
            forceRescan: false,
            customDbDir: repoContext.CustomDbDir,
            ct: ct);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        taggedSymbol = db.Traits.TagSymbolByTarget(target, tags ?? [], summary ?? string.Empty, isUtility, TraitEntry.SourceAiDetailed, category, isActionableDebt, clusterName);
        if (taggedSymbol is null)
        {
            await scanOrchestrator.EnsureScannedAsync(
                workspace.WorkspaceRoot,
                completeOnlyResponse: true,
                forceRescan: true,
                customDbDir: repoContext.CustomDbDir,
                ct: ct);
            taggedSymbol = db.Traits.TagSymbolByTarget(target, tags ?? [], summary ?? string.Empty, isUtility, TraitEntry.SourceAiDetailed, category, isActionableDebt, clusterName);
        }

        coverage = db.Traits.GetCoverageSummary(isScanInProgress: false);

        if (taggedSymbol is null)
        {
            return new TagSymbolResponse(
                Status:       "not_found",
                Message:      $"No symbol found matching '{target}'.",
                TaggedSymbol: null,
                Coverage:     coverage);
        }

        // ── 4. Output / Return ──
        return new TagSymbolResponse(
            Status:       "ok",
            Message:      $"Saved detailed AI traits for '{taggedSymbol.ClassName ?? ""}:{taggedSymbol.SymbolName}' (body_hash: {taggedSymbol.BodyHash}).",
            TaggedSymbol: taggedSymbol,
            Coverage:     coverage);
    }
}
