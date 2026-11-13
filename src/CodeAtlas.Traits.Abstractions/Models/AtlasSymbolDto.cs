using System.Text.Json.Serialization;

namespace CodeAtlas.Traits.Models;

/// <summary>
/// Represents a symbol record returned by <c>codeatlas query --json</c>.
/// </summary>
public sealed record AtlasSymbolDto(
    [property: JsonPropertyName("Id")] long Id,
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Type")] string Type,
    [property: JsonPropertyName("ClassName")] string? ClassName,
    [property: JsonPropertyName("Language")] string? Language,
    [property: JsonPropertyName("FilePath")] string FilePath,
    [property: JsonPropertyName("StartLine")] int StartLine,
    [property: JsonPropertyName("EndLine")] int EndLine,
    [property: JsonPropertyName("StartByte")] int StartByte,
    [property: JsonPropertyName("EndByte")] int EndByte,
    [property: JsonPropertyName("Signature")] string? Signature,
    [property: JsonPropertyName("DocComment")] string? DocComment,
    [property: JsonPropertyName("IsLive")] bool IsLive);

/// <summary>
/// Represents the JSON envelope returned by <c>codeatlas query --json</c>.
/// </summary>
public sealed record AtlasQueryResponseDto(
    [property: JsonPropertyName("RepoPath")] string RepoPath,
    [property: JsonPropertyName("Filter")] string? Filter,
    [property: JsonPropertyName("TranspiledToSql")] bool TranspiledToSql,
    [property: JsonPropertyName("TotalReturned")] int TotalReturned,
    [property: JsonPropertyName("HasMore")] bool HasMore,
    [property: JsonPropertyName("NextCursor")] long NextCursor,
    [property: JsonPropertyName("Symbols")] IReadOnlyList<AtlasSymbolDto> Symbols);
