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

    private static bool InferIsUtility(SymbolSnapshot snapshot)
    {
        // ── 1. Declarations ──
        string content;
        string className;

        // ── 2. Intent-First Narrative ──
        content   = snapshot.ExtractedContent ?? string.Empty;
        className = snapshot.ClassName ?? string.Empty;

        if (content.Contains("static ", StringComparison.Ordinal) ||
            className.EndsWith("Extensions", StringComparison.OrdinalIgnoreCase) ||
            className.EndsWith("Helper", StringComparison.OrdinalIgnoreCase) ||
            className.EndsWith("Utility", StringComparison.OrdinalIgnoreCase) ||
            className.EndsWith("Utils", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // ── 3. Output / Return ──
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
