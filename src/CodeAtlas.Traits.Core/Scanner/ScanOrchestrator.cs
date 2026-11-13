using System.Collections.Concurrent;
using System.Diagnostics;
using CodeAtlas.Traits.Core.AtlasBridge;
using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Core.Scanner;

/// <summary>
/// Orchestrates background and synchronous symbol scanning from <c>CodeAtlas</c> into <c>traits.db</c>,
/// enforcing the <c>completeOnlyResponse</c> timeout guard so MCP clients never assume the server hung.
/// </summary>
public sealed class ScanOrchestrator
{
    public const int DefaultNonBlockingWaitMs = 1500;

    private readonly ICodeAtlasCliClient _atlasClient;
    private readonly ConcurrentDictionary<string, WorkspaceScanSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public ScanOrchestrator(ICodeAtlasCliClient? atlasClient = null)
    {
        _atlasClient = atlasClient ?? new CodeAtlasCliClient();
    }

    /// <summary>
    /// Ensures the repository symbols have been scanned into <c>traits.db</c>.
    /// When <paramref name="completeOnlyResponse"/> is <c>false</c> (the default), waits up to
    /// <paramref name="nonBlockingTimeoutMs"/> before returning a <c>ScanningInProgress</c> snapshot
    /// while the scan continues in the background.
    /// </summary>
    public async Task<ScanProgressSnapshot> EnsureScannedAsync(
        string repoRoot,
        bool completeOnlyResponse = false,
        bool forceRescan = false,
        string? customDbDir = null,
        int nonBlockingTimeoutMs = DefaultNonBlockingWaitMs,
        string? language = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;
        string? normalisedLanguage;
        string sessionKey;
        WorkspaceScanSession session;
        Task<ScanProgressSnapshot> activeTask;

        // ── 2. Preconditions & Guard Clauses ──
        workspace          = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        normalisedLanguage = LanguageNormalizer.Normalise(language);
        sessionKey         = string.IsNullOrWhiteSpace(normalisedLanguage)
            ? workspace.WorkspaceRoot
            : $"{workspace.WorkspaceRoot}::{normalisedLanguage}";

        session = _sessions.GetOrAdd(sessionKey, _ => new WorkspaceScanSession());

        if (!forceRescan &&
            (session.HasCompletedInitialScan || HasCompletedFullWorkspaceScan(workspace.WorkspaceRoot)) &&
            File.Exists(workspace.TraitsDbPath))
        {
            return session.GetSnapshot();
        }

        if (!forceRescan &&
            File.Exists(workspace.TraitsDbPath) &&
            TryReuseExistingDiskSnapshots(workspace, normalisedLanguage, session))
        {
            return session.GetSnapshot();
        }

        // ── 3. Intent-First Narrative ──
        activeTask = session.GetOrStartScanTask(() => ExecuteFullScanAsync(
            workspace,
            normalisedLanguage,
            customDbDir,
            session,
            CancellationToken.None));

        if (completeOnlyResponse)
        {
            return await activeTask.WaitAsync(ct);
        }

        var completedInTime = await WaitWithTimeoutAsync(activeTask, nonBlockingTimeoutMs, ct);
        if (completedInTime)
        {
            return await activeTask;
        }

        // ── 4. Output / Return ──
        return session.GetSnapshot();
    }

    /// <summary>
    /// Returns the current scan progress snapshot for a repository without starting a new scan.
    /// </summary>
    public ScanProgressSnapshot GetCurrentStatus(string repoRoot, string? customDbDir = null)
    {
        // ── 1. Declarations ──
        WorkspaceInfo workspace;

        // ── 2. Preconditions & Guard Clauses ──
        workspace = WorkspacePathResolver.ResolveWorkspace(repoRoot, customDbDir);
        if (_sessions.TryGetValue(workspace.WorkspaceRoot, out var session))
        {
            return session.GetSnapshot();
        }

        // ── 3. Output / Return ──
        return new ScanProgressSnapshot(
            State:                  ScanState.Idle,
            StageDescription:       "Idle (not scanned in this session).",
            SymbolsProcessed:       0,
            TotalSymbolsDiscovered: 0,
            ElapsedMs:              0);
    }

    private bool HasCompletedFullWorkspaceScan(string workspaceRoot)
    {
        return _sessions.TryGetValue(workspaceRoot, out var fullSession) && fullSession.HasCompletedInitialScan;
    }

