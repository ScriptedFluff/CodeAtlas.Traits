namespace CodeAtlas.Traits.Models;

/// <summary>
/// Snapshot of a symbol synced from CodeAtlas along with its content-addressable <see cref="BodyHash"/>.
/// </summary>
public sealed record SymbolSnapshot
{
    public long Id { get; set; }
    public required string SymbolName { get; init; }
    public required string SymbolType { get; init; }
    public string? ClassName { get; init; }
    public required string Language { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required int StartByte { get; init; }
    public required int EndByte { get; init; }
    public string? Signature { get; init; }
    public string? DocComment { get; init; }
    public required string BodyHash { get; init; }
    public bool IsTrivial { get; init; }
    public string? ExtractedContent { get; init; }
}
