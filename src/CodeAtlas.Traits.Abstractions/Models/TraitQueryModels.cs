namespace CodeAtlas.Traits.Models;

/// <summary>
/// Represents a matched symbol with its traits returned by <c>search_traits</c>.
/// </summary>
public sealed record TraitSearchMatch(
    string SymbolName,
    string SymbolType,
    string? ClassName,
    string Language,
    string FilePath,
    int StartLine,
    int EndLine,
    string? Signature,
    string BodyHash,
    string Summary,
    bool IsUtility,
    IReadOnlyList<string> Tags,
    string TagSource = TraitEntry.SourceAiDetailed,
    string? Category = null,
    bool IsActionableDebt = true,
    string? ClusterName = null,
    int MatchedConceptCount = 0,
    int TotalRequestedConcepts = 0,
    IReadOnlyList<string>? MatchedConcepts = null);

/// <summary>
/// Summarises the repository's local architectural conventions ("local building styles") inferred from <c>search_traits</c> matches:
/// where similar code lives, how enclosing classes/methods are structured, and recurring companion traits.
/// </summary>
public sealed record LocalBuildingStyleSummary(
    IReadOnlyList<string> CommonDirectories,
    IReadOnlyList<string> CommonClassConventions,
    IReadOnlyList<string> CompanionTraits,
    string Guidance);

/// <summary>
/// Represents an untagged (or baseline CPU auto-tagged) symbol candidate ready for an AI agent (Gemini, Copilot, Claude) to analyse and enrich with detailed traits.
/// </summary>
public sealed record UntaggedSymbolCandidate(
    string BodyHash,
    string SymbolName,
    string SymbolType,
    string? ClassName,
    string Language,
    string FilePath,
    int StartLine,
    int EndLine,
    string? Signature,
    string? DocComment,
    string ContentToAnalyse,
    string? CurrentTagSource = null,
    IReadOnlyList<string>? CurrentTags = null,
    string? CurrentSummary = null,
    bool IsUtility = false);

/// <summary>
/// Represents a group of symbols that share an identical <see cref="BodyHash"/>
/// (exact structural/body duplicates across the codebase).
/// </summary>
public sealed record DuplicateBodyGroup(
    string BodyHash,
    int OccurrenceCount,
    string? Summary,
    IReadOnlyList<string> Tags,
    IReadOnlyList<DuplicateSymbolLocation> Locations,
    bool HasExecutableBody = true,
    int StatementCount = 1,
    string? Category = null,
    bool IsActionableDebt = true,
    string? ClusterName = null,
    bool IsRenamedClone = false,
    string? StructuralHash = null);

/// <summary>
/// Represents a file-pair or subsystem cluster where multiple sibling symbols in <see cref="PrimaryFilePath"/>
/// are duplicated in <see cref="SecondaryFilePath"/> (e.g. copied DTO/stub hierarchies or shared utility files).
/// </summary>
public sealed record SubsystemDuplicateCluster(
    string PrimaryFilePath,
    string SecondaryFilePath,
    int IdenticalSymbolCount,
    IReadOnlyList<string> SymbolNames,
    IReadOnlyList<string> BodyHashes,
    string Summary);

/// <summary>
/// Location of one symbol instance within a <see cref="DuplicateBodyGroup"/>.
/// </summary>
public sealed record DuplicateSymbolLocation(
    string SymbolName,
    string SymbolType,
    string? ClassName,
    string Language,
    string FilePath,
    int StartLine,
    int EndLine,
    string? Signature);

/// <summary>
/// Represents a pair of distinct symbol implementations (<c>BodyHashA != BodyHashB</c>)
/// that share multiple overlapping capability/technology tags (semantic near-duplicates).
/// </summary>
public sealed record SemanticSimilarityPair(
    int SharedTagCount,
    IReadOnlyList<string> SharedTags,
    TraitSearchMatch PrimarySymbol,
    TraitSearchMatch SimilarSymbol);

/// <summary>
/// Audit entry for a symbol inside a git-modified file that duplicates or overlaps with existing codebase capabilities.
/// </summary>
public sealed record DiffSymbolAuditEntry(
    TraitSearchMatch DiffSymbol,
    IReadOnlyList<DuplicateSymbolLocation> ExactBodyDuplicates,
    IReadOnlyList<TraitSearchMatch> OverlappingExistingSymbols);

/// <summary>
/// Comprehensive audit report for git-modified files in the active working tree or commit range.
/// </summary>
public sealed record DiffTraitAuditReport(
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<UntaggedSymbolCandidate> UntaggedSymbolsInDiff,
    IReadOnlyList<DiffSymbolAuditEntry> OverlappingSymbolsInDiff);

/// <summary>
/// Represents a candidate symbol found to be similar in capability tags, CPU ONNX semantic embedding, and size to a target symbol.
/// </summary>
public sealed record LocationSimilarityMatch(
    TraitSearchMatch MatchedSymbol,
    bool IsExactBodyDuplicate,
    int SharedConceptCount,
    IReadOnlyList<string> SharedConcepts,
    int TargetLineCount,
    int CandidateLineCount,
    int SizeSimilarityPercent,
    int OverallSimilarityScore,
    int EmbeddingSimilarityPercent = 0,
    bool IsRenamedClone = false);

/// <summary>
/// Result of querying for symbols similar to a specific symbol (<c>MyNamespace.MyClass:MyMethod</c>) or <c>file:line</c> location (<c>MyClass.cs:L35</c>).
/// </summary>
public sealed record SimilarToLocationResult(
    bool TargetFound,
    bool WasAutoTagged,
    string Message,
    TraitSearchMatch? TargetSymbol,
    IReadOnlyList<LocationSimilarityMatch> SimilarMatches);

/// <summary>
/// Describes the active CPU ONNX semantic model or built-in CPU vectoriser status.
/// </summary>
public sealed record CpuModelStatusDto(
    string EngineMode,
    bool IsOnnxModelLoaded,
    string? ModelPath,
    string? VocabPath,
    int EmbeddingDimensions,
    string Description);

/// <summary>
/// Usage count summary for a single capability or technology tag across the repository.
/// </summary>
public sealed record TagUsageCount(
    string Tag,
    int SymbolCount,
    int UniqueBodyCount);

/// <summary>
/// Usage count summary for a multi-trait combination (symbols sharing the same capability/technology tag set).
/// </summary>
public sealed record TagCombinationCount(
    string TagSignature,
    IReadOnlyList<string> Tags,
    int TagCount,
    int SymbolCount,
    int UniqueBodyCount);




