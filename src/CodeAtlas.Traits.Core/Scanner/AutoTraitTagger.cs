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
        var rawTokens      = combinedSourceText.ExtractCodeWordTokens();
        extractedTags      = [];
        seenTags           = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
}
