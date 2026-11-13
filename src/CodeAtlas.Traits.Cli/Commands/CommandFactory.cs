using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Core.Onnx;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Commands;

/// <summary>
/// Builds the CLI command tree for <c>CodeAtlas.Traits</c> (<c>scan</c>, <c>query</c>, <c>similar</c>, <c>inspect</c>, <c>tag</c>, <c>untagged</c>, <c>duplicates</c>, <c>model</c>, <c>status</c>, <c>serve</c>).
/// </summary>
public static class CommandFactory
{
    public static RootCommand CreateRootCommand()
    {
        // ── 1. Declarations ──
        RootCommand rootCommand;

        // ── 2. Intent-First Narrative ──
        rootCommand = new RootCommand("CodeAtlas.Traits — The capability and behaviour layer for CodeAtlas");
        rootCommand.AddCommand(CreateScanCommand());
        rootCommand.AddCommand(CreateQueryCommand());
        rootCommand.AddCommand(CreateSimilarCommand());
        rootCommand.AddCommand(CreateInspectCommand());
        rootCommand.AddCommand(CreateTagCommand());
        rootCommand.AddCommand(CreateTagsCommand());
        rootCommand.AddCommand(CreateUntaggedCommand());
        rootCommand.AddCommand(CreateDuplicatesCommand());
        rootCommand.AddCommand(CreateModelCommand());
        rootCommand.AddCommand(CreateStatusCommand());
        rootCommand.AddCommand(CreateServeCommand());

        // ── 3. Output / Return ──
        return rootCommand;
    }

    private static Command CreateScanCommand()
    {
        // ── 1. Declarations ──
        Argument<DirectoryInfo> repoArg;
        Option<DirectoryInfo?> dbDirOpt;
        Option<bool> jsonOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        repoArg  = new Argument<DirectoryInfo>("repo-path", () => new DirectoryInfo(Environment.CurrentDirectory), "Repository root path to scan");
        dbDirOpt = new Option<DirectoryInfo?>("--db-dir", "Override directory for SQLite databases");
        jsonOpt  = new Option<bool>("--json", "Output result in JSON format");

        cmd = new Command("scan", "Trigger CodeAtlas sync and compute content-addressable body hashes into traits.db");
        cmd.AddArgument(repoArg);
        cmd.AddOption(dbDirOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (repo, dbDir, json) =>
        {
            await ExecuteScanCommandAsync(repo.FullName, dbDir?.FullName, json);
        }, repoArg, dbDirOpt, jsonOpt);

        // ── 3. Output / Return ──
        return cmd;
    }

    private static Command CreateQueryCommand()
    {
        // ── 1. Declarations ──
        Option<string> tagsOpt;
        Option<DirectoryInfo?> repoOpt;
        Option<DirectoryInfo?> dbDirOpt;
        Option<string?> langOpt;
        Option<string?> typeOpt;
        Option<bool?> utilityOpt;
        Option<int> limitOpt;
        Option<bool> jsonOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        tagsOpt    = new Option<string>(new[] { "--tags", "-t" }, () => string.Empty, "Comma-separated capability/technology/language tags (e.g. 'retry,polly,c#')");
        repoOpt    = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path to query");
        dbDirOpt   = new Option<DirectoryInfo?>("--db-dir", "Override directory for SQLite databases");
        langOpt    = new Option<string?>(new[] { "--language", "--lang" }, "Filter by programming language");
        typeOpt    = new Option<string?>("--type", "Filter by symbol type (Class, Method, Function)");
        utilityOpt = new Option<bool?>("--utility", "Filter by utility status (true/false)");
        limitOpt   = new Option<int>(new[] { "--limit", "-l" }, () => 25, "Maximum matches to return");
        jsonOpt    = new Option<bool>("--json", "Output matches as JSON");

        cmd = new Command("query", "Search symbols by capability and technology traits");
        cmd.AddOption(tagsOpt);
        cmd.AddOption(repoOpt);
        cmd.AddOption(dbDirOpt);
        cmd.AddOption(langOpt);
        cmd.AddOption(typeOpt);
        cmd.AddOption(utilityOpt);
        cmd.AddOption(limitOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (tags, repo, dbDir, lang, type, util, limit, json) =>
        {
            await ExecuteQueryCommandAsync(
                repo?.FullName ?? Environment.CurrentDirectory,
                dbDir?.FullName,
                tags,
                lang,
                type,
                util,
                limit,
                json);
        }, tagsOpt, repoOpt, dbDirOpt, langOpt, typeOpt, utilityOpt, limitOpt, jsonOpt);

        // ── 3. Output / Return ──
        return cmd;
    }

