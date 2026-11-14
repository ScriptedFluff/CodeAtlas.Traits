using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Core.Onnx;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Core.Scanner;

/// <summary>
/// Automatically extracts baseline capability and technology traits and computes CPU ONNX semantic embeddings
/// from an untagged symbol's identifier, signature, doc comment, and source body.
/// </summary>
public static class AutoTraitTagger
{
    /// <summary>
    /// Generates a baseline <see cref="TraitEntry"/> (with <c>TagSource = "auto-cpu"</c> and CPU semantic embedding)
    /// for an untagged <see cref="SymbolSnapshot"/> so that <c>similar</c> and <c>search_traits</c> can immediately
    /// compare untagged code while still allowing Gemini/Copilot to enrich it with <c>"ai"</c> detailed traits later.
    /// </summary>
    public static TraitEntry GenerateTraitsForSnapshot(
        SymbolSnapshot snapshot,
        ThesaurusEngine thesaurus,
        IReadOnlySet<string> existingRepoTags,
        CpuSemanticEngine? semanticEngine = null)
    {
        // ── 1. Declarations ──
        string combinedSourceText;
        List<string> extractedTags;
        HashSet<string> seenTags;
        bool isUtility;
        string summary;
        CpuSemanticEngine engine;
        float[] embeddingVector;
        byte[] embeddingBlob;

        // ── 2. Intent-First Narrative ──
        combinedSourceText = $"{snapshot.SymbolName} {snapshot.Signature} {snapshot.DocComment} {snapshot.ExtractedContent}";
        extractedTags      = [];
        seenTags           = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Tier A: Zero-cost deterministic structural/idiom trait seeding (multi-word high-precision traits only).
        ExtractZeroCostStructuralTraits(snapshot, extractedTags, seenTags);

#if ENABLE_CPU_AUTO_TAGS
        // CPU keyword/thesaurus auto-tagging is disabled by default so 'auto-cpu' only indexes ONNX semantic embeddings
        // and leaves behavioural trait tagging to Gemini/Copilot ('ai').
        // To re-enable CPU keyword auto-tagging, uncomment <DefineConstants>$(DefineConstants);ENABLE_CPU_AUTO_TAGS</DefineConstants>
        // in CodeAtlas.Traits.Core.csproj or build with -p:DefineConstants=ENABLE_CPU_AUTO_TAGS.
        var rawTokens = combinedSourceText.ExtractCodeWordTokens();
        foreach (var token in rawTokens)
        {
            if (LanguageNormalizer.TryExtractLanguageTag(token, out _))
            {
                continue;
            }

            if (thesaurus.IsKnownConceptTerm(token, out var canonicalTerm))
            {
                if (seenTags.Add(canonicalTerm))
                {
                    extractedTags.Add(canonicalTerm);
                }
                continue;
            }

            if (existingRepoTags.Contains(token) && seenTags.Add(token))
            {
                extractedTags.Add(token);
            }
        }

        if (extractedTags.Count == 0)
        {
            foreach (var nameToken in snapshot.SymbolName.ExtractCodeWordTokens())
            {
                if (!LanguageNormalizer.TryExtractLanguageTag(nameToken, out _) && seenTags.Add(nameToken))
                {
                    extractedTags.Add(nameToken);
                }
            }
        }
#endif

        isUtility       = InferIsUtility(snapshot);
        summary         = BuildAutoSummary(snapshot, extractedTags);
        engine          = semanticEngine ?? CpuSemanticEngine.LoadForWorkspace();
        embeddingVector = engine.ComputeEmbedding(combinedSourceText, thesaurus);
        embeddingBlob   = CpuSemanticEngine.SerialiseEmbedding(embeddingVector);

        // ── 3. Output / Return ──
        return new TraitEntry
        {
            BodyHash      = snapshot.BodyHash,
            Summary       = summary,
            IsUtility     = isUtility,
            Tags          = extractedTags.Take(12).ToList(),
            TagSource     = TraitEntry.SourceAutoCpu,
            EmbeddingBlob = embeddingBlob
        };
    }

