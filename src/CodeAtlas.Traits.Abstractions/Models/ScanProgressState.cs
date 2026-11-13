namespace CodeAtlas.Traits.Models;

/// <summary>
/// Represents the current state of repository scanning and hash synchronisation.
/// </summary>
public enum ScanState
{
    Idle,
    ScanningInProgress,
    Ready,
    Failed
}

/// <summary>
/// Detailed progress snapshot reported when <c>completeOnlyResponse</c> is <c>false</c>
/// and a cold or delta scan is running in the background.
/// </summary>
public sealed record ScanProgressSnapshot(
    ScanState State,
    string StageDescription,
    int SymbolsProcessed,
    int TotalSymbolsDiscovered,
    long ElapsedMs,
    string? ErrorMessage = null);

/// <summary>
/// Coverage summary returned alongside trait queries so AI agents know whether
/// any newly added/modified symbols are still awaiting tagging.
/// </summary>
public sealed record TraitsCoverageSummary(
    int TotalTrackableSymbols,
    int TaggedSymbols,
    int UntaggedSymbols,
    int TrivialSkippedSymbols,
    bool IsScanInProgress,
    string Notice);
