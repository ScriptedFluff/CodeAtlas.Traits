using System.Text.Json.Serialization;
using CodeAtlas.Traits.Mcp.Tools;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Mcp;

[JsonSerializable(typeof(SearchTraitsResponse))]
[JsonSerializable(typeof(UntaggedBatchResponse))]
[JsonSerializable(typeof(SymbolForAnalysisResponse))]
[JsonSerializable(typeof(TraitSubmissionItem))]
[JsonSerializable(typeof(IReadOnlyList<TraitSubmissionItem>))]
[JsonSerializable(typeof(List<TraitSubmissionItem>))]
[JsonSerializable(typeof(SaveTraitsResponse))]
[JsonSerializable(typeof(TagSymbolResponse))]
[JsonSerializable(typeof(FindDuplicatesResponse))]
[JsonSerializable(typeof(FindSimilarTraitsResponse))]
[JsonSerializable(typeof(FindSimilarToLocationResponse))]
[JsonSerializable(typeof(AuditDiffTraitsResponse))]
[JsonSerializable(typeof(TraitsStatusResponse))]
[JsonSerializable(typeof(TraitSearchMatch))]
[JsonSerializable(typeof(UntaggedSymbolCandidate))]
[JsonSerializable(typeof(DuplicateBodyGroup))]
[JsonSerializable(typeof(DuplicateSymbolLocation))]
[JsonSerializable(typeof(SemanticSimilarityPair))]
[JsonSerializable(typeof(LocationSimilarityMatch))]
[JsonSerializable(typeof(SimilarToLocationResult))]
[JsonSerializable(typeof(DiffSymbolAuditEntry))]
[JsonSerializable(typeof(DiffTraitAuditReport))]
[JsonSerializable(typeof(ScanProgressSnapshot))]
[JsonSerializable(typeof(TraitsCoverageSummary))]
[JsonSerializable(typeof(CpuModelStatusDto))]
[JsonSerializable(typeof(LoadedDictionaryInfo))]
[JsonSerializable(typeof(IReadOnlyList<LoadedDictionaryInfo>))]
internal sealed partial class McpJsonContext : JsonSerializerContext { }
