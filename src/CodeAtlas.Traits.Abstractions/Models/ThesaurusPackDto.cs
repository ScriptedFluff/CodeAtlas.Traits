using System.Text.Json.Serialization;

namespace CodeAtlas.Traits.Models;

/// <summary>
/// Data-driven spelling normalisation rule defined inside a drop-in dictionary pack
/// (for example, mapping <c>"ize"</c> to <c>"ise"</c> or language-specific diacritic/variant normalisations).
/// </summary>
public sealed record SpellingNormalisationRule(
    [property: JsonPropertyName("find")] string Find,
    [property: JsonPropertyName("replace")] string Replace);

/// <summary>
/// Represents a language-agnostic drop-in thesaurus/dictionary JSON pack loaded from <c>Dictionaries/*.json</c>.
/// </summary>
public sealed record ThesaurusPackDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("spellingNormalisations")] IReadOnlyList<SpellingNormalisationRule>? SpellingNormalisations,
    [property: JsonPropertyName("synonymGroups")] IReadOnlyList<IReadOnlyList<string>>? SynonymGroups);

/// <summary>
/// Metadata summary of a discovered and loaded dictionary pack.
/// </summary>
public sealed record LoadedDictionaryInfo(
    string Id,
    string Language,
    string Name,
    string Version,
    string Tier,
    string FilePath,
    int SynonymGroupCount);
