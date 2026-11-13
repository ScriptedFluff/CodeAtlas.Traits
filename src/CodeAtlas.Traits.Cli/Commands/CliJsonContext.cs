using System.Text.Json.Serialization;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Commands;

public sealed record CliStatusDto(
    string WorkspaceRoot,
    string TraitsDbPath,
    ScanProgressSnapshot ScanProgress,
    TraitsCoverageSummary Coverage);

[JsonSerializable(typeof(CliStatusDto))]
[JsonSerializable(typeof(TraitSearchMatch))]
[JsonSerializable(typeof(IReadOnlyList<TraitSearchMatch>))]
[JsonSerializable(typeof(UntaggedSymbolCandidate))]
[JsonSerializable(typeof(IReadOnlyList<UntaggedSymbolCandidate>))]
[JsonSerializable(typeof(IReadOnlyList<DuplicateBodyGroup>))]
[JsonSerializable(typeof(SimilarToLocationResult))]
[JsonSerializable(typeof(CpuModelStatusDto))]
[JsonSerializable(typeof(IReadOnlyList<TagUsageCount>))]
internal sealed partial class CliJsonContext : JsonSerializerContext { }
