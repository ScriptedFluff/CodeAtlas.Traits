using System.Text;
using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Core.Scanner;

/// <summary>
/// Computes Type-2 structural hashes (<c>structural_hash</c>) for classes and methods:
/// - **Classes**: Normalises the class name to <c>$CLASS</c> in the header and constructors while preserving
///   base types and member signatures, catching renamed DTOs and model clones with identical properties/methods.
/// - **Methods / Functions**: Normalises the method name, declared parameter names (<c>$P0</c>, <c>$P1</c>),
///   and local variable identifiers (<c>$V0</c>, <c>$V1</c>) while preserving control-flow keywords,
///   external service/field references, and member method calls.
/// </summary>
public static class StructuralNormaliser
{
    private static readonly HashSet<string> LanguageKeywords = new(StringComparer.Ordinal)
    {
        "if", "else", "for", "foreach", "in", "while", "do", "switch", "case", "default",
        "break", "continue", "return", "yield", "await", "async", "using", "lock",
        "try", "catch", "finally", "throw", "new", "var", "let", "const", "function",
        "null", "true", "false", "this", "base", "typeof", "nameof", "sizeof",
        "is", "as", "not", "and", "or", "when", "where", "select", "from", "orderby",
        "int", "long", "short", "byte", "bool", "string", "char", "decimal", "double", "float",
        "object", "void", "dynamic", "Task", "ValueTask", "CancellationToken", "Guid",
        "DateTime", "DateTimeOffset", "TimeSpan", "Exception", "List", "IReadOnlyList",
        "IEnumerable", "ICollection", "Dictionary", "HashSet", "Math", "Console", "String"
    };

    private static readonly HashSet<string> DeclarationKeywords = new(StringComparer.Ordinal)
    {
        "var", "let", "const", "int", "long", "short", "byte", "bool", "string", "char",
        "decimal", "double", "float", "object", "Guid", "DateTime", "DateTimeOffset", "TimeSpan",
        "out"
    };

    /// <summary>
    /// Computes the Type-2 <c>structural_hash</c> for a class contract skeleton or method/function slice.
    /// </summary>
    public static string ComputeStructuralHash(
        AtlasSymbolDto symbol,
        string extractedContent,
        string fallbackBodyHash)
    {
        // ── 1. Declarations ──
        string normalisedStructure;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(extractedContent))
        {
            return fallbackBodyHash;
        }

        // ── 3. Intent-First Narrative ──
        if (string.Equals(symbol.Type, "Class", StringComparison.OrdinalIgnoreCase))
        {
            normalisedStructure = NormaliseClassSkeleton(symbol.Name, extractedContent);
        }
        else
        {
            normalisedStructure = NormaliseMethodBody(symbol.Name, extractedContent);
        }

        if (string.IsNullOrWhiteSpace(normalisedStructure))
        {
            return fallbackBodyHash;
        }