    private static bool TryReuseExistingDiskSnapshots(
        WorkspaceInfo workspace,
        string? normalisedLanguage,
        WorkspaceScanSession session)
    {
        // ── 1. Declarations ──
        Stopwatch stopwatch;
        int totalSnapshots;
        int trackableSnapshots;
        int untaggedSnapshots;
        int autoTaggedCount;

        // ── 2. Intent-First Narrative ──
        try
        {
            stopwatch = Stopwatch.StartNew();
            using var db = new TraitsDb(workspace.TraitsDbPath);
            db.Migrate();

            (totalSnapshots, trackableSnapshots, untaggedSnapshots) = db.Traits.GetQuickSnapshotCounts(normalisedLanguage);
            if (totalSnapshots <= 0)
            {
                return false;
            }

            autoTaggedCount = 0;
            if (untaggedSnapshots > 0)
            {
                autoTaggedCount = db.Traits.AutoTagAllUntaggedSymbols(
                    workspace.WorkspaceRoot,
                    normalisedLanguage,
                    (processed, total) => session.UpdateProgress(
                        ScanState.ScanningInProgress,
                        $"Auto-tagging untagged symbols with CPU ONNX engine ({processed}/{total} unique bodies)...",
                        processed,
                        total,
                        stopwatch.ElapsedMilliseconds));
            }

            stopwatch.Stop();
            session.MarkCompletedFromExistingDb(
                totalSnapshots,
                trackableSnapshots,
                autoTaggedCount,
                stopwatch.ElapsedMilliseconds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ScanProgressSnapshot> ExecuteFullScanAsync(
        WorkspaceInfo workspace,
        string? normalisedLanguage,
        string? customDbDir,
        WorkspaceScanSession session,
        CancellationToken ct)
    {
        // ── 1. Declarations ──
        Stopwatch stopwatch;
        IReadOnlyList<AtlasSymbolDto> atlasSymbols;
        List<SymbolSnapshot> computedSnapshots;
        int autoTaggedCount;

        // ── 2. Intent-First Narrative ──
        stopwatch = Stopwatch.StartNew();
        session.UpdateProgress(ScanState.ScanningInProgress, "Invoking CodeAtlas index and querying symbol graph...", 0, 0, 0);

        try
        {
            atlasSymbols = await _atlasClient.QueryAllSymbolsAsync(
                workspace.WorkspaceRoot,
                language: normalisedLanguage,
                customDbDir: customDbDir,
                ct: ct);

            session.UpdateProgress(
                ScanState.ScanningInProgress,
                $"Computing content-addressable body hashes for {atlasSymbols.Count} symbols...",
                0,
                atlasSymbols.Count,
                stopwatch.ElapsedMilliseconds);

            computedSnapshots = ComputeAllFileSnapshots(workspace.WorkspaceRoot, atlasSymbols, session, stopwatch);

            autoTaggedCount = PersistSnapshotsAndAutoTagDatabase(
                workspace.WorkspaceRoot,
                workspace.TraitsDbPath,
                computedSnapshots,
                normalisedLanguage,
                session,
                stopwatch);

            stopwatch.Stop();
            session.MarkCompleted(computedSnapshots.Count, atlasSymbols.Count, autoTaggedCount, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            session.MarkFailed(ex.Message, stopwatch.ElapsedMilliseconds);
        }

        // ── 3. Output / Return ──
        return session.GetSnapshot();
    }

    private static List<SymbolSnapshot> ComputeAllFileSnapshots(
        string workspaceRoot,
        IReadOnlyList<AtlasSymbolDto> atlasSymbols,
        WorkspaceScanSession session,
        Stopwatch stopwatch)
    {
        // ── 1. Declarations ──
        List<SymbolSnapshot> allSnapshots;
        IEnumerable<IGrouping<string, AtlasSymbolDto>> symbolsByFile;
        int processedCount;

        // ── 2. Intent-First Narrative ──
        allSnapshots   = new List<SymbolSnapshot>(atlasSymbols.Count);
        symbolsByFile  = atlasSymbols.GroupBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase);
        processedCount = 0;

        foreach (var fileGroup in symbolsByFile)
        {
            var fileSymbols   = fileGroup.ToList();
            var fileSnapshots = BodyHasher.ComputeSnapshotsForFile(workspaceRoot, fileGroup.Key, fileSymbols);
            allSnapshots.AddRange(fileSnapshots);

            processedCount += fileSymbols.Count;
            session.UpdateProgress(
                ScanState.ScanningInProgress,
                $"Hashing symbols ({processedCount}/{atlasSymbols.Count})...",
                processedCount,
                atlasSymbols.Count,
                stopwatch.ElapsedMilliseconds);
        }

        // ── 3. Output / Return ──
        return allSnapshots;
    }

    private static int PersistSnapshotsAndAutoTagDatabase(
        string workspaceRoot,
        string traitsDbPath,
        IReadOnlyList<SymbolSnapshot> snapshots,
        string? scopedLanguage,
        WorkspaceScanSession session,
        Stopwatch stopwatch)
    {
        using var db = new TraitsDb(traitsDbPath);
        db.Migrate();
        db.Traits.ReplaceSymbolSnapshots(snapshots, scopedLanguage);

        return db.Traits.AutoTagAllUntaggedSymbols(
            workspaceRoot,
            scopedLanguage,
            (processed, total) => session.UpdateProgress(
                ScanState.ScanningInProgress,
                $"Auto-tagging untagged symbols with CPU ONNX engine ({processed}/{total} unique bodies)...",
                processed,
                total,
                stopwatch.ElapsedMilliseconds));
    }

    private static async Task<bool> WaitWithTimeoutAsync(
        Task task,
        int timeoutMs,
        CancellationToken ct)
    {
        // ── 1. Declarations ──
        Task delayTask;
        Task finished;

        // ── 2. Preconditions & Guard Clauses ──
        if (task.IsCompleted)
        {
            return true;
        }

        // ── 3. Intent-First Narrative ──
        delayTask = Task.Delay(Math.Max(50, timeoutMs), ct);
        finished  = await Task.WhenAny(task, delayTask);

        // ── 4. Output / Return ──
        return ReferenceEquals(finished, task);
    }

    private sealed class WorkspaceScanSession
    {
        private readonly object _gate = new();
        private Task<ScanProgressSnapshot>? _activeTask;
        private ScanState _state = ScanState.Idle;
        private string _stageDescription = "Idle";
        private int _symbolsProcessed;
        private int _totalSymbols;
        private long _elapsedMs;
        private string? _errorMessage;
        private bool _hasCompletedInitialScan;

        public bool HasCompletedInitialScan
        {
            get { lock (_gate) return _hasCompletedInitialScan; }
        }

        public Task<ScanProgressSnapshot> GetOrStartScanTask(Func<Task<ScanProgressSnapshot>> factory)
        {
            lock (_gate)
            {
                if (_activeTask is null || _activeTask.IsCompleted)
                {
                    _activeTask = Task.Run(factory);
                }

                return _activeTask;
            }
        }

        public void UpdateProgress(ScanState state, string stage, int processed, int total, long elapsedMs)
        {
            lock (_gate)
            {
                _state            = state;
                _stageDescription = stage;
                _symbolsProcessed = processed;
                _totalSymbols     = total;
                _elapsedMs        = elapsedMs;
                _errorMessage     = null;
            }
        }

        public void MarkCompleted(int snapshotsStored, int totalDiscovered, int autoTaggedBodies, long elapsedMs)
        {
            lock (_gate)
            {
                _state                   = ScanState.Ready;
                _stageDescription        = $"Scan complete ({snapshotsStored} symbols hashed, {autoTaggedBodies} unique bodies auto-tagged in {elapsedMs}ms).";
                _symbolsProcessed        = snapshotsStored;
                _totalSymbols            = totalDiscovered;
                _elapsedMs               = elapsedMs;
                _errorMessage            = null;
                _hasCompletedInitialScan = true;
            }
        }

        public void MarkCompletedFromExistingDb(int snapshotsStored, int trackableSnapshots, int autoTaggedBodies, long elapsedMs)
        {
            lock (_gate)
            {
                _state            = ScanState.Ready;
                _stageDescription = autoTaggedBodies > 0
                    ? $"Ready ({snapshotsStored} symbols cached in traits.db; auto-tagged {autoTaggedBodies} untagged unique bodies in {elapsedMs}ms)."
                    : $"Ready ({snapshotsStored} symbols cached in traits.db, {trackableSnapshots} non-trivial trackable; verified in {elapsedMs}ms).";
                _symbolsProcessed        = snapshotsStored;
                _totalSymbols            = snapshotsStored;
                _elapsedMs               = elapsedMs;
                _errorMessage            = null;
                _hasCompletedInitialScan = true;
            }
        }

        public void MarkFailed(string error, long elapsedMs)
        {
            lock (_gate)
            {
                _state            = ScanState.Failed;
                _stageDescription = $"Scan failed: {error}";
                _elapsedMs        = elapsedMs;
                _errorMessage     = error;
            }
        }

        public ScanProgressSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                return new ScanProgressSnapshot(
                    State:                  _state,
                    StageDescription:       _stageDescription,
                    SymbolsProcessed:       _symbolsProcessed,
                    TotalSymbolsDiscovered: _totalSymbols,
                    ElapsedMs:              _elapsedMs,
                    ErrorMessage:           _errorMessage);
            }
        }
    }
}
