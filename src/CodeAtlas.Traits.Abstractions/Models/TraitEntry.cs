namespace CodeAtlas.Traits.Models;

/// <summary>
/// Content-addressable capability and technology traits keyed by <see cref="BodyHash"/>.
/// </summary>
public sealed record TraitEntry
{
    public const string SourceAiDetailed = "ai";
    public const string SourceAutoCpu    = "auto-cpu";

    public required string BodyHash { get; init; }
    public required string Summary { get; init; }
    public required bool IsUtility { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public string TagSource { get; init; } = SourceAiDetailed;
    public byte[]? EmbeddingBlob { get; init; }
    public string? TaggedAtUtc { get; init; }
}