    private static Command CreateSimilarCommand()
    {
        // ── 1. Declarations ──
        Argument<string> locationArg;
        Option<DirectoryInfo?> repoOpt;
        Option<DirectoryInfo?> dbDirOpt;
        Option<int> limitOpt;
        Option<bool> jsonOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        locationArg = new Argument<string>("target", "Symbol reference (e.g. 'MyNamespace.MyClass:MyMethod' or 'MyClass:MyMethod') or file:line ('MyClass.cs:L35')");
        repoOpt     = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path");
        dbDirOpt    = new Option<DirectoryInfo?>("--db-dir", "Override directory for SQLite databases");
        limitOpt    = new Option<int>(new[] { "--limit", "-l" }, () => 25, "Maximum similar symbols to return");
        jsonOpt     = new Option<bool>("--json", "Output similar symbols as JSON");

        cmd = new Command("similar", "Find symbols similar in size and capability traits to a target symbol ('MyNamespace.MyClass:MyMethod') or location ('MyClass.cs:L35')");
        cmd.AddArgument(locationArg);
        cmd.AddOption(repoOpt);
        cmd.AddOption(dbDirOpt);
        cmd.AddOption(limitOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (location, repo, dbDir, limit, json) =>
        {
            await ExecuteSimilarCommandAsync(
                repo?.FullName ?? Environment.CurrentDirectory,
                dbDir?.FullName,
                location,
                limit,
                json);
        }, locationArg, repoOpt, dbDirOpt, limitOpt, jsonOpt);

        // ── 3. Output / Return ──
        return cmd;
    }

    private static Command CreateUntaggedCommand()
    {
        // ── 1. Declarations ──
        Option<DirectoryInfo?> repoOpt;
        Option<DirectoryInfo?> dbDirOpt;
        Option<string?> langOpt;
        Option<int> limitOpt;
        Option<bool> includeAutoOpt;
        Option<bool> jsonOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        repoOpt        = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path");
        dbDirOpt       = new Option<DirectoryInfo?>("--db-dir", "Override directory for SQLite databases");
        langOpt        = new Option<string?>(new[] { "--language", "--lang" }, "Filter by language");
        limitOpt       = new Option<int>(new[] { "--limit", "-l" }, () => 25, "Batch size");
        includeAutoOpt = new Option<bool>(new[] { "--include-auto", "-a" }, () => true, "Include baseline 'auto-cpu' tagged symbols awaiting detailed AI enrichment");
        jsonOpt        = new Option<bool>("--json", "Output candidates as JSON");

        cmd = new Command("untagged", "List untagged or baseline auto-cpu tagged non-trivial symbols awaiting detailed AI capability tagging");
        cmd.AddOption(repoOpt);
        cmd.AddOption(dbDirOpt);
        cmd.AddOption(langOpt);
        cmd.AddOption(limitOpt);
        cmd.AddOption(includeAutoOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (repo, dbDir, lang, limit, includeAuto, json) =>
        {
            await ExecuteUntaggedCommandAsync(repo?.FullName ?? Environment.CurrentDirectory, dbDir?.FullName, lang, limit, includeAuto, json);
        }, repoOpt, dbDirOpt, langOpt, limitOpt, includeAutoOpt, jsonOpt);

        // ── 3. Output / Return ──
        return cmd;
    }