    public static bool InferIsUtility(SymbolSnapshot snapshot)
    {
        return IsHighValueUtilityCandidate(
            snapshot.FilePath,
            snapshot.ClassName,
            snapshot.SymbolName,
            snapshot.SymbolType,
            snapshot.Signature,
            snapshot.ExtractedContent,
            snapshot.HasExecutableBody);
    }

    /// <summary>
    /// Determines whether a symbol belongs to a high-value utility island (e.g. <c>*Extensions*</c>, <c>*Utilities*</c>,
    /// <c>*Helpers*</c>, <c>*Common*</c>, <c>*Shared*</c>, or non-test <c>static</c> helper methods) while excluding
    /// unit test fixtures (<c>*Tests*</c>, <c>[Fact]</c>, <c>[Theory]</c>, <c>[Test]</c>).
    /// </summary>
    public static bool IsHighValueUtilityCandidate(
        string? filePath,
        string? className,
        string? symbolName,
        string? symbolType,
        string? signature,
        string? extractedContent,
        bool hasExecutableBody = true)
    {
        // ── 1. Declarations ──
        string normPath;
        string normClass;
        string normSymbol;
        string normSig;
        string normContent;

        // ── 2. Preconditions & Guard Clauses ──
        normPath    = (filePath ?? string.Empty).Replace('\\', '/');
        normClass   = className ?? string.Empty;
        normSymbol  = symbolName ?? string.Empty;
        normSig     = signature ?? string.Empty;
        normContent = extractedContent ?? string.Empty;

        if (!hasExecutableBody)
        {
            return false;
        }

        // Exclude unit test fixtures and test methods so static test setup helpers do not pollute production utilities.
        if (IsTestSymbolOrFile(normPath, normClass, normContent))
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        if (MatchesUtilityNamingConventions(normPath, normClass, normSymbol) ||
            IsExtensionMethod(normContent, normSig))
        {
            return true;
        }

        // Exclude Azure Function entry points and MVC Controller actions from being auto-classified as reusable utilities
        if (normClass.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) ||
            normPath.Contains("/Controllers/", StringComparison.OrdinalIgnoreCase) ||
            normPath.Contains("/Functions/", StringComparison.OrdinalIgnoreCase) ||
            normContent.Contains("[FunctionName(", StringComparison.Ordinal) ||
            normContent.Contains("[Function(", StringComparison.Ordinal))
        {
            return false;
        }