        // ── 4. Output / Return ──
        return normalisedStructure.ComputeContentHash();
    }

    /// <summary>
    /// Replaces the class name with <c>$CLASS</c> in the skeleton header and constructors while
    /// preserving inheritance and all sorted member signatures.
    /// </summary>
    public static string NormaliseClassSkeleton(string className, string skeletonText)
    {
        // ── 1. Declarations ──
        string[] lines;
        StringBuilder sb;
        string headerLine;
        int colonIdx;
        string inheritancePart;

        // ── 2. Preconditions & Guard Clauses ──
        lines = skeletonText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        // ── 3. Intent-First Narrative ──
        sb         = new StringBuilder();
        headerLine = lines[0];
        colonIdx   = headerLine.IndexOf(':');

        if (colonIdx >= 0 && TrivialSymbolFilter.HasClassInheritanceList(headerLine))
        {
            inheritancePart = headerLine[(colonIdx + 1)..].NormaliseWhitespace();
            sb.AppendLine($"class $CLASS : {inheritancePart}");
        }
        else
        {
            sb.AppendLine("class $CLASS");
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var memberLine = lines[i];
            if (memberLine.StartsWith($"ctor: {className}(", StringComparison.Ordinal))
            {
                memberLine = "ctor: $CLASS(" + memberLine[$"ctor: {className}(".Length..];
            }

            sb.AppendLine(memberLine.NormaliseWhitespace());
        }

        // ── 4. Output / Return ──
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Normalises a method or function body by mapping declared parameter names to <c>$P0..$Pn</c>
    /// and local variable declarations to <c>$V0..$Vn</c>, while preserving member calls and external symbols.
    /// </summary>
    public static string NormaliseMethodBody(string methodName, string rawMethodText)
    {
        // ── 1. Declarations ──
        Dictionary<string, string> identifierMap;
        string headerPart;
        string bodyPart;
        IReadOnlyList<string> paramNames;
        List<LexToken> tokens;
        StringBuilder sb;
        int localVarCounter;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(rawMethodText))
        {
            return string.Empty;
        }

        // ── 3. Intent-First Narrative ──
        identifierMap          = new Dictionary<string, string>(StringComparer.Ordinal);
        (headerPart, bodyPart) = SplitHeaderAndBody(rawMethodText);

        if (!string.IsNullOrWhiteSpace(methodName))
        {
            identifierMap[methodName] = "$SELF";
        }

        paramNames = ExtractParameterNames(headerPart);
        for (var i = 0; i < paramNames.Count; i++)
        {
            identifierMap[paramNames[i]] = $"$P{i}";
        }

        tokens          = TokeniseCode(bodyPart);
        sb              = new StringBuilder(bodyPart.Length);
        localVarCounter = 0;

        for (var i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i];
            if (tok.Kind != LexTokenKind.Identifier)
            {
                sb.Append(tok.Text);
                sb.Append(' ');
                continue;
            }

            var prevTok = i > 0 ? tokens[i - 1] : default;
            var nextTok = i + 1 < tokens.Count ? tokens[i + 1] : default;

            // Never rename member access targets after '.' (e.g. .Trim, .UpdateInvestor)
            if (prevTok.Kind == LexTokenKind.Punctuation && prevTok.Text == ".")
            {
                sb.Append(tok.Text);
                sb.Append(' ');
                continue;
            }

            if (identifierMap.TryGetValue(tok.Text, out var mappedName))
            {
                sb.Append(mappedName);
                sb.Append(' ');
                continue;
            }

            if (LanguageKeywords.Contains(tok.Text))
            {
                sb.Append(tok.Text);
                sb.Append(' ');
                continue;
            }

            if (IsLocalVariableDeclaration(tokens, i, prevTok, nextTok))
            {
                var placeholder = $"$V{localVarCounter++}";
                identifierMap[tok.Text] = placeholder;
                sb.Append(placeholder);
                sb.Append(' ');
                continue;
            }

            sb.Append(tok.Text);
            sb.Append(' ');
        }

        // ── 4. Output / Return ──
        return sb.ToString().Trim();
    }

    private static (string Header, string Body) SplitHeaderAndBody(string rawMethodText)
    {
        // ── 1. Declarations ──
        int openBrace;
        int closeBrace;
        int arrowIdx;

        // ── 2. Intent-First Narrative ──
        openBrace  = rawMethodText.IndexOf('{');
        closeBrace = rawMethodText.LastIndexOf('}');
        arrowIdx   = rawMethodText.IndexOf("=>", StringComparison.Ordinal);

        if (openBrace >= 0 && (arrowIdx < 0 || openBrace < arrowIdx) && closeBrace > openBrace)
        {
            return (rawMethodText[..openBrace], rawMethodText[(openBrace + 1)..closeBrace]);
        }

        if (arrowIdx >= 0)
        {
            return (rawMethodText[..arrowIdx], rawMethodText[(arrowIdx + 2)..]);
        }

        // ── 3. Output / Return ──
        return (rawMethodText, rawMethodText);
    }

    private static IReadOnlyList<string> ExtractParameterNames(string headerPart)
    {
        // ── 1. Declarations ──
        int openParen;
        int closeParen;
        string insideParens;
        string[] rawParams;
        List<string> names;

        // ── 2. Preconditions & Guard Clauses ──
        openParen  = headerPart.IndexOf('(');
        closeParen = headerPart.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen + 1)
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        insideParens = headerPart[(openParen + 1)..closeParen];
        rawParams    = insideParens.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        names        = [];

        foreach (var rawParam in rawParams)
        {
            var beforeDefault = rawParam.Split('=')[0].Trim();
            var colonSplit    = beforeDefault.Split(':');
            if (colonSplit.Length == 2)
            {
                // TypeScript/JavaScript style: paramName: Type
                var tsName = colonSplit[0].Trim().TrimStart('.', '?');
                if (IsValidIdentifier(tsName))
                {
                    names.Add(tsName);
                }
                continue;
            }

            // C# style: [ modifiers ] Type paramName
            var parts = beforeDefault.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var candidate = parts[^1].Trim();
                if (IsValidIdentifier(candidate) && !LanguageKeywords.Contains(candidate))
                {
                    names.Add(candidate);
                }
            }
        }

        // ── 4. Output / Return ──
        return names;
    }

    private static bool IsLocalVariableDeclaration(
        IReadOnlyList<LexToken> tokens,
        int index,
        LexToken prevTok,
        LexToken nextTok)
    {
        // Pattern 1: var/let/const/int/string/bool <ident> = / in / ; / , / )
        if (prevTok.Kind == LexTokenKind.Identifier &&
            DeclarationKeywords.Contains(prevTok.Text) &&
            nextTok.Kind == LexTokenKind.Punctuation &&
            nextTok.Text is "=" or ";" or "," or ")" or "in")
        {
            return true;
        }

        // Pattern 2: foreach (var/Type <ident> in ...) or catch (Exception <ident>)
        if (prevTok.Kind == LexTokenKind.Identifier &&
            !LanguageKeywords.Contains(prevTok.Text) &&
            nextTok.Kind == LexTokenKind.Punctuation &&
            nextTok.Text is "=" or ";" &&
            index >= 2 &&
            tokens[index - 2].Text is ";" or "{" or "(" or ")")
        {
            return true;
        }

        return false;
    }

    private static bool IsValidIdentifier(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return (char.IsLetter(text[0]) || text[0] == '_' || text[0] == '$') &&
               text.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '$');
    }

    private static List<LexToken> TokeniseCode(string text)
    {
        // ── 1. Declarations ──
        List<LexToken> tokens;
        int i;
        int len;

        // ── 2. Intent-First Narrative ──
        tokens = [];
        i      = 0;
        len    = text.Length;

        while (i < len)
        {
            var ch = text[i];

            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            // Single-line comment
            if (ch == '/' && i + 1 < len && text[i + 1] == '/')
            {
                while (i < len && text[i] != '\n') i++;
                continue;
            }

            // Multi-line comment
            if (ch == '/' && i + 1 < len && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < len && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i += 2;
                continue;
            }

            // String or char literal
            if (ch is '"' or '\'')
            {
                var quote = ch;
                i++;
                while (i < len && text[i] != quote)
                {
                    if (text[i] == '\\' && i + 1 < len) i += 2;
                    else i++;
                }
                if (i < len) i++;
                tokens.Add(new LexToken(LexTokenKind.Literal, "$STR"));
                continue;
            }

            // Identifier or keyword
            if (char.IsLetter(ch) || ch == '_' || ch == '$')
            {
                var start = i;
                while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$'))
                {
                    i++;
                }

                var word = text[start..i];
                if (word == "in")
                {
                    tokens.Add(new LexToken(LexTokenKind.Punctuation, "in"));
                }
                else
                {
                    tokens.Add(new LexToken(LexTokenKind.Identifier, word));
                }
                continue;
            }

            // Numeric literal
            if (char.IsDigit(ch))
            {
                while (i < len && (char.IsLetterOrDigit(text[i]) || text[i] == '.'))
                {
                    i++;
                }
                tokens.Add(new LexToken(LexTokenKind.Literal, "$NUM"));
                continue;
            }

            // Multi-char operators
            if (i + 1 < len)
            {
                var twoChar = text.Substring(i, 2);
                if (twoChar is "==" or "!=" or "<=" or ">=" or "&&" or "||" or "??" or "=>" or "+=" or "-=" or "?.")
                {
                    tokens.Add(new LexToken(LexTokenKind.Punctuation, twoChar));
                    i += 2;
                    continue;
                }
            }

            tokens.Add(new LexToken(LexTokenKind.Punctuation, ch.ToString()));
            i++;
        }

        // ── 3. Output / Return ──
        return tokens;
    }

    private enum LexTokenKind
    {
        Identifier,
        Literal,
        Punctuation
    }

    private readonly record struct LexToken(LexTokenKind Kind, string Text);
}
