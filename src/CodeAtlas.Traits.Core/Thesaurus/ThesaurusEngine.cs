using System.Text.Json;
using System.Text.Json.Serialization;
using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Core.Thesaurus;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ThesaurusPackDto))]
internal sealed partial class ThesaurusJsonContext : JsonSerializerContext { }

/// <summary>
/// Language-agnostic thesaurus and spelling normalisation engine.
/// Discovers and merges drop-in <c>*.json</c> dictionary packs across App, User, Environment, and Workspace tiers.
/// Contains zero hardcoded language rules—all spelling equivalents and synonym groups are driven by the loaded JSON packs.
/// </summary>
public sealed class ThesaurusEngine
{
    private readonly List<SpellingNormalisationRule> _spellingRules = [];
    private readonly Dictionary<string, HashSet<string>> _synonymGraph = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LoadedDictionaryInfo> _loadedDictionaries = [];

    public IReadOnlyList<LoadedDictionaryInfo> LoadedDictionaries => _loadedDictionaries;

    /// <summary>
    /// Discovers and loads all <c>*.json</c> dictionary packs across configured tiers.
    /// </summary>
    public static ThesaurusEngine LoadForWorkspace(
        string? workspaceRoot = null,
        DictionaryDirectoryResolver? resolver = null)
    {
        // ── 1. Declarations ──
        DictionaryDirectoryResolver activeResolver;
        ThesaurusEngine engine;
        IReadOnlyList<(DictionarySourceTier Tier, string DirectoryPath)> searchDirs;

        // ── 2. Intent-First Narrative ──
        activeResolver = resolver ?? new DictionaryDirectoryResolver();
        engine         = new ThesaurusEngine();
        searchDirs     = activeResolver.GetSearchDirectories(workspaceRoot);

        foreach (var (tier, dirPath) in searchDirs)
        {
            engine.LoadAllPacksInDirectory(dirPath, tier);
        }

        // ── 3. Output / Return ──
        return engine;
    }

    /// <summary>
    /// Registers a deserialised <see cref="ThesaurusPackDto"/> into the engine's spelling rules and synonym graph.
    /// </summary>
    public void RegisterPack(ThesaurusPackDto pack, string tierName = "Custom", string filePath = "<in-memory>")
    {
        // ── 1. Declarations ──
        int groupCount;

        // ── 2. Preconditions & Guard Clauses ──
        if (pack is null)
        {
            return;
        }

        // ── 3. Intent-First Narrative ──
        if (pack.SpellingNormalisations is not null)
        {
            foreach (var rule in pack.SpellingNormalisations)
            {
                if (!string.IsNullOrWhiteSpace(rule.Find) && rule.Replace is not null)
                {
                    _spellingRules.Add(new SpellingNormalisationRule(
                        rule.Find.Trim().ToLowerInvariant(),
                        rule.Replace.Trim().ToLowerInvariant()));
                }
            }
        }

        groupCount = 0;
        if (pack.SynonymGroups is not null)
        {
            foreach (var group in pack.SynonymGroups)
            {
                if (RegisterSynonymGroup(group))
                {
                    groupCount++;
                }
            }
        }

        _loadedDictionaries.Add(new LoadedDictionaryInfo(
            Id:                string.IsNullOrWhiteSpace(pack.Id) ? Path.GetFileNameWithoutExtension(filePath) : pack.Id,
            Language:          string.IsNullOrWhiteSpace(pack.Language) ? "unknown" : pack.Language,
            Name:              string.IsNullOrWhiteSpace(pack.Name) ? Path.GetFileName(filePath) : pack.Name,
            Version:           string.IsNullOrWhiteSpace(pack.Version) ? "1.0.0" : pack.Version,
            Tier:              tierName,
            FilePath:          filePath,
            SynonymGroupCount: groupCount));
    }

    /// <summary>
    /// Applies all data-driven spelling normalisation rules from loaded dictionary packs to a tag.
    /// </summary>
    public string ApplySpellingNormalisation(string? rawTag)
    {
        // ── 1. Declarations ──
        string current;

        // ── 2. Preconditions & Guard Clauses ──
        current = rawTag.NormaliseTag();
        if (string.IsNullOrWhiteSpace(current) || _spellingRules.Count == 0)
        {
            return current;
        }

        // ── 3. Intent-First Narrative ──
        foreach (var rule in _spellingRules)
        {
            if (current.Contains(rule.Find, StringComparison.Ordinal))
            {
                current = current.Replace(rule.Find, rule.Replace, StringComparison.Ordinal);
            }
        }

        // ── 4. Output / Return ──
        return current;
    }

