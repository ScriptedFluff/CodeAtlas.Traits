using System.Security.Cryptography;
using System.Text;

namespace CodeAtlas.Traits.Core.Extensions;

/// <summary>
/// Shared general-purpose string, normalisation, and cryptographic hashing extensions.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Collapses contiguous whitespace sequences into a single space and trims leading/trailing whitespace
    /// so formatting or line-ending differences (<c>\r\n</c> vs <c>\n</c>) do not alter content hashes.
    /// </summary>
    public static string NormaliseWhitespace(this string? value)
    {
        // ── 1. Declarations ──
        StringBuilder builder;
        bool previousWasWhitespace;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // ── 3. Intent-First Narrative ──
        builder               = new StringBuilder(value.Length);
        previousWasWhitespace = false;

        foreach (var ch in value.AsSpan().Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }
            }
            else
            {
                builder.Append(ch);
                previousWasWhitespace = false;
            }
        }

        // ── 4. Output / Return ──
        return builder.ToString();
    }

    /// <summary>
    /// Normalises a capability or technology tag to lowercase kebab/trimmed format.
    /// </summary>
    public static string NormaliseTag(this string? rawTag)
    {
        // ── 1. Declarations ──
        string trimmed;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return string.Empty;
        }

        // ── 3. Intent-First Narrative ──
        trimmed = rawTag.Trim().ToLowerInvariant();

        // ── 4. Output / Return ──
        return trimmed;
    }

    /// <summary>
    /// Normalises file paths to forward-slash relative representation.
    /// </summary>
    public static string NormaliseRelativePath(this string? path)
    {
        // ── 1. Declarations ──
        string normalised;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        // ── 3. Intent-First Narrative ──
        normalised = path.Trim().Replace('\\', '/');

        // ── 4. Output / Return ──
        return normalised;
    }

    /// <summary>
    /// Computes a deterministic 16-character lowercase hex digest (64-bit prefix of SHA-256)
    /// over the UTF-8 representation of <paramref name="content"/>.
    /// </summary>
    public static string ComputeContentHash(this string? content)
    {
        // ── 1. Declarations ──
        byte[] utf8Bytes;
        byte[] hashBytes;
        string hexDigest;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrEmpty(content))
        {
            return "0000000000000000";
        }

        // ── 3. Intent-First Narrative ──
        utf8Bytes = Encoding.UTF8.GetBytes(content);
        hashBytes = SHA256.HashData(utf8Bytes);
        hexDigest = Convert.ToHexStringLower(hashBytes.AsSpan(0, 8));

        // ── 4. Output / Return ──
        return hexDigest;
    }

    /// <summary>
    /// Extracts a UTF-8 byte slice safely from a byte buffer or falls back to line-range extraction.
    /// </summary>
    public static string SliceUtf8OrLines(
        this byte[] fileBytes,
        int startByte,
        int endByte,
        int startLine,
        int endLine)
    {
        // ── 1. Declarations ──
        int length;
        string fullText;
        string[] lines;
        int clampedStart;
        int clampedEnd;

        // ── 2. Preconditions & Guard Clauses ──
        if (fileBytes.Length == 0)
        {
            return string.Empty;
        }

        // ── 3. Intent-First Narrative ──
        if (startByte >= 0 && endByte > startByte && endByte <= fileBytes.Length)
        {
            length = endByte - startByte;
            return Encoding.UTF8.GetString(fileBytes, startByte, length);
        }

        fullText = Encoding.UTF8.GetString(fileBytes);
        lines    = fullText.Split('\n');
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        clampedStart = Math.Clamp(startLine - 1, 0, lines.Length - 1);
        clampedEnd   = Math.Clamp(endLine - 1, clampedStart, lines.Length - 1);

        // ── 4. Output / Return ──
        return string.Join("\n", lines[clampedStart..(clampedEnd + 1)]);
    }

    /// <summary>
    /// Parses a file and line location string such as <c>"MyClass.cs:L35"</c>, <c>"MyClass.cs:35"</c>,
    /// or <c>"src/MyClass.cs:L35-L50"</c> (preserving Windows drive colons like <c>"D:\..."</c>).
    /// </summary>
    public static bool TryParseFileLineReference(
        this string? rawReference,
        out string filePathPattern,
        out int lineNumber)
    {
        // ── 1. Declarations ──
        string trimmed;
        int lastColonIndex;
        string filePart;
        string linePart;
        int dashIndex;

        // ── 2. Preconditions & Guard Clauses ──
        filePathPattern = string.Empty;
        lineNumber      = 0;

        if (string.IsNullOrWhiteSpace(rawReference))
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        trimmed        = rawReference.Trim().Trim('"', '\'');
        lastColonIndex = trimmed.LastIndexOf(':');

        // Ensure the colon is not a Windows drive letter colon (index 1, e.g. "C:\foo.cs")
        if (lastColonIndex <= 1 || lastColonIndex >= trimmed.Length - 1)
        {
            return false;
        }

        filePart = trimmed[..lastColonIndex].NormaliseRelativePath();
        linePart = trimmed[(lastColonIndex + 1)..].Trim();

        dashIndex = linePart.IndexOf('-');
        if (dashIndex > 0)
        {
            linePart = linePart[..dashIndex].Trim();
        }

        if (linePart.StartsWith("L", StringComparison.OrdinalIgnoreCase))
        {
            linePart = linePart[1..].Trim();
        }

        if (string.IsNullOrWhiteSpace(filePart) ||
            !int.TryParse(linePart, out lineNumber) ||
            lineNumber <= 0)
        {
            filePathPattern = string.Empty;
            lineNumber      = 0;
            return false;
        }

        // ── 4. Output / Return ──
        filePathPattern = filePart;
        return true;
    }

    /// <summary>
    /// Parses a symbol target reference such as <c>"MyNamespace.MyClass:MyMethod"</c>,
    /// <c>"MyClass:MyMethod"</c>, <c>"MyNamespace.MyClass.MyMethod"</c>, or <c>"MyMethod"</c>.
    /// Returns <c>false</c> if the input is empty or is a <c>&lt;file&gt;:L&lt;line&gt;</c> location reference.
    /// </summary>
    public static bool TryParseSymbolTargetReference(
        this string? rawReference,
        out string? containerPattern,
        out string symbolName)
    {
        // ── 1. Declarations ──
        string trimmed;
        int colonIndex;
        int lastDotIndex;
        string rawContainer;
        string rawSymbol;

        // ── 2. Preconditions & Guard Clauses ──
        containerPattern = null;
        symbolName       = string.Empty;

        if (string.IsNullOrWhiteSpace(rawReference) ||
            rawReference.TryParseFileLineReference(out _, out _))
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        trimmed = rawReference.Trim().Trim('"', '\'');

        // Support C++ / Rust / PowerShell "::" separator by normalising to single ':'
        trimmed    = trimmed.Replace("::", ":", StringComparison.Ordinal);
        colonIndex = trimmed.LastIndexOf(':');

        if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
        {
            rawContainer = trimmed[..colonIndex].Trim().Trim('.');
            rawSymbol    = StripMethodSignatureParameters(trimmed[(colonIndex + 1)..].Trim());

            if (string.IsNullOrWhiteSpace(rawSymbol))
            {
                return false;
            }

            containerPattern = string.IsNullOrWhiteSpace(rawContainer) ? null : rawContainer;
            symbolName       = rawSymbol;
            return true;
        }

        // No colon present: check for dot-qualified "MyNamespace.MyClass.MyMethod" or "MyClass.MyMethod"
        trimmed      = StripMethodSignatureParameters(trimmed);
        lastDotIndex = trimmed.LastIndexOf('.');

        if (lastDotIndex > 0 && lastDotIndex < trimmed.Length - 1)
        {
            rawContainer     = trimmed[..lastDotIndex].Trim().Trim('.');
            rawSymbol        = trimmed[(lastDotIndex + 1)..].Trim();
            containerPattern = string.IsNullOrWhiteSpace(rawContainer) ? null : rawContainer;
            symbolName       = rawSymbol;
            return !string.IsNullOrWhiteSpace(symbolName);
        }

        // ── 4. Output / Return ──
        symbolName = trimmed;
        return !string.IsNullOrWhiteSpace(symbolName);
    }

    private static string StripMethodSignatureParameters(string memberText)
    {
        // ── 1. Declarations ──
        int parenIndex;

        // ── 2. Intent-First Narrative ──
        parenIndex = memberText.IndexOf('(');
        if (parenIndex > 0)
        {
            return memberText[..parenIndex].Trim();
        }

        // ── 3. Output / Return ──
        return memberText;
    }

    /// <summary>
    /// Splits source code and camelCase/PascalCase/snake_case identifiers into lowercase word tokens.
    /// </summary>
    public static IReadOnlyList<string> ExtractCodeWordTokens(this string? text)
    {
        // ── 1. Declarations ──
        List<string> tokens;
        StringBuilder currentWord;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        tokens      = [];
        currentWord = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!char.IsLetterOrDigit(ch))
            {
                FlushTokenIfValid(currentWord, tokens);
                continue;
            }

            if (currentWord.Length > 0 &&
                char.IsUpper(ch) &&
                (char.IsLower(text[i - 1]) ||
                 (i + 1 < text.Length && char.IsLower(text[i + 1]))))
            {
                FlushTokenIfValid(currentWord, tokens);
            }

            currentWord.Append(char.ToLowerInvariant(ch));
        }

        FlushTokenIfValid(currentWord, tokens);

        // ── 4. Output / Return ──
        return tokens;
    }

    private static void FlushTokenIfValid(StringBuilder currentWord, List<string> tokens)
    {
        if (currentWord.Length >= 2)
        {
            tokens.Add(currentWord.ToString());
        }

        currentWord.Clear();
    }
}
