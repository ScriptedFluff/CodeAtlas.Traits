using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Core.AtlasBridge;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AtlasSymbolDto))]
[JsonSerializable(typeof(AtlasQueryResponseDto))]
internal sealed partial class AtlasBridgeJsonContext : JsonSerializerContext { }

/// <summary>
/// Abstraction for invoking <c>CodeAtlas</c> console operations so unit tests can mock or substitute it cleanly.
/// </summary>
public interface ICodeAtlasCliClient
{
    Task<bool> EnsureIndexedAsync(string repoRoot, string? customDbDir = null, CancellationToken ct = default);
    Task<IReadOnlyList<AtlasSymbolDto>> QueryAllSymbolsAsync(
        string repoRoot,
        string? language = null,
        string? customDbDir = null,
        CancellationToken ct = default);
}

/// <summary>
/// Invokes the external <c>CodeAtlas</c> console application to trigger indexing when <c>atlas.db</c>
/// is absent and to fetch symbol metadata in JSON format.
/// </summary>
public sealed class CodeAtlasCliClient : ICodeAtlasCliClient
{
    private readonly string? _explicitCliPath;

    public CodeAtlasCliClient(string? explicitCliPath = null)
    {
        _explicitCliPath = explicitCliPath;
    }

    /// <summary>
    /// Ensures <c>atlas.db</c> exists by invoking <c>codeatlas index &lt;repoRoot&gt;</c> when missing.
    /// </summary>
    public async Task<bool> EnsureIndexedAsync(
        string repoRoot,
        string? customDbDir = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string expectedAtlasDb;
        string? cliPath;
        List<string> args;
        ProcessExecutionResult result;

        // ── 2. Preconditions & Guard Clauses ──
        expectedAtlasDb = TraitsStorageConfig.ResolveExpectedAtlasDbPath(repoRoot, customDbDir);
        if (File.Exists(expectedAtlasDb))
        {
            return true;
        }

        cliPath = CodeAtlasLocator.ResolveCliPath(_explicitCliPath);
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            throw new InvalidOperationException(
                "Could not locate the CodeAtlas CLI executable. Run CodeAtlas once or set CODEATLAS_PATH.");
        }

        // ── 3. Intent-First Narrative ──
        args = ["index", repoRoot];
        if (!string.IsNullOrWhiteSpace(customDbDir))
        {
            args.Add("--db-dir");
            args.Add(customDbDir);
        }

        result = await ExecuteCliProcessAsync(cliPath, args, ct);

        // ── 4. Output / Return ──
        return result.ExitCode == 0 && File.Exists(expectedAtlasDb);
    }

    /// <summary>
    /// Queries <c>CodeAtlas</c> via <c>codeatlas query --json</c> with cursor pagination
    /// to retrieve symbols (optionally filtered by language).
    /// </summary>
    public async Task<IReadOnlyList<AtlasSymbolDto>> QueryAllSymbolsAsync(
        string repoRoot,
        string? language = null,
        string? customDbDir = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string? cliPath;
        string? normalisedLanguage;
        List<AtlasSymbolDto> allSymbols;
        long cursor;
        bool hasMore;
        const int pageSize = 500;

        // ── 2. Preconditions & Guard Clauses ──
        cliPath = CodeAtlasLocator.ResolveCliPath(_explicitCliPath);
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            throw new InvalidOperationException(
                "Could not locate the CodeAtlas CLI executable. Run CodeAtlas once or set CODEATLAS_PATH.");
        }

        // ── 3. Intent-First Narrative ──
        await EnsureIndexedAsync(repoRoot, customDbDir, ct);

        normalisedLanguage = LanguageNormalizer.Normalise(language);
        allSymbols         = [];
        cursor             = 0L;
        hasMore            = true;

        while (hasMore && !ct.IsCancellationRequested)
        {
            var pageResponse = await FetchSingleQueryPageAsync(
                cliPath,
                repoRoot,
                normalisedLanguage,
                customDbDir,
                pageSize,
                cursor,
                ct);

            if (pageResponse is null || pageResponse.Symbols.Count == 0)
            {
                break;
            }

            allSymbols.AddRange(pageResponse.Symbols);
            hasMore = pageResponse.HasMore && pageResponse.NextCursor > cursor;
            cursor  = pageResponse.NextCursor;
        }

        // ── 4. Output / Return ──
        return allSymbols;
    }

    private static async Task<AtlasQueryResponseDto?> FetchSingleQueryPageAsync(
        string cliPath,
        string repoRoot,
        string? normalisedLanguage,
        string? customDbDir,
        int limit,
        long cursor,
        CancellationToken ct)
    {
        // ── 1. Declarations ──
        List<string> args;
        ProcessExecutionResult processResult;

        // ── 2. Preconditions & Guard Clauses ──
        args =
        [
            "query",
            "--repo", repoRoot,
            "--limit", limit.ToString(),
            "--cursor", cursor.ToString(),
            "--json"
        ];

        // ── 3. Intent-First Narrative ──
        if (!string.IsNullOrWhiteSpace(normalisedLanguage))
        {
            args.Add("--lang");
            args.Add(normalisedLanguage);
        }

        if (!string.IsNullOrWhiteSpace(customDbDir))
        {
            args.Add("--db-dir");
            args.Add(customDbDir);
        }

        processResult = await ExecuteCliProcessAsync(cliPath, args, ct);
        if (processResult.ExitCode != 0 || string.IsNullOrWhiteSpace(processResult.StandardOutput))
        {
            return null;
        }

        // ── 4. Output / Return ──
        return TryDeserialiseQueryResponseQuietly(processResult.StandardOutput);
    }

    private static AtlasQueryResponseDto? TryDeserialiseQueryResponseQuietly(string rawStdout)
    {
        // ── 1. Declarations ──
        int jsonStartIndex;
        string jsonPayload;

        // ── 2. Preconditions & Guard Clauses ──
        jsonStartIndex = rawStdout.IndexOf('{');
        if (jsonStartIndex < 0)
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        jsonPayload = rawStdout[jsonStartIndex..].Trim();
        try
        {
            return JsonSerializer.Deserialize(
                jsonPayload,
                AtlasBridgeJsonContext.Default.AtlasQueryResponseDto);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ProcessExecutionResult> ExecuteCliProcessAsync(
        string cliPath,
        IReadOnlyList<string> args,
        CancellationToken ct)
    {
        // ── 1. Declarations ──
        ProcessStartInfo startInfo;
        string stdout;
        string stderr;

        // ── 2. Preconditions & Guard Clauses ──
        if (cliPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo = CreateProcessStartInfo("dotnet", [cliPath, .. args]);
        }
        else
        {
            startInfo = CreateProcessStartInfo(cliPath, args);
        }

        // ── 3. Intent-First Narrative ──
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start CodeAtlas process '{cliPath}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);
        stdout = await stdoutTask;
        stderr = await stderrTask;

        // ── 4. Output / Return ──
        return new ProcessExecutionResult(process.ExitCode, stdout, stderr);
    }

    private static ProcessStartInfo CreateProcessStartInfo(string fileName, IReadOnlyList<string> args)
    {
        // ── 1. Declarations ──
        ProcessStartInfo psi;

        // ── 2. Intent-First Narrative ──
        psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // ── 3. Output / Return ──
        return psi;
    }

    private sealed record ProcessExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
