using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Core.Scanner;

/// <summary>
/// Pre-filters symbols before AI capability tagging to eliminate noise
/// (auto-properties, interface/abstract contract declarations without bodies,
/// simple field-assignment constructors, trivial 1-line pass-throughs) and computes statement weight.
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
    /// Determines whether a symbol has an executable body (as opposed to an interface signature
    /// or <c>abstract</c> / <c>extern</c> method declaration with <c>Body == null</c>).
    /// </summary>
    public static bool HasExecutableBody(
        AtlasSymbolDto symbol,
        string extractedContent,
        IReadOnlyList<AtlasSymbolDto>? fileSymbols = null)
    {
        // ── 1. Declarations ──
        string normalised;
        AtlasSymbolDto? enclosingType;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(extractedContent))
        {
            return false;
        }

        if (string.Equals(symbol.Type, "Class", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // ── 3. Intent-First Narrative ──
        if (fileSymbols is not null && fileSymbols.Count > 0)
        {
            enclosingType = fileSymbols.FirstOrDefault(c =>
                (string.Equals(c.Type, "Interface", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Type, "Class", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Type, "Struct", StringComparison.OrdinalIgnoreCase)) &&
                c.StartLine <= symbol.StartLine &&
                c.EndLine >= symbol.EndLine &&
                !ReferenceEquals(c, symbol));

            if (enclosingType is not null &&
                string.Equals(enclosingType.Type, "Interface", StringComparison.OrdinalIgnoreCase) &&
                !extractedContent.Contains('{') &&
                !extractedContent.Contains("=>", StringComparison.Ordinal))
            {
                return false;
            }
        }

        normalised = extractedContent.NormaliseWhitespace();

        // Any method/function declaration that contains neither a block '{' nor an expression body '=>'
        // is a pure signature contract (interface method, abstract method, extern, or TypeScript overload/ambient signature).
        if (!normalised.Contains('{') && !normalised.Contains("=>", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalised.StartsWith("abstract ", StringComparison.Ordinal) ||
            normalised.Contains(" abstract ", StringComparison.Ordinal))
        {
            return false;
        }

        // ── 4. Output / Return ──
        return true;
    }

    /// <summary>
    /// Computes a lightweight cognitive weight / statement count for a symbol so single-statement
    /// or single-property boilerplate can be distinguished from multi-statement logic.
    /// </summary>
    public static int EstimateStatementCount(
        AtlasSymbolDto symbol,
        string extractedContent,
        bool hasExecutableBody)
    {
        // ── 1. Declarations ──
        string[] lines;
        int memberCount;
        string normalised;
        int openBrace;
        int closeBrace;
        string bodySlice;
        int semicolons;
        int arrows;
        int branches;

        // ── 2. Preconditions & Guard Clauses ──
        if (!hasExecutableBody || string.IsNullOrWhiteSpace(extractedContent))
        {
            return 0;
        }

        // ── 3. Intent-First Narrative ──
        if (string.Equals(symbol.Type, "Class", StringComparison.OrdinalIgnoreCase))
        {
            lines       = extractedContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            memberCount = Math.Max(0, lines.Length - 1);
            if (lines.Length > 0 && HasClassInheritanceList(lines[0]))
            {
                memberCount++;
            }

            return memberCount;
        }

        normalised = extractedContent.NormaliseWhitespace();
        openBrace  = normalised.IndexOf('{');
        closeBrace = normalised.LastIndexOf('}');

        if (openBrace >= 0 && closeBrace > openBrace)
        {
            bodySlice = normalised[(openBrace + 1)..closeBrace];
        }
        else
        {
            var arrowIdx = normalised.IndexOf("=>", StringComparison.Ordinal);
            bodySlice = arrowIdx >= 0 ? normalised[(arrowIdx + 2)..] : normalised;
        }

        semicolons = bodySlice.Count(ch => ch == ';');
        arrows     = CountSubstringOccurrences(bodySlice, "=>");
        branches   = CountBranchKeywords(bodySlice);

        // ── 4. Output / Return ──
        return Math.Max(1, semicolons + arrows + branches);
    }

    /// <summary>
    /// Determines whether a method/function or class skeleton is trivial boilerplate
    /// that does not warrant standalone AI tagging or standalone duplication alerts.
    /// </summary>
    public static bool IsTrivialSymbol(
        AtlasSymbolDto symbol,
        string extractedContent,
        bool hasExecutableBody = true)
    {
        // ── 1. Declarations ──
        string normalisedContent;
        int lineSpan;

        // ── 2. Preconditions & Guard Clauses ──
        if (!hasExecutableBody || string.IsNullOrWhiteSpace(extractedContent))
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

    internal static bool HasClassInheritanceList(string classHeaderLine)
    {
        // ── 1. Declarations ──
        int colonIndex;
        string afterColon;

        // ── 2. Preconditions & Guard Clauses ──
        colonIndex = classHeaderLine.IndexOf(':');
        if (colonIndex < 0 || colonIndex + 1 >= classHeaderLine.Length)
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        afterColon = classHeaderLine[(colonIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(afterColon) ||
            afterColon.StartsWith("public ", StringComparison.Ordinal) ||
            afterColon.StartsWith("internal ", StringComparison.Ordinal) ||
            afterColon.StartsWith("private ", StringComparison.Ordinal) ||
            afterColon.StartsWith("protected ", StringComparison.Ordinal) ||
            afterColon.StartsWith("class ", StringComparison.Ordinal))
        {
            return false;
        }

        // ── 4. Output / Return ──
        return true;
    }

    private static bool IsTrivialClassSkeleton(string skeletonText)
    {
        // ── 1. Declarations ──
        string[] lines;

        // ── 2. Preconditions & Guard Clauses ──
        lines = skeletonText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // ── 3. Output / Return ──
        // A class with no inheritance and zero non-private methods/constructors is a pure DTO/marker.
        return lines.Length <= 1 && (lines.Length == 0 || !HasClassInheritanceList(lines[0]));
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

    private static int CountSubstringOccurrences(string text, string pattern)
    {
        // ── 1. Declarations ──
        int count;
        int idx;

        // ── 2. Intent-First Narrative ──
        count = 0;
        idx   = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }

        // ── 3. Output / Return ──
        return count;
    }

    private static int CountBranchKeywords(string bodySlice)
    {
        // ── 1. Declarations ──
        int count;
        string[] keywords;

        // ── 2. Intent-First Narrative ──
        count    = 0;
        keywords = ["if (", "if(", "for (", "for(", "foreach (", "foreach(", "while (", "while(", "switch (", "switch(", "catch"];
        foreach (var kw in keywords)
        {
            count += CountSubstringOccurrences(bodySlice, kw);
        }

        // ── 3. Output / Return ──
        return count;
    }
}
