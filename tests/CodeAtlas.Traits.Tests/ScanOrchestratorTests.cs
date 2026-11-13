using CodeAtlas.Traits.Core.AtlasBridge;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Models;
using Microsoft.Data.Sqlite;

namespace CodeAtlas.Traits.Tests;

public sealed class ScanOrchestratorTests : IDisposable
{
    private readonly string _tempDir;

    public ScanOrchestratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ScanOrchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors.
        }
    }

    [Fact]
    public async Task EnsureScannedAsync_WhenCompleteOnlyResponseIsFalse_ReturnsScanningInProgressThenCompletes()
    {
        // ── 1. Declarations ──
        DelayedFakeAtlasClient fakeClient;
        ScanOrchestrator orchestrator;
        ScanProgressSnapshot initialResponse;
        ScanProgressSnapshot completedResponse;

        // ── 2. Intent-First Narrative ──
        File.WriteAllText(
            Path.Combine(_tempDir, "Sample.cs"),
            "public class Sample { public int Calculate(int x) { var y = x * 2; return y + 10; } }");

        fakeClient   = new DelayedFakeAtlasClient(delayMs: 400);
        orchestrator = new ScanOrchestrator(fakeClient);

        // Call with completeOnlyResponse = false and a 50ms timeout threshold:
        initialResponse = await orchestrator.EnsureScannedAsync(
            _tempDir,
            completeOnlyResponse: false,
            nonBlockingTimeoutMs: 50);

        // Now call with completeOnlyResponse = true to wait for the in-flight background scan to finish:
        completedResponse = await orchestrator.EnsureScannedAsync(
            _tempDir,
            completeOnlyResponse: true);

        // ── 3. Output / Assertions ──
        Assert.Equal(ScanState.ScanningInProgress, initialResponse.State);
        Assert.Equal(ScanState.Ready, completedResponse.State);
        Assert.True(File.Exists(TraitsStorageConfig.ResolveTraitsDbPath(_tempDir)));
    }

    [Fact]
    public async Task EndToEnd_InvokesRealCodeAtlasCli_WhenAtlasDbDoesNotExist()
    {
        // ── 1. Declarations ──
        string? resolvedCli;
        string sampleCode;
        ScanOrchestrator orchestrator;
        ScanProgressSnapshot snapshot;
        string expectedAtlasDb;
        string expectedTraitsDb;

        // ── 2. Preconditions & Guard Clauses ──
        resolvedCli = CodeAtlasLocator.ResolveCliPath();
        Assert.False(string.IsNullOrWhiteSpace(resolvedCli), "Expected CodeAtlasLocator to find the sibling CodeAtlas CLI build.");

        // ── 3. Intent-First Narrative ──
        sampleCode = """
            namespace Demo;

            public class ResilienceHelper
            {
                public async Task ExecuteWithRetryAsync(Func<Task> action, int maxAttempts)
                {
                    for (var attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        await action();
                    }
                }
            }
            """;

        File.WriteAllText(Path.Combine(_tempDir, "ResilienceHelper.cs"), sampleCode);

        expectedAtlasDb  = TraitsStorageConfig.ResolveExpectedAtlasDbPath(_tempDir);
        expectedTraitsDb = TraitsStorageConfig.ResolveTraitsDbPath(_tempDir);
        Assert.False(File.Exists(expectedAtlasDb));

        orchestrator = new ScanOrchestrator(new CodeAtlasCliClient(resolvedCli));
        snapshot     = await orchestrator.EnsureScannedAsync(_tempDir, completeOnlyResponse: true);

        // ── 4. Output / Assertions ──
        Assert.Equal(ScanState.Ready, snapshot.State);
        Assert.True(File.Exists(expectedAtlasDb), "CodeAtlas.Traits should have triggered CodeAtlas to create atlas.db.");
        Assert.True(File.Exists(expectedTraitsDb), "CodeAtlas.Traits should have created traits.db.");
        Assert.True(snapshot.SymbolsProcessed >= 2);
    }

    private sealed class DelayedFakeAtlasClient(int delayMs) : ICodeAtlasCliClient
    {
        public async Task<bool> EnsureIndexedAsync(string repoRoot, string? customDbDir = null, CancellationToken ct = default)
        {
            await Task.Delay(delayMs, ct);
            return true;
        }

        public async Task<IReadOnlyList<AtlasSymbolDto>> QueryAllSymbolsAsync(
            string repoRoot,
            string? language = null,
            string? customDbDir = null,
            CancellationToken ct = default)
        {
            await Task.Delay(delayMs, ct);
            return
            [
                new AtlasSymbolDto(
                    Id:         1,
                    Name:       "Calculate",
                    Type:       "Method",
                    ClassName:  "Sample",
                    Language:   "csharp",
                    FilePath:   "Sample.cs",
                    StartLine:  1,
                    EndLine:    1,
                    StartByte:  22,
                    EndByte:    83,
                    Signature:  "int Calculate(int x)",
                    DocComment: null,
                    IsLive:     false)
            ];
        }
    }
}