    private static Command CreateTagsCommand()
    {
        // ── 1. Declarations ──
        Option<DirectoryInfo?> repoOpt;
        Option<DirectoryInfo?> dbDirOpt;
        Option<string?> langOpt;
        Option<string?> typeOpt;
        Option<bool?> utilityOpt;
        Option<bool> combosOpt;
        Option<int> minTagsOpt;
        Option<int> limitOpt;
        Option<bool> jsonOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        repoOpt    = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path");
        dbDirOpt   = new Option<DirectoryInfo?>("--db-dir", "Override directory for SQLite databases");
        langOpt    = new Option<string?>(new[] { "--language", "--lang" }, "Filter by programming language (e.g. csharp, typescript)");
        typeOpt    = new Option<string?>("--type", "Filter by symbol type (Class, Method, Function)");
        utilityOpt = new Option<bool?>("--utility", "Filter by utility status (true/false)");
        combosOpt  = new Option<bool>(new[] { "--combos", "-c" }, "Group by multi-trait combinations (2+ or 3+ shared traits) rather than single tags");
        minTagsOpt = new Option<int>("--min-tags", () => 3, "Minimum number of traits in a combination when --combos is enabled");
        limitOpt   = new Option<int>(new[] { "--limit", "-l" }, () => 50, "Maximum number of tags or combinations to return");
        jsonOpt    = new Option<bool>("--json", "Output tag frequency counts as JSON");

        cmd = new Command("tags", "List the most frequently used capability tags or multi-trait combinations (--combos) in the repository");
        cmd.AddOption(repoOpt);
        cmd.AddOption(dbDirOpt);
        cmd.AddOption(langOpt);
        cmd.AddOption(typeOpt);
        cmd.AddOption(utilityOpt);
        cmd.AddOption(combosOpt);
        cmd.AddOption(minTagsOpt);
        cmd.AddOption(limitOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (repo, dbDir, lang, type, utility, combos, minTags, limit) =>
        {
            await ExecuteTagsCommandAsync(
                repo?.FullName ?? Environment.CurrentDirectory,
                dbDir?.FullName,
                lang,
                type,
                utility,
                combos,
                minTags,
                limit,
                isJson: false);
        }, repoOpt, dbDirOpt, langOpt, typeOpt, utilityOpt, combosOpt, minTagsOpt, limitOpt);

        // ── 3. Output / Return ──
        return cmd;
    }

    private static Command CreateDuplicatesCommand()
    {
        // ── 1. Declarations ──
        Option<DirectoryInfo?> repoOpt;
        Option<DirectoryInfo?> dbDirOpt;
        Option<string?> langOpt;
        Option<int> limitOpt;
        Option<bool> jsonOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        repoOpt  = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path");
        dbDirOpt = new Option<DirectoryInfo?>("--db-dir", "Override directory for SQLite databases");
        langOpt  = new Option<string?>(new[] { "--language", "--lang" }, "Filter by language");
        limitOpt = new Option<int>(new[] { "--limit", "-l" }, () => 50, "Maximum duplicate groups");
        jsonOpt  = new Option<bool>("--json", "Output duplicates as JSON");

        cmd = new Command("duplicates", "Detect non-trivial methods and classes sharing identical body hashes");
        cmd.AddOption(repoOpt);
        cmd.AddOption(dbDirOpt);
        cmd.AddOption(langOpt);
        cmd.AddOption(limitOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (repo, dbDir, lang, limit, json) =>
        {
            await ExecuteDuplicatesCommandAsync(repo?.FullName ?? Environment.CurrentDirectory, dbDir?.FullName, lang, limit, json);
        }, repoOpt, dbDirOpt, langOpt, limitOpt, jsonOpt);

        // ── 3. Output / Return ──
        return cmd;
    }

    private static Command CreateStatusCommand()
    {
        // ── 1. Declarations ──
        Option<DirectoryInfo?> repoOpt;
        Option<DirectoryInfo?> dbDirOpt;
        Option<bool> jsonOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        repoOpt  = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path");
        dbDirOpt = new Option<DirectoryInfo?>("--db-dir", "Override directory for SQLite databases");
        jsonOpt  = new Option<bool>("--json", "Output status as JSON");

        cmd = new Command("status", "Show traits coverage and synchronisation status");
        cmd.AddOption(repoOpt);
        cmd.AddOption(dbDirOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (repo, dbDir, json) =>
        {
            await ExecuteScanCommandAsync(repo?.FullName ?? Environment.CurrentDirectory, dbDir?.FullName, json, rescan: false);
        }, repoOpt, dbDirOpt, jsonOpt);

        // ── 3. Output / Return ──
        return cmd;
    }

    private static Command CreateServeCommand()
    {
        // ── 1. Declarations ──
        Argument<DirectoryInfo?> repoArg;
        Option<DirectoryInfo?> repoOpt;
        Option<DirectoryInfo?> dbDirOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        repoArg  = new Argument<DirectoryInfo?>("repo-path", () => null, "Repository path to serve (optional)");
        repoOpt  = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path to serve (optional)");
        dbDirOpt = new Option<DirectoryInfo?>("--db-dir", "Override SQLite database directory");

        cmd = new Command("serve", "Start the CodeAtlas.Traits MCP stdio server");
        cmd.AddArgument(repoArg);
        cmd.AddOption(repoOpt);
        cmd.AddOption(dbDirOpt);

        cmd.SetHandler(async (context) =>
        {
            var rArg  = context.ParseResult.GetValueForArgument(repoArg);
            var rOpt  = context.ParseResult.GetValueForOption(repoOpt);
            var dbDir = context.ParseResult.GetValueForOption(dbDirOpt);
            await LaunchMcpServerSubprocessAsync(rArg?.FullName ?? rOpt?.FullName, dbDir?.FullName, context.GetCancellationToken());
        });

        // ── 3. Output / Return ──
        return cmd;
    }

