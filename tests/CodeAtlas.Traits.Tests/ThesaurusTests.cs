using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;
using Microsoft.Data.Sqlite;

namespace CodeAtlas.Traits.Tests;

public sealed class ThesaurusTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly TraitsDb _db;

    public ThesaurusTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ThesaurusTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath  = TraitsStorageConfig.ResolveTraitsDbPath(_tempDir);
        _db      = new TraitsDb(_dbPath);
        _db.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors.
        }
    }

    [Fact]
    public void BuiltInEnglishDictionary_ExpandsSynonymsAndNormalisesUkUsSpelling()
    {
        // ── 1. Declarations ──
        ThesaurusEngine engine;
        IReadOnlyList<string> sanitiseCluster;
        IReadOnlyList<string> compareCluster;

        // ── 2. Intent-First Narrative ──
        engine          = ThesaurusEngine.LoadForWorkspace(_tempDir);
        sanitiseCluster = engine.ExpandTagToCluster("sanitize"); // US spelling input
        compareCluster  = engine.ExpandTagToCluster("match");

        // ── 3. Output / Assertions ──
        Assert.NotEmpty(engine.LoadedDictionaries);
        Assert.Contains(engine.LoadedDictionaries, d => d.Id == "en-core");

        Assert.Equal("sanitize", sanitiseCluster[0]); // Exact query term placed at index 0
        Assert.Contains("sanitise", sanitiseCluster);
        Assert.Contains("escape", sanitiseCluster);
        Assert.Contains("clean", sanitiseCluster);
        Assert.Contains("scrub", sanitiseCluster);

        Assert.Contains("compare", compareCluster);
        Assert.Contains("equals", compareCluster);
        Assert.Contains("std-equals", compareCluster);
    }

    [Fact]
    public void DropInLanguagePack_IsAutomaticallyPickedUpFromWorkspaceDictionariesFolder()
    {
        // ── 1. Declarations ──
        string repoDictDir;
        string frenchPackJson;
        ThesaurusEngine engine;
        IReadOnlyList<string> frenchRetryCluster;

        // ── 2. Intent-First Narrative ──
        repoDictDir = Path.Combine(_tempDir, ".codeatlas", "dictionaries");
        Directory.CreateDirectory(repoDictDir);

        frenchPackJson = """
            {
              "id": "fr-custom",
              "language": "fr",
              "name": "French Developer Thesaurus",
              "version": "1.0.0",
              "spellingNormalisations": [
                { "find": "é", "replace": "e" }
              ],
              "synonymGroups": [
                ["reessayer", "relancer", "retry"],
                ["nettoyer", "echapper", "sanitise"]
              ]
            }
            """;

        File.WriteAllText(Path.Combine(repoDictDir, "fr.json"), frenchPackJson);

        engine             = ThesaurusEngine.LoadForWorkspace(_tempDir);
        frenchRetryCluster = engine.ExpandTagToCluster("réessayer");

        // ── 3. Output / Assertions ──
        Assert.Contains(engine.LoadedDictionaries, d => d.Id == "fr-custom" && d.Tier == "Workspace");
        Assert.Contains("reessayer", frenchRetryCluster);
        Assert.Contains("relancer", frenchRetryCluster);
        Assert.Contains("retry", frenchRetryCluster);
        // Transitively connects to the built-in English retry cluster ('backoff', 'polly')!
        Assert.Contains("backoff", frenchRetryCluster);
        Assert.Contains("polly", frenchRetryCluster);
    }

    [Fact]
    public void SearchByTraits_FindsSynonymMatchesAndRanksExactMatchesFirst()
    {
        // ── 1. Declarations ──
        IReadOnlyList<SymbolSnapshot> snapshots;
        IReadOnlyList<TraitEntry> entries;
        IReadOnlyList<TraitSearchMatch> matches;

        // ── 2. Intent-First Narrative ──
        snapshots =
        [
            new SymbolSnapshot
            {
                SymbolName = "EscapeCsv",
                SymbolType = "Method",
                ClassName  = "CsvFormatter",
                Language   = "csharp",
                FilePath   = "src/CsvFormatter.cs",
                StartLine  = 10,
                EndLine    = 20,
                StartByte  = 100,
                EndByte    = 300,
                BodyHash   = "hash_escape_csv",
                IsTrivial  = false
            },
            new SymbolSnapshot
            {
                SymbolName = "SanitiseCsvField",
                SymbolType = "Method",
                ClassName  = "CsvSanitiser",
                Language   = "csharp",
                FilePath   = "src/CsvSanitiser.cs",
                StartLine  = 10,
                EndLine    = 25,
                StartByte  = 100,
                EndByte    = 350,
                BodyHash   = "hash_sanitise_csv",
                IsTrivial  = false
            }
        ];

        entries =
        [
            new TraitEntry
            {
                BodyHash  = "hash_escape_csv",
                Summary   = "Escapes quotes and commas in a CSV cell.",
                IsUtility = true,
                Tags      = ["escape", "csv"]
            },
            new TraitEntry
            {
                BodyHash  = "hash_sanitise_csv",
                Summary   = "Sanitises a CSV field against formula injection.",
                IsUtility = true,
                Tags      = ["sanitise", "csv"]
            }
        ];

        _db.Traits.ReplaceSymbolSnapshots(snapshots);
        _db.Traits.SaveTraitEntries(entries);

        // Query for ["sanitise", "csv"]:
        // Both methods should be returned (because 'escape' is in the 'sanitise' concept cluster),
        // and 'SanitiseCsvField' (exact match on 'sanitise') should be ranked #1!
        matches = _db.Traits.SearchByTraits(["sanitise", "csv"]);

        // ── 3. Output / Assertions ──
        Assert.Equal(2, matches.Count);
        Assert.Equal("SanitiseCsvField", matches[0].SymbolName);
        Assert.Equal("EscapeCsv", matches[1].SymbolName);
    }
}
