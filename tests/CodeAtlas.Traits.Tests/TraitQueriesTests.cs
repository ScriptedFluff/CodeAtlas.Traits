using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Db;
using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Core.Onnx;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;
using Microsoft.Data.Sqlite;

namespace CodeAtlas.Traits.Tests;

public sealed class TraitQueriesTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly TraitsDb _db;

    public TraitQueriesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TraitQueriesTests_" + Guid.NewGuid().ToString("N"));
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
    public void SearchByTraits_WithLanguageTagInList_FiltersOutNonMatchingLanguageFiles()
    {
        // ── 1. Declarations ──
        IReadOnlyList<SymbolSnapshot> snapshots;
        IReadOnlyList<TraitEntry> traitEntries;
        IReadOnlyList<TraitSearchMatch> csharpMatches;
        IReadOnlyList<TraitSearchMatch> tsMatches;

        // ── 2. Intent-First Narrative ──
        snapshots =
        [
            new SymbolSnapshot
            {
                SymbolName = "RetryWithBackoffAsync",
                SymbolType = "Method",
                ClassName  = "RetryService",
                Language   = "csharp",
                FilePath   = "src/Services/RetryService.cs",
                StartLine  = 10,
                EndLine    = 35,
                StartByte  = 100,
                EndByte    = 800,
                Signature  = "Task RetryWithBackoffAsync(Func<Task> op)",
                BodyHash   = "aaaa1111bbbb2222",
                IsTrivial  = false
            },
            new SymbolSnapshot
            {
                SymbolName = "retryObservable",
                SymbolType = "Function",
                ClassName  = null,
                Language   = "typescript",
                FilePath   = "web/src/retry.ts",
                StartLine  = 5,
                EndLine    = 20,
                StartByte  = 50,
                EndByte    = 400,
                Signature  = "retryObservable<T>(source$: Observable<T>)",
                BodyHash   = "cccc3333dddd4444",
                IsTrivial  = false
            }
        ];

        traitEntries =
        [
            new TraitEntry
            {
                BodyHash  = "aaaa1111bbbb2222",
                Summary   = "Executes an asynchronous delegate with Polly exponential backoff and jitter.",
                IsUtility = true,
                Tags      = ["retry", "polly", "backoff", "jitter"]
            },
            new TraitEntry
            {
                BodyHash  = "cccc3333dddd4444",
                Summary   = "Retries an RxJS observable stream with delay.",
                IsUtility = true,
                Tags      = ["retry", "rxjs", "backoff"]
            }
        ];

        _db.Traits.ReplaceSymbolSnapshots(snapshots);
        _db.Traits.SaveTraitEntries(traitEntries);

        // Pass "C#" directly inside the tags list:
        csharpMatches = _db.Traits.SearchByTraits(["retry", "C#"]);
        tsMatches     = _db.Traits.SearchByTraits(["retry", "typescript"]);

        // ── 3. Output / Assertions ──
        Assert.Single(csharpMatches);
        Assert.Equal("RetryWithBackoffAsync", csharpMatches[0].SymbolName);
        Assert.Equal("csharp", csharpMatches[0].Language);

        Assert.Single(tsMatches);
        Assert.Equal("retryObservable", tsMatches[0].SymbolName);
        Assert.Equal("typescript", tsMatches[0].Language);
    }

    [Fact]
    public void TraitsKeyedByBodyHash_SurviveFullSnapshotReindexAndFileMoves()
    {
        // ── 1. Declarations ──
        SymbolSnapshot originalSnapshot;
        SymbolSnapshot movedSnapshot;
        IReadOnlyList<TraitSearchMatch> matchesAfterMove;

        // ── 2. Intent-First Narrative ──
        originalSnapshot = new SymbolSnapshot
        {
            SymbolName = "HashesMatch",
            SymbolType = "Method",
            ClassName  = "OrderController",
            Language   = "csharp",
            FilePath   = "src/Controllers/OrderController.cs",
            StartLine  = 120,
            EndLine    = 130,
            StartByte  = 3000,
            EndByte    = 3250,
            Signature  = "bool HashesMatch(string a, string b)",
            BodyHash   = "deadbeef12345678",
            IsTrivial  = false
        };

        _db.Traits.ReplaceSymbolSnapshots([originalSnapshot]);
        _db.Traits.SaveTraitEntries([
            new TraitEntry
            {
                BodyHash  = "deadbeef12345678",
                Summary   = "Compares two strings for ordinal equality.",
                IsUtility = true,
                Tags      = ["string", "compare", "hash"]
            }
        ]);

        // Simulate moving the method to StringExtensions.cs and running a full snapshot replace:
        movedSnapshot = originalSnapshot with
        {
            SymbolName = "Match",
            ClassName  = "StringExtensions",
            FilePath   = "src/Common/Extensions/StringExtensions.cs",
            StartLine  = 15,
            EndLine    = 25
        };

        _db.Traits.ReplaceSymbolSnapshots([movedSnapshot]);
        matchesAfterMove = _db.Traits.SearchByTraits(["string", "compare"]);

        // ── 3. Output / Assertions ──
        Assert.Single(matchesAfterMove);
        Assert.Equal("src/Common/Extensions/StringExtensions.cs", matchesAfterMove[0].FilePath);
        Assert.Equal("Match", matchesAfterMove[0].SymbolName);
    }

    [Fact]
    public void FindExactBodyDuplicates_ReturnsMethodsSharingSameBodyHash()
    {
        // ── 1. Declarations ──
        IReadOnlyList<SymbolSnapshot> snapshots;
        IReadOnlyList<DuplicateBodyGroup> duplicates;

        // ── 2. Intent-First Narrative ──
        snapshots =
        [
            new SymbolSnapshot
            {
                SymbolName = "EscapeCsv",
                SymbolType = "Method",
                ClassName  = "ReportService",
                Language   = "csharp",
                FilePath   = "src/Services/ReportService.cs",
                StartLine  = 40,
                EndLine    = 50,
                StartByte  = 400,
                EndByte    = 600,
                BodyHash   = "dup1111122223333",
                IsTrivial  = false
            },
            new SymbolSnapshot
            {
                SymbolName = "SanitiseCsvCell",
                SymbolType = "Method",
                ClassName  = "ExportController",
                Language   = "csharp",
                FilePath   = "src/Controllers/ExportController.cs",
                StartLine  = 85,
                EndLine    = 95,
                StartByte  = 1200,
                EndByte    = 1400,
                BodyHash   = "dup1111122223333",
                IsTrivial  = false
            }
        ];

        _db.Traits.ReplaceSymbolSnapshots(snapshots);
        duplicates = _db.Traits.FindExactBodyDuplicates("csharp");

        // ── 3. Output / Assertions ──
        Assert.Single(duplicates);
        Assert.Equal(2, duplicates[0].OccurrenceCount);
        Assert.Equal(2, duplicates[0].Locations.Count);
    }

    [Fact]
    public void FindSemanticSimilarPairs_DetectsDifferentImplementationsSharingCapabilityTags()
    {
        // ── 1. Declarations ──
        IReadOnlyList<SymbolSnapshot> snapshots;
        IReadOnlyList<TraitEntry> entries;
        IReadOnlyList<SemanticSimilarityPair> similarPairs;

        // ── 2. Intent-First Narrative ──
        snapshots =
        [
            new SymbolSnapshot
            {
                SymbolName = "RetryWithBackoffAsync",
                SymbolType = "Method",
                ClassName  = "RetryService",
                Language   = "csharp",
                FilePath   = "src/Services/RetryService.cs",
                StartLine  = 10,
                EndLine    = 35,
                StartByte  = 100,
                EndByte    = 600,
                BodyHash   = "hash_polly_retry",
                IsTrivial  = false
            },
            new SymbolSnapshot
            {
                SymbolName = "DelayWithJitterAsync",
                SymbolType = "Method",
                ClassName  = "BillingClient",
                Language   = "csharp",
                FilePath   = "src/Clients/BillingClient.cs",
                StartLine  = 90,
                EndLine    = 115,
                StartByte  = 2100,
                EndByte    = 2600,
                BodyHash   = "hash_custom_retry",
                IsTrivial  = false
            }
        ];

        entries =
        [
            new TraitEntry
            {
                BodyHash  = "hash_polly_retry",
                Summary   = "Shared Polly retry service with exponential backoff and jitter.",
                IsUtility = true,
                Tags      = ["retry", "backoff", "jitter", "polly"]
            },
            new TraitEntry
            {
                BodyHash  = "hash_custom_retry",
                Summary   = "Bespoke retry loop with random jitter delay.",
                IsUtility = false,
                Tags      = ["retry", "backoff", "jitter", "http"]
            }
        ];

        _db.Traits.ReplaceSymbolSnapshots(snapshots);
        _db.Traits.SaveTraitEntries(entries);

        similarPairs = _db.Traits.FindSemanticSimilarPairs(language: "csharp", minSharedTags: 3);

        // ── 3. Output / Assertions ──
        Assert.Single(similarPairs);
        Assert.Equal(3, similarPairs[0].SharedTagCount);
        Assert.Contains("retry", similarPairs[0].SharedTags);
        Assert.Contains("backoff", similarPairs[0].SharedTags);
        Assert.Contains("jitter", similarPairs[0].SharedTags);
    }

    [Fact]
    public void AuditFilesTraits_IdentifiesUntaggedAndOverlappingDiffSymbols()
    {
        // ── 1. Declarations ──
        IReadOnlyList<SymbolSnapshot> snapshots;
        IReadOnlyList<TraitEntry> entries;
        DiffTraitAuditReport report;

        // ── 2. Intent-First Narrative ──
        snapshots =
        [
            new SymbolSnapshot
            {
                SymbolName = "Match",
                SymbolType = "Method",
                ClassName  = "StringExtensions",
                Language   = "csharp",
                FilePath   = "src/Extensions/StringExtensions.cs",
                StartLine  = 10,
                EndLine    = 20,
                StartByte  = 100,
                EndByte    = 300,
                BodyHash   = "hash_shared_match",
                IsTrivial  = false
            },
            new SymbolSnapshot
            {
                SymbolName = "HashesMatch",
                SymbolType = "Method",
                ClassName  = "AuthService",
                Language   = "csharp",
                FilePath   = "src/Services/AuthService.cs",
                StartLine  = 50,
                EndLine    = 60,
                StartByte  = 900,
                EndByte    = 1100,
                BodyHash   = "hash_private_match",
                IsTrivial  = false
            },
            new SymbolSnapshot
            {
                SymbolName       = "NewUntaggedMethod",
                SymbolType       = "Method",
                ClassName        = "AuthService",
                Language         = "csharp",
                FilePath         = "src/Services/AuthService.cs",
                StartLine        = 70,
                EndLine          = 85,
                StartByte        = 1200,
                EndByte          = 1500,
                BodyHash         = "hash_untagged_new",
                IsTrivial        = false,
                ExtractedContent = "public void NewUntaggedMethod() { Console.WriteLine(123); }"
            }
        ];

        entries =
        [
            new TraitEntry
            {
                BodyHash  = "hash_shared_match",
                Summary   = "General string equality matcher.",
                IsUtility = true,
                Tags      = ["string", "compare"]
            },
            new TraitEntry
            {
                BodyHash  = "hash_private_match",
                Summary   = "Compares two hash strings.",
                IsUtility = true,
                Tags      = ["string", "match"] // 'match' is a synonym of 'compare'!
            }
        ];

        _db.Traits.ReplaceSymbolSnapshots(snapshots);
        _db.Traits.SaveTraitEntries(entries);

        // Audit only the touched file 'src/Services/AuthService.cs':
        report = _db.Traits.AuditFilesTraits(["src/Services/AuthService.cs"]);

        // ── 3. Output / Assertions ──
        Assert.Single(report.ModifiedFiles);
        Assert.Single(report.UntaggedSymbolsInDiff);
        Assert.Equal("NewUntaggedMethod", report.UntaggedSymbolsInDiff[0].SymbolName);

        Assert.Single(report.OverlappingSymbolsInDiff);
        Assert.Equal("HashesMatch", report.OverlappingSymbolsInDiff[0].DiffSymbol.SymbolName);
        Assert.Contains(
            report.OverlappingSymbolsInDiff[0].OverlappingExistingSymbols,
            m => m.SymbolName == "Match" && m.ClassName == "StringExtensions");
    }

    [Fact]
    public void SaveTraitEntries_StripsLanguageNamesFromStoredTags_AndSplitTagsExtractsLanguageForCodeAtlas()
    {
        // ── 1. Declarations ──
        SymbolSnapshot snapshot;
        IReadOnlyList<TraitSearchMatch> matches;
        string? extractedLanguage;
        IReadOnlyList<string> capabilityTags;

        // ── 2. Intent-First Narrative ──
        snapshot = new SymbolSnapshot
        {
            SymbolName = "RetryAsync",
            SymbolType = "Method",
            ClassName  = "RetryService",
            Language   = "csharp",
            FilePath   = "src/RetryService.cs",
            StartLine  = 10,
            EndLine    = 20,
            StartByte  = 100,
            EndByte    = 300,
            BodyHash   = "hash_strip_lang_test",
            IsTrivial  = false
        };

        _db.Traits.ReplaceSymbolSnapshots([snapshot]);

        // Even if an AI passes 'c#', 'csharp', or 'typescript' inside Tags, SaveTraitEntries strips them:
        _db.Traits.SaveTraitEntries([
            new TraitEntry
            {
                BodyHash  = "hash_strip_lang_test",
                Summary   = "Retries an operation.",
                IsUtility = true,
                Tags      = ["retry", "polly", "c#", "csharp"]
            }
        ]);

        (extractedLanguage, capabilityTags) = LanguageNormalizer.SplitTagsAndLanguage(["retry", "C#"]);
        matches = _db.Traits.SearchByTraits(capabilityTags, extractedLanguage);

        // ── 3. Output / Assertions ──
        Assert.Equal("csharp", extractedLanguage);
        Assert.Single(capabilityTags);
        Assert.Equal("retry", capabilityTags[0]);

        Assert.Single(matches);
        Assert.DoesNotContain("c#", matches[0].Tags);
        Assert.DoesNotContain("csharp", matches[0].Tags);
        Assert.Contains("retry", matches[0].Tags);
        Assert.Contains("polly", matches[0].Tags);
    }

    [Fact]
    public void FindSimilarToLocation_WhenTargetSymbolIsUntagged_AutoTagsAndFindsSimilarInSizeAndTags()
    {
        // ── 1. Declarations ──
        IReadOnlyList<SymbolSnapshot> snapshots;
        bool parsedOk;
        string filePattern;
        int lineNum;
        SimilarToLocationResult result;

        // ── 2. Intent-First Narrative ──
        snapshots =
        [
            new SymbolSnapshot
            {
                SymbolName       = "EscapeCsvCell",
                SymbolType       = "Method",
                ClassName        = "StringExtensions",
                Language         = "csharp",
                FilePath         = "src/Common/Extensions/StringExtensions.cs",
                StartLine        = 10,
                EndLine          = 22,
                StartByte        = 100,
                EndByte          = 400,
                Signature        = "string EscapeCsvCell(string value)",
                BodyHash         = "hash_existing_csv_ext",
                IsTrivial        = false,
                ExtractedContent = "public static string EscapeCsvCell(this string value) { return value.Replace(\"\\\"\", \"\\\"\\\"\"); }"
            },
            // Untagged method in MyClass.cs spanning lines 30..42 (covers MyClass.cs:L35!):
            new SymbolSnapshot
            {
                SymbolName       = "SanitiseCsvValue",
                SymbolType       = "Method",
                ClassName        = "MyClass",
                Language         = "csharp",
                FilePath         = "src/Features/Export/MyClass.cs",
                StartLine        = 30,
                EndLine          = 42,
                StartByte        = 800,
                EndByte          = 1100,
                Signature        = "string SanitiseCsvValue(string rawText)",
                BodyHash         = "hash_untagged_myclass_l35",
                IsTrivial        = false,
                ExtractedContent = "private static string SanitiseCsvValue(string rawText) { return rawText.Trim(); }"
            }
        ];

        _db.Traits.ReplaceSymbolSnapshots(snapshots);

        // Notice NEITHER symbol was manually tagged prior to calling FindSimilarToLocation!
        parsedOk = "MyClass.cs:L35".TryParseFileLineReference(out filePattern, out lineNum);
        result   = _db.Traits.FindSimilarToLocation(filePattern, lineNum);

        // ── 3. Output / Assertions ──
        Assert.True(parsedOk);
        Assert.Equal("MyClass.cs", filePattern);
        Assert.Equal(35, lineNum);

        Assert.True(result.TargetFound);
        Assert.True(result.WasAutoTagged);
        Assert.NotNull(result.TargetSymbol);
        Assert.Equal("SanitiseCsvValue", result.TargetSymbol!.SymbolName);

        Assert.Single(result.SimilarMatches);
        Assert.Equal("EscapeCsvCell", result.SimilarMatches[0].MatchedSymbol.SymbolName);
        Assert.Equal(100, result.SimilarMatches[0].SizeSimilarityPercent); // Both are 13 lines long (10..22 and 30..42)
#if ENABLE_CPU_AUTO_TAGS
        Assert.Contains("csv", result.SimilarMatches[0].SharedConcepts);
        Assert.Contains("sanitise", result.SimilarMatches[0].SharedConcepts); // Matched 'escape' via thesaurus!
#else
        Assert.True(result.SimilarMatches[0].EmbeddingSimilarityPercent > 0);
#endif
    }

    [Fact]
    public void FindSimilarToTarget_WithNamespaceClassColonMethod_ResolvesAutoTagsAndFindsSimilar()
    {
        // ── 1. Declarations ──
        IReadOnlyList<SymbolSnapshot> snapshots;
        bool parsedOk;
        string? containerPattern;
        string symbolName;
        SimilarToLocationResult fullNamespaceResult;
        SimilarToLocationResult shortClassResult;

        // ── 2. Intent-First Narrative ──
        snapshots =
        [
            new SymbolSnapshot
            {
                SymbolName       = "RetryWithBackoffAsync",
                SymbolType       = "Method",
                ClassName        = "MyCompany.Common.Resilience.RetryService",
                Language         = "csharp",
                FilePath         = "src/Common/Resilience/RetryService.cs",
                StartLine        = 10,
                EndLine          = 30,
                StartByte        = 100,
                EndByte          = 500,
                Signature        = "Task RetryWithBackoffAsync(Func<Task> operation)",
                BodyHash         = "hash_shared_retry_svc",
                IsTrivial        = false,
                ExtractedContent = "public async Task RetryWithBackoffAsync(Func<Task> operation) { await Task.Delay(100); await operation(); }"
            },
            new SymbolSnapshot
            {
                SymbolName       = "MyMethod",
                SymbolType       = "Method",
                ClassName        = "MyNamespace.MyClass",
                Language         = "csharp",
                FilePath         = "src/Features/Billing/MyClass.cs",
                StartLine        = 40,
                EndLine          = 60,
                StartByte        = 700,
                EndByte          = 1100,
                Signature        = "Task MyMethod(Func<Task> action)",
                BodyHash         = "hash_billing_retry_method",
                IsTrivial        = false,
                ExtractedContent = "public async Task MyMethod(Func<Task> action) { /* retry with backoff */ await Task.Delay(50); await action(); }"
            }
        ];

        _db.Traits.ReplaceSymbolSnapshots(snapshots);

        parsedOk = "MyNamespace.MyClass:MyMethod".TryParseSymbolTargetReference(out containerPattern, out symbolName);
        fullNamespaceResult = _db.Traits.FindSimilarToTarget("MyNamespace.MyClass:MyMethod");
        shortClassResult    = _db.Traits.FindSimilarToTarget("MyClass:MyMethod");

        // ── 3. Output / Assertions ──
        Assert.True(parsedOk);
        Assert.Equal("MyNamespace.MyClass", containerPattern);
        Assert.Equal("MyMethod", symbolName);

        Assert.True(fullNamespaceResult.TargetFound);
        Assert.True(fullNamespaceResult.WasAutoTagged);
        Assert.NotNull(fullNamespaceResult.TargetSymbol);
        Assert.Equal("MyMethod", fullNamespaceResult.TargetSymbol!.SymbolName);
        Assert.Equal("MyNamespace.MyClass", fullNamespaceResult.TargetSymbol.ClassName);

        Assert.Single(fullNamespaceResult.SimilarMatches);
        Assert.Equal("RetryWithBackoffAsync", fullNamespaceResult.SimilarMatches[0].MatchedSymbol.SymbolName);
#if ENABLE_CPU_AUTO_TAGS
        Assert.Contains("retry", fullNamespaceResult.SimilarMatches[0].SharedConcepts);
        Assert.Contains("backoff", fullNamespaceResult.SimilarMatches[0].SharedConcepts);
#else
        Assert.True(fullNamespaceResult.SimilarMatches[0].EmbeddingSimilarityPercent > 0);
#endif

        Assert.True(shortClassResult.TargetFound);
        Assert.Equal("MyMethod", shortClassResult.TargetSymbol!.SymbolName);
        Assert.Single(shortClassResult.SimilarMatches);
    }

    [Fact]
    public void AiDetailedTaggingEndpoints_InspectUpgradeAutoTagsAndProtectAgainstAutoCpuOverwrite()
    {
        // ── 1. Declarations ──
        SymbolSnapshot snapshot;
        UntaggedSymbolCandidate? inspectedBefore;
        IReadOnlyList<UntaggedSymbolCandidate> batchWithAuto;
        TraitSearchMatch? upgradedMatch;
        IReadOnlyList<UntaggedSymbolCandidate> batchAfterAiUpgrade;
        UntaggedSymbolCandidate? inspectedAfterAutoAttempt;

        // ── 2. Intent-First Narrative ──
        snapshot = new SymbolSnapshot
        {
            SymbolName       = "ExecuteOrderWithJitterAsync",
            SymbolType       = "Method",
            ClassName        = "MyShop.Orders.OrderProcessor",
            Language         = "csharp",
            FilePath         = "src/Orders/OrderProcessor.cs",
            StartLine        = 25,
            EndLine          = 48,
            StartByte        = 400,
            EndByte          = 950,
            Signature        = "Task ExecuteOrderWithJitterAsync(Order order)",
            BodyHash         = "hash_order_jitter_op",
            IsTrivial        = false,
            ExtractedContent = "private async Task ExecuteOrderWithJitterAsync(Order order) { await Task.Delay(50); }"
        };

        _db.Traits.ReplaceSymbolSnapshots([snapshot]);

        // Step 1: Simulate baseline CPU auto-tagging ('auto-cpu'):
        _db.Traits.SaveTraitEntries([
            new TraitEntry
            {
                BodyHash  = "hash_order_jitter_op",
                Summary   = "Auto-tagged method ExecuteOrderWithJitterAsync.",
                IsUtility = false,
                Tags      = ["retry"],
                TagSource = TraitEntry.SourceAutoCpu
            }
        ]);

        // Step 2: Gemini/Copilot calls GetUntaggedBatch(includeAutoTagged: true) and GetSymbolForAnalysis:
        batchWithAuto   = _db.Traits.GetUntaggedBatch("csharp", batchSize: 25, includeAutoTagged: true);
        inspectedBefore = _db.Traits.GetSymbolForAnalysis("MyShop.Orders.OrderProcessor:ExecuteOrderWithJitterAsync");

        // Step 3: Gemini/Copilot calls TagSymbolByTarget to insert rich, detailed 'ai' traits:
        upgradedMatch = _db.Traits.TagSymbolByTarget(
            "MyShop.Orders.OrderProcessor:ExecuteOrderWithJitterAsync",
            tags:      ["retry", "jitter", "exponential-backoff", "polly", "resilience"],
            summary:   "Executes an order submission with randomised exponential backoff jitter.",
            isUtility: true);

        // Step 4: Verify 'auto-cpu' never overwrites 'ai' detailed traits:
        _db.Traits.SaveTraitEntries([
            new TraitEntry
            {
                BodyHash  = "hash_order_jitter_op",
                Summary   = "Should be ignored because AI already tagged this!",
                IsUtility = false,
                Tags      = ["ignored"],
                TagSource = TraitEntry.SourceAutoCpu
            }
        ]);

        batchAfterAiUpgrade       = _db.Traits.GetUntaggedBatch("csharp", batchSize: 25, includeAutoTagged: true);
        inspectedAfterAutoAttempt = _db.Traits.GetSymbolForAnalysis("OrderProcessor:ExecuteOrderWithJitterAsync");

        // ── 3. Output / Assertions ──
        Assert.Single(batchWithAuto);
        Assert.Equal(TraitEntry.SourceAutoCpu, batchWithAuto[0].CurrentTagSource);

        Assert.NotNull(inspectedBefore);
        Assert.Equal("hash_order_jitter_op", inspectedBefore!.BodyHash);
        Assert.Equal(TraitEntry.SourceAutoCpu, inspectedBefore.CurrentTagSource);

        Assert.NotNull(upgradedMatch);
        Assert.Equal(TraitEntry.SourceAiDetailed, upgradedMatch!.TagSource);
        Assert.True(upgradedMatch.IsUtility);
        Assert.Contains("exponential-backoff", upgradedMatch.Tags);

        Assert.Empty(batchAfterAiUpgrade);
        Assert.NotNull(inspectedAfterAutoAttempt);
        Assert.Equal(TraitEntry.SourceAiDetailed, inspectedAfterAutoAttempt!.CurrentTagSource);
        Assert.Equal("Executes an order submission with randomised exponential backoff jitter.", inspectedAfterAutoAttempt.CurrentSummary);
        Assert.DoesNotContain("ignored", inspectedAfterAutoAttempt.CurrentTags!);
    }

    [Fact]
    public void CpuSemanticEngine_ComputesHighCosineSimilarityForSemanticallyRelatedMethods()
    {
        // ── 1. Declarations ──
        ThesaurusEngine thesaurus;
        CpuSemanticEngine engine;
        float[] vecEscapeCsv;
        float[] vecSanitiseCsv;
        float[] vecJwtToken;
        double simCsvPair;
        double simUnrelatedPair;

        // ── 2. Intent-First Narrative ──
        thesaurus      = ThesaurusEngine.LoadForWorkspace(_tempDir);
        engine         = CpuSemanticEngine.LoadForWorkspace(_tempDir);
        vecEscapeCsv   = engine.ComputeEmbedding("string EscapeCsvField(string rawCell) { quote comma csv }", thesaurus);
        vecSanitiseCsv = engine.ComputeEmbedding("string SanitiseCsvValue(string cellText) { clean scrub csv }", thesaurus);
        vecJwtToken    = engine.ComputeEmbedding("bool ValidateJwtBearerSignature(string token) { hmac sha256 auth }", thesaurus);

        simCsvPair       = CpuSemanticEngine.ComputeCosineSimilarity(vecEscapeCsv, vecSanitiseCsv);
        simUnrelatedPair = CpuSemanticEngine.ComputeCosineSimilarity(vecEscapeCsv, vecJwtToken);

        // ── 3. Output / Assertions ──
        Assert.True(simCsvPair > 0.60, $"Expected high semantic cosine similarity for CSV methods, got {simCsvPair:F2}");
        Assert.True(simCsvPair > simUnrelatedPair + 0.30);
    }

    [Fact]
    public void Iteration2_ContractSeparationClusterDetectionAndConfigIgnoreFiltering_WorksEndToEnd()
    {
        // ── 1. Declarations ──
        IReadOnlyList<SymbolSnapshot> snapshots;
        IReadOnlyList<DuplicateBodyGroup> defaultDuplicates;
        IReadOnlyList<DuplicateBodyGroup> withContractsDuplicates;
        IReadOnlyList<SubsystemDuplicateCluster> clusters;
        string settingsDir;

        // ── 2. Intent-First Narrative ──
        settingsDir = Path.Combine(_tempDir, ".codeatlas");
        Directory.CreateDirectory(settingsDir);
        File.WriteAllText(
            Path.Combine(settingsDir, "settings.json"),
            """{ "ignorePatterns": ["**/*Tests/**/*"] }""");

        snapshots =
        [
            // 1. Interface contract signature (HasExecutableBody = false)
            new SymbolSnapshot
            {
                SymbolName        = "CancelJob",
                SymbolType        = "Method",
                ClassName         = "IJobClient",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Jobs/Client/IJobClient.cs",
                StartLine         = 81,
                EndLine           = 81,
                StartByte         = 100,
                EndByte           = 140,
                BodyHash          = "hash_cancel_job_contract",
                IsTrivial         = false,
                HasExecutableBody = false,
                StatementCount    = 0
            },
            new SymbolSnapshot
            {
                SymbolName        = "CancelJob",
                SymbolType        = "Method",
                ClassName         = "IJobManager",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Jobs/Manager/IJobManager.cs",
                StartLine         = 36,
                EndLine           = 36,
                StartByte         = 100,
                EndByte           = 140,
                BodyHash          = "hash_cancel_job_contract",
                IsTrivial         = false,
                HasExecutableBody = false,
                StatementCount    = 0
            },
            // 2. Subsystem cluster: 2 sibling stub classes duplicated between PerformanceQuery and ReportQuery
            new SymbolSnapshot
            {
                SymbolName        = "RunResultStub",
                SymbolType        = "Class",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Model/GraphQLModels/PerformanceQuery/GraphQlResponseStub.cs",
                StartLine         = 8,
                EndLine           = 11,
                StartByte         = 50,
                EndByte           = 120,
                BodyHash          = "hash_run_result_stub",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 1
            },
            new SymbolSnapshot
            {
                SymbolName        = "RunResultStub",
                SymbolType        = "Class",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Model/GraphQLModels/ReportQuery/ReportQuery.cs",
                StartLine         = 9,
                EndLine           = 12,
                StartByte         = 50,
                EndByte           = 120,
                BodyHash          = "hash_run_result_stub",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 1
            },
            new SymbolSnapshot
            {
                SymbolName        = "RollingAnalysisStub",
                SymbolType        = "Class",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Model/GraphQLModels/PerformanceQuery/GraphQlResponseStub.cs",
                StartLine         = 42,
                EndLine           = 49,
                StartByte         = 200,
                EndByte           = 400,
                BodyHash          = "hash_rolling_analysis_stub",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 4
            },
            new SymbolSnapshot
            {
                SymbolName        = "RollingAnalysisStub",
                SymbolType        = "Class",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Model/GraphQLModels/ReportQuery/ReportQuery.cs",
                StartLine         = 40,
                EndLine           = 47,
                StartByte         = 200,
                EndByte           = 400,
                BodyHash          = "hash_rolling_analysis_stub",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 4
            },
            // 3. Test method in ignored **/*Tests/**/* path
            new SymbolSnapshot
            {
                SymbolName        = "TestInit",
                SymbolType        = "Method",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Tests/Web/Auth/PartnerAccessHandlerTests.cs",
                StartLine         = 20,
                EndLine           = 30,
                StartByte         = 100,
                EndByte           = 300,
                BodyHash          = "hash_test_init",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 3
            },
            new SymbolSnapshot
            {
                SymbolName        = "TestInit",
                SymbolType        = "Method",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Tests/Web/Auth/RestrictedModeHandlerTests.cs",
                StartLine         = 21,
                EndLine           = 31,
                StartByte         = 100,
                EndByte           = 300,
                BodyHash          = "hash_test_init",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 3
            }
        ];

        _db.Traits.ReplaceSymbolSnapshots(snapshots);

        defaultDuplicates       = _db.Traits.FindExactBodyDuplicates("csharp", limit: 50, includeContracts: false, minStatements: 2, onlyActionableDebt: true, workspaceRoot: _tempDir);
        withContractsDuplicates = _db.Traits.FindExactBodyDuplicates("csharp", limit: 50, includeContracts: true, minStatements: 2, onlyActionableDebt: true, workspaceRoot: _tempDir);
        clusters                = _db.Traits.DetectSubsystemDuplicateClusters("csharp", includeContracts: false, onlyActionableDebt: true, workspaceRoot: _tempDir);

        // ── 3. Output / Assertions ──
        // Interface contract (CancelJob) and ignored test path (TestInit) are excluded by default;
        // both stub classes in the GraphQlResponseStub.cs <-> ReportQuery.cs cluster are kept!
        Assert.Equal(2, defaultDuplicates.Count);
        Assert.DoesNotContain(defaultDuplicates, d => d.BodyHash == "hash_cancel_job_contract");
        Assert.DoesNotContain(defaultDuplicates, d => d.BodyHash == "hash_test_init");
        Assert.Equal(3, withContractsDuplicates.Count);

        Assert.Single(clusters);
        Assert.Equal(2, clusters[0].IdenticalSymbolCount);
        Assert.Contains("RunResultStub", clusters[0].SymbolNames);
        Assert.Contains("RollingAnalysisStub", clusters[0].SymbolNames);
    }

    [Fact]
    public void ZeroCostStructuralTraits_SeedsHighSignalMultiWordTraitsWithoutNoisySingleWords()
    {
        // ── 1. Declarations ──
        SymbolSnapshot snapshot;
        ThesaurusEngine thesaurus;
        TraitEntry generated;

        // ── 2. Intent-First Narrative ──
        thesaurus = ThesaurusEngine.LoadForWorkspace(_tempDir);
        snapshot  = new SymbolSnapshot
        {
            SymbolName       = "GetOrSetHashEntryAsync",
            SymbolType       = "Method",
            ClassName        = "RedisExtensions",
            Language         = "csharp",
            FilePath         = "Clever.Adviser.Services/Extensions.cs",
            StartLine        = 21,
            EndLine          = 31,
            StartByte        = 100,
            EndByte          = 450,
            Signature        = "public static async Task<T> GetOrSetHashEntryAsync<T>(this IRedisCacheActions redis, string key, CancellationToken ct)",
            BodyHash         = "hash_redis_ext",
            IsTrivial        = false,
            ExtractedContent = "public static async Task<T> GetOrSetHashEntryAsync<T>(this IRedisCacheActions redis, string key, CancellationToken ct) { return await retryService.RetryAsync(ct); }"
        };

        generated = CodeAtlas.Traits.Core.Scanner.AutoTraitTagger.GenerateTraitsForSnapshot(
            snapshot,
            thesaurus,
            new HashSet<string>());

        // ── 3. Output / Assertions ──
        Assert.Contains("extension_method", generated.Tags);
        Assert.Contains("cancellable_async", generated.Tags);
        Assert.Contains("accesses_redis", generated.Tags);
        Assert.Contains("has_retry_logic", generated.Tags);
        Assert.DoesNotContain("string", generated.Tags);
    }

    [Fact]
    public void AiTraitsJsonlSync_AutomaticallyExportsAndReimportsVerifiedAiTraits()
    {
        // ── 1. Declarations ──
        string expectedJsonlPath;
        string secondDbPath;
        TraitEntry aiEntry;
        TraitEntry autoEntry;
        IReadOnlyList<TraitSearchMatch> matchesAfterReimport;

        // ── 2. Intent-First Narrative ──
        expectedJsonlPath = AiTraitsJsonlStore.ResolveJsonlPathFromDbPath(_dbPath)!;
        secondDbPath      = Path.Combine(Path.GetDirectoryName(_dbPath)!, "fresh_traits.db");

        aiEntry = new TraitEntry
        {
            BodyHash         = "hash_retry_backoff",
            Summary          = "Verified AI retry policy with exponential backoff.",
            IsUtility        = true,
            Tags             = ["retry_backoff", "polly"],
            TagSource        = TraitEntry.SourceAiDetailed,
            Category         = "pattern",
            IsActionableDebt = false,
            ClusterName      = "ResilienceHelpers"
        };

        autoEntry = new TraitEntry
        {
            BodyHash  = "hash_parse_csv",
            Summary   = "Baseline auto-cpu trait.",
            IsUtility = false,
            Tags      = ["csv_parse"],
            TagSource = TraitEntry.SourceAutoCpu
        };

        _db.Traits.SaveTraitEntries([aiEntry, autoEntry]);

        // Simulate deleting traits.db and opening a fresh DB in the same .codeatlas directory where traits.ai.jsonl exists
        using var freshDb = new TraitsDb(secondDbPath);
        freshDb.Migrate();
        freshDb.Traits.ReplaceSymbolSnapshots([
            new SymbolSnapshot
            {
                SymbolName = "RetryWithBackoffAsync",
                SymbolType = "Method",
                ClassName  = "RetryService",
                Language   = "csharp",
                FilePath   = "src/RetryService.cs",
                StartLine  = 10,
                EndLine    = 30,
                StartByte  = 100,
                EndByte    = 500,
                BodyHash   = "hash_retry_backoff",
                IsTrivial  = false
            }
        ]);
        matchesAfterReimport = freshDb.Traits.SearchByTraits(["retry_backoff"], useThesaurus: false);

        // ── 3. Output / Assertions ──
        Assert.True(File.Exists(expectedJsonlPath));
        Assert.Single(matchesAfterReimport);
        Assert.Equal("ai", matchesAfterReimport[0].TagSource);
        Assert.Equal("pattern", matchesAfterReimport[0].Category);
        Assert.False(matchesAfterReimport[0].IsActionableDebt);
        Assert.Equal("ResilienceHelpers", matchesAfterReimport[0].ClusterName);
    }

    [Fact]
    public void StructuralNormaliser_CatchesRenamedClonesWhilePreservingServiceCalls()
    {
        // ── 1. Declarations ──
        string dtoSkeletonA;
        string dtoSkeletonB;
        string methodCloneA;
        string methodCloneB;
        string serviceMethodDoc;
        string serviceMethodInv;
        string dtoStructHashA;
        string dtoStructHashB;
        string methodStructHashA;
        string methodStructHashB;
        string docStructHash;
        string invStructHash;
        IReadOnlyList<DuplicateBodyGroup> duplicates;

        // ── 2. Intent-First Narrative ──
        dtoSkeletonA = "public class PerformanceResultStub : BaseStub\n{\n    string Code { get; set; }\n    decimal Value { get; set; }\n}";
        dtoSkeletonB = "public class ReportResultStub : BaseStub\n{\n    string Code { get; set; }\n    decimal Value { get; set; }\n}";

        methodCloneA = "public decimal CalculateFee(decimal amount, int tier) { var rate = 0.05m; if (tier > 1) { return amount * rate; } return amount; }";
        methodCloneB = "public decimal ComputeFee(decimal totalValue, int level) { var factor = 0.25m; if (level > 2) { return totalValue * factor; } return totalValue; }";

        serviceMethodDoc = "public async Task UpdateAsync(int id) { var entity = await _documentService.GetAsync(id); await _documentService.UpdateAsync(entity); }";
        serviceMethodInv = "public async Task UpdateAsync(int id) { var entity = await _investorService.GetAsync(id); await _investorService.UpdateAsync(entity); }";

        dtoStructHashA    = CodeAtlas.Traits.Core.Scanner.StructuralNormaliser.NormaliseClassSkeleton("PerformanceResultStub", dtoSkeletonA).ComputeContentHash();
        dtoStructHashB    = CodeAtlas.Traits.Core.Scanner.StructuralNormaliser.NormaliseClassSkeleton("ReportResultStub", dtoSkeletonB).ComputeContentHash();
        methodStructHashA = CodeAtlas.Traits.Core.Scanner.StructuralNormaliser.NormaliseMethodBody("CalculateFee", methodCloneA).ComputeContentHash();
        methodStructHashB = CodeAtlas.Traits.Core.Scanner.StructuralNormaliser.NormaliseMethodBody("ComputeFee", methodCloneB).ComputeContentHash();
        docStructHash     = CodeAtlas.Traits.Core.Scanner.StructuralNormaliser.NormaliseMethodBody("UpdateAsync", serviceMethodDoc).ComputeContentHash();
        invStructHash     = CodeAtlas.Traits.Core.Scanner.StructuralNormaliser.NormaliseMethodBody("UpdateAsync", serviceMethodInv).ComputeContentHash();

        _db.Traits.ReplaceSymbolSnapshots([
            new SymbolSnapshot
            {
                SymbolName        = "PerformanceResultStub",
                SymbolType        = "Class",
                Language          = "csharp",
                FilePath          = "Models/PerformanceQuery.cs",
                StartLine         = 10,
                EndLine           = 20,
                StartByte         = 100,
                EndByte           = 300,
                BodyHash          = "body_perf_stub",
                StructuralHash    = dtoStructHashA,
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 2
            },
            new SymbolSnapshot
            {
                SymbolName        = "ReportResultStub",
                SymbolType        = "Class",
                Language          = "csharp",
                FilePath          = "Models/ReportQuery.cs",
                StartLine         = 10,
                EndLine           = 20,
                StartByte         = 100,
                EndByte           = 300,
                BodyHash          = "body_report_stub",
                StructuralHash    = dtoStructHashB,
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 2
            }
        ]);

        duplicates = _db.Traits.FindExactBodyDuplicates("csharp", includeRenamedClones: true);

        // ── 3. Output / Assertions ──
        Assert.Equal(dtoStructHashA, dtoStructHashB);
        Assert.Equal(methodStructHashA, methodStructHashB);
        Assert.NotEqual(docStructHash, invStructHash);

        Assert.Single(duplicates);
        Assert.True(duplicates[0].IsRenamedClone);
        Assert.Equal(dtoStructHashA, duplicates[0].StructuralHash);
        Assert.Equal(2, duplicates[0].OccurrenceCount);
    }

    [Fact]
    public void GetUntaggedBatch_PrioritisesHighValueUtilityIslandsAndSupportsPathFilter()
    {
        // ── 1. Declarations ──
        IReadOnlyList<UntaggedSymbolCandidate> defaultBatch;
        IReadOnlyList<UntaggedSymbolCandidate> onlyUtilitiesBatch;
        IReadOnlyList<UntaggedSymbolCandidate> pathFilteredBatch;

        // ── 2. Intent-First Narrative ──
        _db.Traits.ReplaceSymbolSnapshots([
            // Alphabetically first, but a non-utility domain DbContext method
            new SymbolSnapshot
            {
                SymbolName        = "OnModelCreating",
                SymbolType        = "Method",
                ClassName         = "CleverEntityDbContext",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Data/CleverEntityDbContext.cs",
                StartLine         = 10,
                EndLine           = 60,
                StartByte         = 100,
                EndByte           = 1200,
                BodyHash          = "hash_db_context",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 20
            },
            // Static helper inside a unit test file (should NOT be treated as a production utility)
            new SymbolSnapshot
            {
                SymbolName        = "CreateRetryTestMock",
                SymbolType        = "Method",
                ClassName         = "RetryAsyncTests",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Tests/Services/RetryAsyncTests.cs",
                StartLine         = 10,
                EndLine           = 25,
                StartByte         = 100,
                EndByte           = 400,
                Signature         = "private static Mock<IRetryService> CreateRetryTestMock()",
                ExtractedContent  = "private static Mock<IRetryService> CreateRetryTestMock() { return new Mock<IRetryService>(); }",
                BodyHash          = "hash_test_mock_helper",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 5
            },
            // High-value utility extension method (smaller weight)
            new SymbolSnapshot
            {
                SymbolName        = "SetHashEntryAsync",
                SymbolType        = "Method",
                ClassName         = "Extensions",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Services/Extensions/RedisExtensions.cs",
                StartLine         = 40,
                EndLine           = 48,
                StartByte         = 400,
                EndByte           = 600,
                Signature         = "public static Task SetHashEntryAsync(this IRedisCacheActions redis, string key)",
                ExtractedContent  = "public static Task SetHashEntryAsync(this IRedisCacheActions redis, string key) { return Task.CompletedTask; }",
                BodyHash          = "hash_set_hash_util",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 2
            },
            // High-value utility extension method (larger weight - should rank #1!)
            new SymbolSnapshot
            {
                SymbolName        = "GetDisplayName",
                SymbolType        = "Method",
                ClassName         = "EnumExtensions",
                Language          = "csharp",
                FilePath          = "Clever.Adviser.Web/Shared/Utils/EnumExtensions.cs",
                StartLine         = 12,
                EndLine           = 42,
                StartByte         = 100,
                EndByte           = 900,
                Signature         = "public static string GetDisplayName(this Enum value)",
                ExtractedContent  = "public static string GetDisplayName(this Enum value) { return value.ToString(); }",
                BodyHash          = "hash_display_name_util",
                IsTrivial         = false,
                HasExecutableBody = true,
                StatementCount    = 13
            }
        ]);

        defaultBatch       = _db.Traits.GetUntaggedBatch("csharp", batchSize: 10);
        onlyUtilitiesBatch = _db.Traits.GetUntaggedBatch("csharp", batchSize: 10, onlyUtilities: true);
        pathFilteredBatch  = _db.Traits.GetUntaggedBatch("csharp", batchSize: 10, pathFilter: "**/Extensions/**");

        // ── 3. Output / Assertions ──
        Assert.Equal(4, defaultBatch.Count);
        // The two production utility methods come first, ordered by StatementCount DESC (GetDisplayName -> SetHashEntryAsync)
        Assert.Equal("GetDisplayName", defaultBatch[0].SymbolName);
        Assert.True(defaultBatch[0].IsUtility);
        Assert.Equal("SetHashEntryAsync", defaultBatch[1].SymbolName);
        Assert.True(defaultBatch[1].IsUtility);
        // The test static helper is NOT flagged as a production utility
        Assert.DoesNotContain(defaultBatch, c => c.SymbolName == "CreateRetryTestMock" && c.IsUtility);

        Assert.Equal(2, onlyUtilitiesBatch.Count);
        Assert.All(onlyUtilitiesBatch, c => Assert.True(c.IsUtility));

        Assert.Single(pathFilteredBatch);
        Assert.Equal("SetHashEntryAsync", pathFilteredBatch[0].SymbolName);
    }
}