    private static async Task ExecuteScanCommandAsync(
        string repoRoot,
        string? customDbDir,
        bool isJson,
        bool rescan = true)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;
        ScanOrchestrator orchestrator;
        ScanProgressSnapshot scanSnapshot;
        TraitsCoverageSummary coverage;

        // ── 2. Intent-First Narrative ──
        workspace    = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        orchestrator = new ScanOrchestrator();
        scanSnapshot = await orchestrator.EnsureScannedAsync(
            workspace.WorkspaceRoot,
            completeOnlyResponse: true,
            forceRescan: rescan,
            customDbDir: customDbDir);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();
        coverage = db.Traits.GetCoverageSummary(isScanInProgress: false);

        // ── 3. Output / Return ──
        if (isJson)
        {
            var dto = new CliStatusDto(workspace.WorkspaceRoot, workspace.TraitsDbPath, scanSnapshot, coverage);
            Console.WriteLine(JsonSerializer.Serialize(dto, CliJsonContext.Default.CliStatusDto));
            return;
        }

        Console.WriteLine($"Workspace: {workspace.WorkspaceRoot}");
        Console.WriteLine($"Traits DB: {workspace.TraitsDbPath}");
        Console.WriteLine($"Scan:      {scanSnapshot.StageDescription}");
        Console.WriteLine($"Coverage:  {coverage.Notice}");
    }

    private static async Task ExecuteQueryCommandAsync(
        string repoRoot,
        string? customDbDir,
        string rawTags,
        string? language,
        string? symbolType,
        bool? onlyUtilities,
        int limit,
        bool isJson)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;
        ScanOrchestrator orchestrator;
        string[] tagArray;
        string? resolvedLanguage;
        IReadOnlyList<string> capabilityTags;
        IReadOnlyList<TraitSearchMatch> matches;

        // ── 2. Intent-First Narrative ──
        workspace = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        tagArray  = string.IsNullOrWhiteSpace(rawTags)
            ? []
            : rawTags.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        (resolvedLanguage, capabilityTags) = LanguageNormalizer.SplitTagsAndLanguage(tagArray, language);

        orchestrator = new ScanOrchestrator();
        await orchestrator.EnsureScannedAsync(
            workspace.WorkspaceRoot,
            completeOnlyResponse: true,
            forceRescan: false,
            customDbDir: customDbDir,
            language: resolvedLanguage);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();
        matches = db.Traits.SearchByTraits(capabilityTags, resolvedLanguage, symbolType, onlyUtilities, matchAllTags: true, limit: limit);

        // ── 3. Output / Return ──
        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(matches, CliJsonContext.Default.IReadOnlyListTraitSearchMatch));
            return;
        }

        foreach (var m in matches)
        {
            var utilBadge = m.IsUtility ? " [UTILITY]" : "";
            Console.WriteLine($"[{m.SymbolType}] {m.SymbolName} ({m.Language}) | {m.FilePath}:{m.StartLine}-{m.EndLine}{utilBadge}");
            Console.WriteLine($"  Tags:    {string.Join(", ", m.Tags)}");
            Console.WriteLine($"  Summary: {m.Summary}");
        }
    }

    private static async Task ExecuteSimilarCommandAsync(
        string repoRoot,
        string? customDbDir,
        string rawTarget,
        int limit,
        bool isJson)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;
        ScanOrchestrator orchestrator;
        ThesaurusEngine thesaurus;
        SimilarToLocationResult result;

        // ── 2. Preconditions & Guard Clauses ──
        if (!rawTarget.TryParseFileLineReference(out _, out _) &&
            !rawTarget.TryParseSymbolTargetReference(out _, out _))
        {
            Console.Error.WriteLine($"Invalid target '{rawTarget}'. Expected 'MyNamespace.MyClass:MyMethod', 'MyClass:MyMethod', or 'MyClass.cs:L35'.");
            return;
        }

        // ── 3. Intent-First Narrative ──
        workspace    = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        orchestrator = new ScanOrchestrator();
        await orchestrator.EnsureScannedAsync(workspace.WorkspaceRoot, completeOnlyResponse: true, forceRescan: false, customDbDir: customDbDir);

        thesaurus = ThesaurusEngine.LoadForWorkspace(workspace.WorkspaceRoot);
        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        result = db.Traits.FindSimilarToTarget(rawTarget, thesaurus, limit);
        if (!result.TargetFound)
        {
            await orchestrator.EnsureScannedAsync(workspace.WorkspaceRoot, completeOnlyResponse: true, forceRescan: true, customDbDir: customDbDir);
            result = db.Traits.FindSimilarToTarget(rawTarget, thesaurus, limit);
        }

        // ── 4. Output / Return ──
        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.SimilarToLocationResult));
            return;
        }

        if (!result.TargetFound || result.TargetSymbol is null)
        {
            Console.WriteLine(result.Message);
            return;
        }

        var t = result.TargetSymbol;
        var autoBadge = result.WasAutoTagged ? " [AUTO-TAGGED]" : "";
        var qualifiedTarget = string.IsNullOrWhiteSpace(t.ClassName) ? t.SymbolName : $"{t.ClassName}:{t.SymbolName}";
        Console.WriteLine($"Target: [{t.SymbolType}] {qualifiedTarget} ({t.FilePath}:L{t.StartLine}-{t.EndLine}){autoBadge}");
        Console.WriteLine($"  Tags: {string.Join(", ", t.Tags)}");
        Console.WriteLine($"  {result.Message}");

        foreach (var sim in result.SimilarMatches)
        {
            var cand          = sim.MatchedSymbol;
            var dupBadge      = sim.IsExactBodyDuplicate ? " [EXACT DUPLICATE]" : "";
            var qualifiedCand = string.IsNullOrWhiteSpace(cand.ClassName) ? cand.SymbolName : $"{cand.ClassName}:{cand.SymbolName}";
            Console.WriteLine($"  -> [{sim.OverallSimilarityScore}% match]{dupBadge} [{cand.SymbolType}] {qualifiedCand} | {cand.FilePath}:L{cand.StartLine}-{cand.EndLine} ({sim.CandidateLineCount} lines, {sim.SizeSimilarityPercent}% size similarity)");
            Console.WriteLine($"     Shared concepts: {string.Join(", ", sim.SharedConcepts)} | All tags: {string.Join(", ", cand.Tags)}");
        }
    }

    private static async Task ExecuteUntaggedCommandAsync(
        string repoRoot,
        string? customDbDir,
        string? language,
        int limit,
        bool includeAuto,
        bool isJson)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;
        ScanOrchestrator orchestrator;
        IReadOnlyList<UntaggedSymbolCandidate> candidates;

        // ── 2. Intent-First Narrative ──
        workspace    = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        orchestrator = new ScanOrchestrator();
        await orchestrator.EnsureScannedAsync(workspace.WorkspaceRoot, completeOnlyResponse: true, forceRescan: false, customDbDir: customDbDir, language: language);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();
        candidates = db.Traits.GetUntaggedBatch(language, limit, includeAutoTagged: includeAuto);

        // ── 3. Output / Return ──
        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(candidates, CliJsonContext.Default.IReadOnlyListUntaggedSymbolCandidate));
            return;
        }

        foreach (var c in candidates)
        {
            var srcBadge = string.IsNullOrWhiteSpace(c.CurrentTagSource) ? "untagged" : c.CurrentTagSource;
            Console.WriteLine($"[{c.BodyHash}] ({srcBadge}) [{c.SymbolType}] {c.SymbolName} ({c.Language}) | {c.FilePath}:{c.StartLine}-{c.EndLine}");
        }
    }

    private static async Task ExecuteDuplicatesCommandAsync(
        string repoRoot,
        string? customDbDir,
        string? language,
        int limit,
        bool isJson)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;
        ScanOrchestrator orchestrator;
        IReadOnlyList<DuplicateBodyGroup> groups;

        // ── 2. Intent-First Narrative ──
        workspace    = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        orchestrator = new ScanOrchestrator();
        await orchestrator.EnsureScannedAsync(workspace.WorkspaceRoot, completeOnlyResponse: true, forceRescan: false, customDbDir: customDbDir, language: language);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();
        groups = db.Traits.FindExactBodyDuplicates(language, limit);

        // ── 3. Output / Return ──
        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(groups, CliJsonContext.Default.IReadOnlyListDuplicateBodyGroup));
            return;
        }

        foreach (var g in groups)
        {
            Console.WriteLine($"BodyHash {g.BodyHash} ({g.OccurrenceCount} occurrences):");
            foreach (var loc in g.Locations)
            {
                Console.WriteLine($"  - [{loc.SymbolType}] {loc.SymbolName} | {loc.FilePath}:{loc.StartLine}-{loc.EndLine}");
            }
        }
    }

    private static async Task LaunchMcpServerSubprocessAsync(
        string? repoPath,
        string? customDbDir,
        CancellationToken ct)
    {
        // ── 1. Declarations ──
        string baseDir;
        string serverExeName;
        string serverPath;
        ProcessStartInfo psi;

        // ── 2. Intent-First Narrative ──
        baseDir       = AppContext.BaseDirectory;
        serverExeName = OperatingSystem.IsWindows() ? "CodeAtlas.Traits.Mcp.exe" : "CodeAtlas.Traits.Mcp";
        serverPath    = Path.Combine(baseDir, serverExeName);

        psi = new ProcessStartInfo(serverPath)
        {
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };

        if (!string.IsNullOrWhiteSpace(repoPath))
        {
            psi.ArgumentList.Add("--repo");
            psi.ArgumentList.Add(repoPath);
        }

        if (!string.IsNullOrWhiteSpace(customDbDir))
        {
            psi.ArgumentList.Add("--db-dir");
            psi.ArgumentList.Add(customDbDir);
        }

        using var proc = Process.Start(psi);
        if (proc is not null)
        {
            await proc.WaitForExitAsync(ct);
        }
    }

    private static Command CreateInspectCommand()
    {
        // ── 1. Declarations ──
        Argument<string> targetArg;
        Option<DirectoryInfo?> repoOpt;
        Option<DirectoryInfo?> dbDirOpt;
        Option<bool> jsonOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        targetArg = new Argument<string>("target", "Symbol reference (e.g. 'MyNamespace.MyClass:MyMethod' or 'MyClass:MyMethod') or file:line ('MyClass.cs:L35')");
        repoOpt   = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path");
        dbDirOpt  = new Option<DirectoryInfo?>("--db-dir", "Override directory for SQLite databases");
        jsonOpt   = new Option<bool>("--json", "Output symbol analysis candidate as JSON");

        cmd = new Command("inspect", "Retrieve a symbol's body_hash, source body, and current traits so Gemini or Copilot can analyse it");
        cmd.AddArgument(targetArg);
        cmd.AddOption(repoOpt);
        cmd.AddOption(dbDirOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (target, repo, dbDir, json) =>
        {
            await ExecuteInspectCommandAsync(repo?.FullName ?? Environment.CurrentDirectory, dbDir?.FullName, target, json);
        }, targetArg, repoOpt, dbDirOpt, jsonOpt);

        // ── 3. Output / Return ──
        return cmd;
    }

    private static Command CreateTagCommand()
    {
        // ── 1. Declarations ──
        Argument<string> targetArg;
        Option<string> tagsOpt;
        Option<string> summaryOpt;
        Option<bool> utilOpt;
        Option<DirectoryInfo?> repoOpt;
        Option<DirectoryInfo?> dbDirOpt;
        Option<bool> jsonOpt;
        Command cmd;

        // ── 2. Intent-First Narrative ──
        targetArg  = new Argument<string>("target", "Symbol reference (e.g. 'MyNamespace.MyClass:MyMethod' or 'MyClass:MyMethod') or file:line ('MyClass.cs:L35')");
        tagsOpt    = new Option<string>(new[] { "--tags", "-t" }, "Comma-separated capability/technology tags (e.g. 'retry,backoff,jitter,polly')") { IsRequired = true };
        summaryOpt = new Option<string>(new[] { "--summary", "-s" }, () => string.Empty, "Concise 1-sentence behavioural summary");
        utilOpt    = new Option<bool>(new[] { "--utility", "-u" }, () => false, "Mark symbol as a reusable utility/helper");
        repoOpt    = new Option<DirectoryInfo?>(new[] { "--repo", "-r" }, "Repository path");
        dbDirOpt   = new Option<DirectoryInfo?>("--db-dir", "Override directory for SQLite databases");
        jsonOpt    = new Option<bool>("--json", "Output saved trait match as JSON");

        cmd = new Command("tag", "Insert or upgrade detailed AI traits for a method or class directly by symbol name or location");
        cmd.AddArgument(targetArg);
        cmd.AddOption(tagsOpt);
        cmd.AddOption(summaryOpt);
        cmd.AddOption(utilOpt);
        cmd.AddOption(repoOpt);
        cmd.AddOption(dbDirOpt);
        cmd.AddOption(jsonOpt);

        cmd.SetHandler(async (target, tags, summary, util, repo, dbDir, json) =>
        {
            await ExecuteTagCommandAsync(
                repo?.FullName ?? Environment.CurrentDirectory,
                dbDir?.FullName,
                target,
                tags,
                summary,
                util,
                json);
        }, targetArg, tagsOpt, summaryOpt, utilOpt, repoOpt, dbDirOpt, jsonOpt);

        // ── 3. Output / Return ──
        return cmd;
    }

    private static Command CreateModelCommand()
    {
        // ── 1. Declarations ──
        Command modelCmd;
        Command statusSub;
        Command downloadSub;
        Option<bool> jsonOpt;

        // ── 2. Intent-First Narrative ──
        jsonOpt  = new Option<bool>("--json", "Output model status as JSON");
        modelCmd = new Command("model", "Manage the local CPU-only ONNX semantic embedding model");

        statusSub = new Command("status", "Show active CPU ONNX model or built-in CPU vectoriser status");
        statusSub.AddOption(jsonOpt);
        statusSub.SetHandler((json) =>
        {
            var eng    = CpuSemanticEngine.LoadForWorkspace(Environment.CurrentDirectory);
            var status = eng.GetStatus();
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(status, CliJsonContext.Default.CpuModelStatusDto));
                return;
            }

            Console.WriteLine($"Engine Mode: {status.EngineMode}");
            Console.WriteLine($"ONNX Loaded: {status.IsOnnxModelLoaded}");
            Console.WriteLine($"Dimensions:  {status.EmbeddingDimensions}");
            Console.WriteLine($"Details:     {status.Description}");
        }, jsonOpt);

        downloadSub = new Command("download", "Download the quantised Int8 all-MiniLM-L6-v2 CPU ONNX model (~23 MB) into LocalApplicationData");
        downloadSub.SetHandler(async (context) =>
        {
            Console.WriteLine("Downloading quantised all-MiniLM-L6-v2 CPU ONNX model (~23 MB)...");
            var (modelPath, vocabPath) = await OnnxModelDirectoryResolver.DownloadDefaultMiniLmModelAsync(ct: context.GetCancellationToken());
            Console.WriteLine($"Model saved: {modelPath}");
            Console.WriteLine($"Vocab saved: {vocabPath}");
        });

        modelCmd.AddCommand(statusSub);
        modelCmd.AddCommand(downloadSub);

        // ── 3. Output / Return ──
        return modelCmd;
    }

    private static async Task ExecuteInspectCommandAsync(
        string repoRoot,
        string? customDbDir,
        string rawTarget,
        bool isJson)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;
        ScanOrchestrator orchestrator;
        UntaggedSymbolCandidate? candidate;

        // ── 2. Intent-First Narrative ──
        workspace    = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        orchestrator = new ScanOrchestrator();
        await orchestrator.EnsureScannedAsync(workspace.WorkspaceRoot, completeOnlyResponse: true, forceRescan: false, customDbDir: customDbDir);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        candidate = db.Traits.GetSymbolForAnalysis(rawTarget);
        if (candidate is null)
        {
            await orchestrator.EnsureScannedAsync(workspace.WorkspaceRoot, completeOnlyResponse: true, forceRescan: true, customDbDir: customDbDir);
            candidate = db.Traits.GetSymbolForAnalysis(rawTarget);
        }

        // ── 3. Output / Return ──
        if (candidate is null)
        {
            Console.Error.WriteLine($"No symbol found matching '{rawTarget}'.");
            return;
        }

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(candidate, CliJsonContext.Default.UntaggedSymbolCandidate));
            return;
        }

        Console.WriteLine($"Symbol:     [{candidate.SymbolType}] {candidate.ClassName}:{candidate.SymbolName} ({candidate.Language})");
        Console.WriteLine($"Location:   {candidate.FilePath}:L{candidate.StartLine}-{candidate.EndLine}");
        Console.WriteLine($"BodyHash:   {candidate.BodyHash}");
        Console.WriteLine($"TagSource:  {candidate.CurrentTagSource ?? "untagged"}");
        Console.WriteLine($"Tags:       {(candidate.CurrentTags is not null ? string.Join(", ", candidate.CurrentTags) : "(none)")}");
        Console.WriteLine($"Summary:    {candidate.CurrentSummary ?? "(none)"}");
        Console.WriteLine("--- Source ---");
        Console.WriteLine(candidate.ContentToAnalyse);
    }

    private static async Task ExecuteTagCommandAsync(
        string repoRoot,
        string? customDbDir,
        string rawTarget,
        string rawTags,
        string summary,
        bool isUtility,
        bool isJson)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;
        ScanOrchestrator orchestrator;
        string[] tagArray;
        TraitSearchMatch? match;

        // ── 2. Intent-First Narrative ──
        workspace    = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        orchestrator = new ScanOrchestrator();
        await orchestrator.EnsureScannedAsync(workspace.WorkspaceRoot, completeOnlyResponse: true, forceRescan: false, customDbDir: customDbDir);

        tagArray = string.IsNullOrWhiteSpace(rawTags)
            ? []
            : rawTags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        match = db.Traits.TagSymbolByTarget(rawTarget, tagArray, summary, isUtility, TraitEntry.SourceAiDetailed);
        if (match is null)
        {
            await orchestrator.EnsureScannedAsync(workspace.WorkspaceRoot, completeOnlyResponse: true, forceRescan: true, customDbDir: customDbDir);
            match = db.Traits.TagSymbolByTarget(rawTarget, tagArray, summary, isUtility, TraitEntry.SourceAiDetailed);
        }

        // ── 3. Output / Return ──
        if (match is null)
        {
            Console.Error.WriteLine($"No symbol found matching '{rawTarget}'.");
            return;
        }

        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(match, CliJsonContext.Default.TraitSearchMatch));
            return;
        }

        Console.WriteLine($"Saved detailed AI traits for [{match.SymbolType}] {match.ClassName}:{match.SymbolName} (body_hash: {match.BodyHash})");
        Console.WriteLine($"  Tags:    {string.Join(", ", match.Tags)}");
        Console.WriteLine($"  Summary: {match.Summary}");
    }

    private static async Task ExecuteTagsCommandAsync(
        string repoRoot,
        string? customDbDir,
        string? language,
        string? symbolType,
        bool? onlyUtilities,
        bool showCombinations,
        int minTags,
        int limit,
        bool isJson)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;
        ScanOrchestrator orchestrator;
        IReadOnlyList<TagUsageCount> topTags;
        IReadOnlyList<TagCombinationCount> topCombos;

        // ── 2. Intent-First Narrative ──
        workspace    = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        orchestrator = new ScanOrchestrator();
        await orchestrator.EnsureScannedAsync(
            workspace.WorkspaceRoot,
            completeOnlyResponse: true,
            forceRescan: false,
            customDbDir: customDbDir,
            language: language);

        using var db = new TraitsDb(workspace.TraitsDbPath);
        db.Migrate();

        if (showCombinations)
        {
            topCombos = db.Traits.GetTopTagCombinations(language, symbolType, onlyUtilities, minTags, limit);
            if (isJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(topCombos, CliJsonContext.Default.IReadOnlyListTagCombinationCount));
                return;
            }

            if (topCombos.Count == 0)
            {
                Console.WriteLine("No multi-trait combinations found.");
                return;
            }

            Console.WriteLine($"Top {topCombos.Count} recurring multi-trait combination(s) (>= {minTags} traits) in {workspace.WorkspaceRoot}:");
            for (var i = 0; i < topCombos.Count; i++)
            {
                var combo = topCombos[i];
                Console.WriteLine($"  {i + 1,3}. [{combo.UniqueBodyCount,4} unique bodies | {combo.SymbolCount,4} symbols] ({combo.TagCount} traits): {combo.TagSignature}");
            }

            return;
        }

        topTags = db.Traits.GetTopTags(language, symbolType, onlyUtilities, limit);

        // ── 3. Output / Return ──
        if (isJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(topTags, CliJsonContext.Default.IReadOnlyListTagUsageCount));
            return;
        }

        if (topTags.Count == 0)
        {
            Console.WriteLine("No tags found.");
            return;
        }

        Console.WriteLine($"Top {topTags.Count} most used tag(s) in {workspace.WorkspaceRoot}:");
        for (var i = 0; i < topTags.Count; i++)
        {
            var item = topTags[i];
            Console.WriteLine($"  {i + 1,3}. {item.Tag,-28} {item.SymbolCount,6} symbol(s) ({item.UniqueBodyCount} unique bodies)");
        }
    }
}