        if ((string.Equals(symbolType, "Method", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(symbolType, "Function", StringComparison.OrdinalIgnoreCase)) &&
            (normSig.Contains("static ", StringComparison.Ordinal) ||
             normContent.Contains("static ", StringComparison.Ordinal)))
        {
            return true;
        }

        // ── 4. Output / Return ──
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the symbol resides inside a dedicated utility/extension/helper class or directory
    /// (Tier 0 "High-Value Utility Island"), or is an extension method (<c>this T</c>).
    /// </summary>
    public static bool IsDedicatedUtilityIsland(
        string? filePath,
        string? className,
        string? symbolName,
        string? signature,
        string? extractedContent)
    {
        var normPath    = (filePath ?? string.Empty).Replace('\\', '/');
        var normClass   = className ?? string.Empty;
        var normSymbol  = symbolName ?? string.Empty;
        var normSig     = signature ?? string.Empty;
        var normContent = extractedContent ?? string.Empty;

        if (IsTestSymbolOrFile(normPath, normClass, normContent))
        {
            return false;
        }

        return MatchesUtilityNamingConventions(normPath, normClass, normSymbol) ||
               IsExtensionMethod(normContent, normSig);
    }

    private static bool IsTestSymbolOrFile(string normPath, string normClass, string normContent)
    {
        if (normPath.Contains(".Tests/", StringComparison.OrdinalIgnoreCase) ||
            normPath.Contains("/Tests/", StringComparison.OrdinalIgnoreCase) ||
            normPath.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase) ||
            normPath.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase) ||
            normPath.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase) ||
            normPath.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase) ||
            normClass.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
            normClass.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
            normContent.Contains("[Fact]", StringComparison.Ordinal) ||
            normContent.Contains("[Theory]", StringComparison.Ordinal) ||
            normContent.Contains("[Test]", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool MatchesUtilityNamingConventions(string normPath, string normClass, string normSymbol)
    {
        string[] utilityIndicators =
        [
            "Extension", "Helper", "Utility", "Utilities", "Utils",
            "Formatter", "Parser", "Validator", "Converter", "Guard"
        ];

        foreach (var indicator in utilityIndicators)
        {
            if (normClass.Contains(indicator, StringComparison.OrdinalIgnoreCase) ||
                normSymbol.EndsWith(indicator, StringComparison.OrdinalIgnoreCase) ||
                normPath.Contains($"/{indicator}", StringComparison.OrdinalIgnoreCase) ||
                normPath.EndsWith($"{indicator}.cs", StringComparison.OrdinalIgnoreCase) ||
                normPath.EndsWith($"{indicator}s.cs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (normPath.Contains("/Shared/Utils/", StringComparison.OrdinalIgnoreCase) ||
            normPath.Contains("/Common/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string BuildAutoSummary(SymbolSnapshot snapshot, IReadOnlyList<string> tags)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.DocComment))
        {
            return snapshot.DocComment.NormaliseWhitespace();
        }

        var tagDesc = tags.Count > 0 ? string.Join(", ", tags.Take(5)) : "ONNX semantic vector indexed";
        return $"Auto-indexed {snapshot.SymbolType.ToLowerInvariant()} '{snapshot.SymbolName}' ({tagDesc}).";
    }

    private static void ExtractZeroCostStructuralTraits(
        SymbolSnapshot snapshot,
        List<string> extractedTags,
        HashSet<string> seenTags)
    {
        // ── 1. Declarations ──
        string content;
        string signature;

        // ── 2. Preconditions & Guard Clauses ──
        content   = snapshot.ExtractedContent ?? string.Empty;
        signature = snapshot.Signature ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(signature))
        {
            return;
        }

        // ── 3. Intent-First Narrative ──
        if (IsExtensionMethod(content, signature))
        {
            AddTagIfNew("extension_method", extractedTags, seenTags);
        }

        if (ContainsAny(content, signature, "CancellationToken"))
        {
            AddTagIfNew("cancellable_async", extractedTags, seenTags);
        }

        if (ContainsAny(content, signature, "BeginTransaction", "BeginTransactionAsync", "TransactionScope", "IDbContextTransaction"))
        {
            AddTagIfNew("database_transaction", extractedTags, seenTags);
        }

        if (ContainsAny(content, signature, "sp_getapplock", "AcquireAppLock", "ReleaseAppLock", "AppLock"))
        {
            AddTagIfNew("uses_applock", extractedTags, seenTags);
        }

        if (ContainsAny(content, signature, "IRetryService", "RetryAsync", "RetryWithBackoff", "RetryPolicy", "AsyncRetryPolicy"))
        {
            AddTagIfNew("has_retry_logic", extractedTags, seenTags);
        }

        if (ContainsAny(content, signature, "IRedisCacheActions", "IRedisClientsManager", "GetOrSetHashEntryAsync", "GetHashEntryAsync", "SetHashEntryAsync"))
        {
            AddTagIfNew("accesses_redis", extractedTags, seenTags);
        }
    }

    private static bool IsExtensionMethod(string content, string signature)
    {
        return (content.Contains("static ", StringComparison.Ordinal) || signature.Contains("static ", StringComparison.Ordinal)) &&
               (content.Contains("(this ", StringComparison.Ordinal) || signature.Contains("(this ", StringComparison.Ordinal));
    }

    private static bool ContainsAny(string content, string signature, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (content.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                signature.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddTagIfNew(string tag, List<string> extractedTags, HashSet<string> seenTags)
    {
        if (seenTags.Add(tag))
        {
            extractedTags.Add(tag);
        }
    }
}
