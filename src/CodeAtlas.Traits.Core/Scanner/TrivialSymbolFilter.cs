using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Core.Scanner;

/// <summary>
/// Pre-filters symbols before AI capability tagging to eliminate noise
/// (auto-properties, simple field-assignment constructors, trivial 1-line pass-throughs).
/// </summary>
public static class TrivialSymbolFilter
{
    private static readonly HashSet<string> TrackableSymbolTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Class",
        "Method",
        "Function"
    };

    /// <summary>
    /// Determines whether a symbol type is eligible for capability indexing (Classes, Methods, Functions).
    /// </summary>
    public static bool IsTrackableSymbolType(string? symbolType)
    {
        // ── 1. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(symbolType))
        {
            return false;
        }

        // ── 2. Output / Return ──
        return TrackableSymbolTypes.Contains(symbolType.Trim());
    }

    /// <summary>
    /// Determines whether a method/function or class skeleton is trivial boilerplate
    /// that does not warrant AI tagging.
    /// </summary>
    public static bool IsTrivialSymbol(AtlasSymbolDto symbol, string extractedContent)
    {
        // ── 1. Declarations ──
        string normalisedContent;
        int lineSpan;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(extractedContent))
        {
            return true;
        }

        // ── 3. Intent-First Narrative ──
        normalisedContent = extractedContent.NormaliseWhitespace();
        if (normalisedContent.Length < 15)
        {
            return true;
        }

        if (string.Equals(symbol.Type, "Class", StringComparison.OrdinalIgnoreCase))
        {
            return IsTrivialClassSkeleton(extractedContent);
        }

        lineSpan = Math.Max(1, symbol.EndLine - symbol.StartLine + 1);
        if (IsAutoPropertyOrEmptyBody(normalisedContent))
        {
            return true;
        }

        if (lineSpan <= 2 && IsZeroLogicFieldOrConstantGetter(normalisedContent))
        {
            return true;
        }

        // ── 4. Output / Return ──
        return false;
    }

    /// <summary>
    /// Determines whether a member signature represents a private method that should be excluded
    /// from a class's public contract skeleton.
    /// </summary>
    public static bool IsPrivateMember(AtlasSymbolDto memberSymbol, string? rawMemberSlice)
    {
        // ── 1. Declarations ──
        string headerCheck;

        // ── 2. Preconditions & Guard Clauses ──
        headerCheck = !string.IsNullOrWhiteSpace(memberSymbol.Signature)
            ? memberSymbol.Signature
            : (rawMemberSlice ?? string.Empty);

        if (string.IsNullOrWhiteSpace(headerCheck))
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        if (headerCheck.Contains("private ", StringComparison.Ordinal) ||
            headerCheck.TrimStart().StartsWith('#'))
        {
            return true;
        }

        // ── 4. Output / Return ──
        return false;
    }

    private static bool IsTrivialClassSkeleton(string skeletonText)
    {
        // ── 1. Declarations ──
        string[] lines;

        // ── 2. Preconditions & Guard Clauses ──
        lines = skeletonText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // ── 3. Output / Return ──
        // A class with no inheritance and zero non-private methods/constructors is a pure DTO/marker.
        return lines.Length <= 1 && !lines[0].Contains(':');
    }

    private static bool IsAutoPropertyOrEmptyBody(string normalisedContent)
    {
        // ── 1. Declarations ──
        int openBrace;
        int closeBrace;
        string insideBraces;

        // ── 2. Preconditions & Guard Clauses ──
        if (normalisedContent.Contains("{ get; set; }", StringComparison.Ordinal) ||
            normalisedContent.Contains("{ get; init; }", StringComparison.Ordinal) ||
            normalisedContent.Contains("{ get; }", StringComparison.Ordinal))
        {
            return true;
        }

        // ── 3. Intent-First Narrative ──
        openBrace  = normalisedContent.IndexOf('{');
        closeBrace = normalisedContent.LastIndexOf('}');
        if (openBrace >= 0 && closeBrace > openBrace)
        {
            insideBraces = normalisedContent[(openBrace + 1)..closeBrace].Trim();
            if (insideBraces.Length == 0)
            {
                return true;
            }
        }

        // ── 4. Output / Return ──
        return false;
    }

    private static bool IsZeroLogicFieldOrConstantGetter(string normalisedContent)
    {
        // ── 1. Declarations ──
        int arrowIndex;
        string headerPart;
        string expressionBody;

        // ── 2. Preconditions & Guard Clauses ──
        arrowIndex = normalisedContent.IndexOf("=>", StringComparison.Ordinal);
        if (arrowIndex < 0)
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        headerPart     = normalisedContent[..arrowIndex].Trim();
        expressionBody = normalisedContent[(arrowIndex + 2)..].Trim().TrimEnd(';').Trim();

        // Any method taking parameters (e.g. private static bool HashesMatch(string a, string b) => a == b)
        // is a real method and must NOT be filtered out as trivial.
        var openParen  = headerPart.IndexOf('(');
        var closeParen = headerPart.LastIndexOf(')');
        if (openParen >= 0 && closeParen > openParen &&
            !string.IsNullOrWhiteSpace(headerPart[(openParen + 1)..closeParen]))
        {
            return false;
        }

        // Only a bare field/constant return with no calls, member chains, or operators is trivial.
        return expressionBody.Length > 0 &&
               expressionBody.Length < 25 &&
               expressionBody.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
    }
}