    /// <summary>
    /// Expands a query tag into its full concept cluster of synonyms (including original, spelling-normalised,
    /// and transitive synonyms across all loaded dictionary packs).
    /// Always places the exact normalised query tag at index 0 for priority ranking.
    /// </summary>
    public IReadOnlyList<string> ExpandTagToCluster(string? rawTag)
    {
        // ── 1. Declarations ──
        string exactTag;
        string spellingNormalised;
        List<string> orderedCluster;
        HashSet<string> visited;
        Queue<string> frontier;

        // ── 2. Preconditions & Guard Clauses ──
        exactTag = rawTag.NormaliseTag();
        if (string.IsNullOrWhiteSpace(exactTag))
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        orderedCluster     = [exactTag];
        visited            = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { exactTag };
        frontier           = new Queue<string>();
        spellingNormalised = ApplySpellingNormalisation(exactTag);

        frontier.Enqueue(exactTag);
        if (visited.Add(spellingNormalised))
        {
            orderedCluster.Add(spellingNormalised);
            frontier.Enqueue(spellingNormalised);
        }

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (!_synonymGraph.TryGetValue(current, out var neighbours))
            {
                continue;
            }

            foreach (var neighbour in neighbours)
            {
                if (visited.Add(neighbour))
                {
                    orderedCluster.Add(neighbour);
                    frontier.Enqueue(neighbour);
                }
            }
        }

        // ── 4. Output / Return ──
        return orderedCluster;
    }

    /// <summary>
    /// Determines whether a normalised word token (or its spelling-normalised form) is present in the loaded thesaurus graph.
    /// </summary>
    public bool IsKnownConceptTerm(string? rawToken, out string canonicalTerm)
    {
        // ── 1. Declarations ──
        string norm;
        string spelled;

        // ── 2. Preconditions & Guard Clauses ──
        canonicalTerm = string.Empty;
        norm          = rawToken.NormaliseTag();
        if (string.IsNullOrWhiteSpace(norm))
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        if (_synonymGraph.ContainsKey(norm))
        {
            canonicalTerm = ApplySpellingNormalisation(norm);
            return true;
        }

        spelled = ApplySpellingNormalisation(norm);
        if (_synonymGraph.ContainsKey(spelled))
        {
            canonicalTerm = spelled;
            return true;
        }

        // ── 4. Output / Return ──
        return false;
    }

    private void LoadAllPacksInDirectory(string directoryPath, DictionarySourceTier tier)
    {
        // ── 1. Declarations ──
        string[] jsonFiles;

        // ── 2. Preconditions & Guard Clauses ──
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        // ── 3. Intent-First Narrative ──
        jsonFiles = TryEnumerateJsonFilesQuietly(directoryPath);
        Array.Sort(jsonFiles, StringComparer.OrdinalIgnoreCase);

        foreach (var file in jsonFiles)
        {
            var pack = TryReadPackFromFileQuietly(file);
            if (pack is not null)
            {
                RegisterPack(pack, tier.ToString(), file);
            }
        }
    }

    private bool RegisterSynonymGroup(IReadOnlyList<string>? rawGroup)
    {
        // ── 1. Declarations ──
        List<string> expandedGroupTerms;

        // ── 2. Preconditions & Guard Clauses ──
        if (rawGroup is null || rawGroup.Count == 0)
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        expandedGroupTerms = [];
        foreach (var rawTerm in rawGroup)
        {
            var norm = rawTerm.NormaliseTag();
            if (string.IsNullOrWhiteSpace(norm))
            {
                continue;
            }

            expandedGroupTerms.Add(norm);

            var spelled = ApplySpellingNormalisation(norm);
            if (!string.Equals(norm, spelled, StringComparison.OrdinalIgnoreCase))
            {
                expandedGroupTerms.Add(spelled);
            }
        }

        if (expandedGroupTerms.Count < 2)
        {
            return false;
        }

        for (var i = 0; i < expandedGroupTerms.Count; i++)
        {
            var termA = expandedGroupTerms[i];
            if (!_synonymGraph.TryGetValue(termA, out var edges))
            {
                edges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _synonymGraph[termA] = edges;
            }

            for (var j = 0; j < expandedGroupTerms.Count; j++)
            {
                if (i != j)
                {
                    edges.Add(expandedGroupTerms[j]);
                }
            }
        }

        // ── 4. Output / Return ──
        return true;
    }

    private static string[] TryEnumerateJsonFilesQuietly(string directoryPath)
    {
        try
        {
            return Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    private static ThesaurusPackDto? TryReadPackFromFileQuietly(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize(json, ThesaurusJsonContext.Default.ThesaurusPackDto);
        }
        catch
        {
            return null;
        }
    }
}
