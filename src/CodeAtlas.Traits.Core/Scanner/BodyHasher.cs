using System.Text;
using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Core.Scanner;

/// <summary>
/// Computes content-addressable <c>body_hash</c> values across polyglot symbols:
/// - **Classes**: Hashed strictly on inheritance/signature, constructors, and non-private method signatures.
/// - **Methods / Functions**: Hashed on whitespace-normalised byte slice (<c>StartByte..EndByte</c>).
/// </summary>
public static class BodyHasher
{
    /// <summary>
    /// Builds <see cref="SymbolSnapshot"/> entries with computed <c>BodyHash</c> values for all symbols in a single file.
    /// </summary>
    public static IReadOnlyList<SymbolSnapshot> ComputeSnapshotsForFile(
        string repoRoot,
        string relativeFilePath,
        IReadOnlyList<AtlasSymbolDto> fileSymbols)
    {
        // ── 1. Declarations ──
        byte[] fileBytes;
        string? declaredNamespace;
        List<SymbolSnapshot> snapshots;
        Dictionary<string, List<AtlasSymbolDto>> membersByClass;

        // ── 2. Preconditions & Guard Clauses ──
        if (fileSymbols.Count == 0)
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        fileBytes         = ReadFileBytesQuietly(repoRoot, relativeFilePath);
        declaredNamespace = ExtractDeclaredNamespace(fileBytes);
        snapshots         = new List<SymbolSnapshot>(fileSymbols.Count);
        membersByClass    = GroupMembersByClassName(fileSymbols);

        foreach (var sym in fileSymbols)
        {
            if (!TrivialSymbolFilter.IsTrackableSymbolType(sym.Type))
            {
                continue;
            }

            var snapshot = CreateSymbolSnapshot(sym, fileBytes, declaredNamespace, fileSymbols, membersByClass);
            snapshots.Add(snapshot);
        }

        // ── 4. Output / Return ──
        return snapshots;
    }

    /// <summary>
    /// Builds the canonical contract skeleton for a class using only its inheritance/signature,
    /// its constructors, and its non-private method signatures.
    /// </summary>
    public static string BuildClassContractSkeleton(
        AtlasSymbolDto classSymbol,
        IReadOnlyList<AtlasSymbolDto> classMembers,
        byte[] fileBytes)
    {
        // ── 1. Declarations ──
        StringBuilder skeletonBuilder;
        string classHeader;
        List<string> constructorSignatures;
        List<string> publicMethodSignatures;

        // ── 2. Preconditions & Guard Clauses ──
        skeletonBuilder       = new StringBuilder();
        constructorSignatures = [];
        publicMethodSignatures = [];

        // ── 3. Intent-First Narrative ──
        classHeader = ExtractClassHeaderLine(classSymbol, fileBytes);
        skeletonBuilder.AppendLine(classHeader);

        foreach (var member in classMembers)
        {
            var rawSlice  = fileBytes.SliceUtf8OrLines(member.StartByte, member.EndByte, member.StartLine, member.EndLine);
            var headerSig = ExtractMemberHeaderLine(member, rawSlice);

            if (IsConstructorMember(classSymbol.Name, member, headerSig))
            {
                constructorSignatures.Add(headerSig.NormaliseWhitespace());
                continue;
            }

            if (TrivialSymbolFilter.IsPrivateMember(member, rawSlice))
            {
                continue;
            }

            publicMethodSignatures.Add(headerSig.NormaliseWhitespace());
        }

        constructorSignatures.Sort(StringComparer.Ordinal);
        publicMethodSignatures.Sort(StringComparer.Ordinal);

        foreach (var ctor in constructorSignatures)
        {
            skeletonBuilder.AppendLine($"  ctor: {ctor}");
        }

        foreach (var method in publicMethodSignatures)
        {
            skeletonBuilder.AppendLine($"  method: {method}");
        }

        // ── 4. Output / Return ──
        return skeletonBuilder.ToString().Trim();
    }

    private static SymbolSnapshot CreateSymbolSnapshot(
        AtlasSymbolDto symbol,
        byte[] fileBytes,
        string? declaredNamespace,
        IReadOnlyList<AtlasSymbolDto> fileSymbols,
        Dictionary<string, List<AtlasSymbolDto>> membersByClass)
    {
        // ── 1. Declarations ──
        string extractedContent;
        string normalisedForHash;
        string bodyHash;
        bool isTrivial;
        string? qualifiedClassName;

        // ── 2. Intent-First Narrative ──
        if (string.Equals(symbol.Type, "Class", StringComparison.OrdinalIgnoreCase))
        {
            membersByClass.TryGetValue(symbol.Name, out var members);
            extractedContent = BuildClassContractSkeleton(
                symbol,
                members ?? (IReadOnlyList<AtlasSymbolDto>)[],
                fileBytes);
        }
        else
        {
            extractedContent = fileBytes.SliceUtf8OrLines(
                symbol.StartByte,
                symbol.EndByte,
                symbol.StartLine,
                symbol.EndLine);
        }

        normalisedForHash  = extractedContent.NormaliseWhitespace();
        bodyHash           = normalisedForHash.ComputeContentHash();
        isTrivial          = TrivialSymbolFilter.IsTrivialSymbol(symbol, extractedContent);
        qualifiedClassName = ResolveQualifiedClassName(symbol, declaredNamespace, fileSymbols);

        // ── 3. Output / Return ──
        return new SymbolSnapshot
        {
            SymbolName       = symbol.Name,
            SymbolType       = symbol.Type,
            ClassName        = qualifiedClassName,
            Language         = symbol.Language ?? "unknown",
            FilePath         = symbol.FilePath.NormaliseRelativePath(),
            StartLine        = symbol.StartLine,
            EndLine          = symbol.EndLine,
            StartByte        = symbol.StartByte,
            EndByte          = symbol.EndByte,
            Signature        = symbol.Signature,
            DocComment       = symbol.DocComment,
            BodyHash         = bodyHash,
            IsTrivial        = isTrivial,
            ExtractedContent = extractedContent
        };
    }

    private static string? ResolveQualifiedClassName(
        AtlasSymbolDto symbol,
        string? declaredNamespace,
        IReadOnlyList<AtlasSymbolDto> fileSymbols)
    {
        // ── 1. Declarations ──
        string? enclosingClass;

        // ── 2. Intent-First Narrative ──
        enclosingClass = symbol.ClassName;

        if (string.IsNullOrWhiteSpace(enclosingClass) &&
            !string.Equals(symbol.Type, "Class", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(symbol.Type, "Interface", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(symbol.Type, "Struct", StringComparison.OrdinalIgnoreCase))
        {
            var parentType = fileSymbols.FirstOrDefault(c =>
                (string.Equals(c.Type, "Class", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Type, "Interface", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Type, "Struct", StringComparison.OrdinalIgnoreCase)) &&
                c.StartLine <= symbol.StartLine &&
                c.EndLine >= symbol.EndLine);

            enclosingClass = parentType?.Name;
        }

        if (string.IsNullOrWhiteSpace(declaredNamespace))
        {
            return enclosingClass;
        }

        if (string.IsNullOrWhiteSpace(enclosingClass))
        {
            return declaredNamespace;
        }

        if (enclosingClass.StartsWith(declaredNamespace + ".", StringComparison.OrdinalIgnoreCase))
        {
            return enclosingClass;
        }

        // ── 3. Output / Return ──
        return $"{declaredNamespace}.{enclosingClass}";
    }

    private static string? ExtractDeclaredNamespace(byte[] fileBytes)
    {
        // ── 1. Declarations ──
        string fullText;
        string[] lines;

        // ── 2. Preconditions & Guard Clauses ──
        if (fileBytes.Length == 0)
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        fullText = Encoding.UTF8.GetString(fileBytes);
        lines    = fullText.Split('\n');

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (!trimmed.StartsWith("namespace ", StringComparison.Ordinal))
            {
                continue;
            }

            var afterKeyword = trimmed["namespace ".Length..].Trim();
            var stopIdx      = afterKeyword.IndexOfAny([';', '{', ' ', '\t', '\r']);
            var candidate    = stopIdx >= 0 ? afterKeyword[..stopIdx].Trim() : afterKeyword;

            if (!string.IsNullOrWhiteSpace(candidate) &&
                candidate.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.'))
            {
                return candidate;
            }
        }

        // ── 4. Output / Return ──
        return null;
    }

    private static Dictionary<string, List<AtlasSymbolDto>> GroupMembersByClassName(
        IReadOnlyList<AtlasSymbolDto> fileSymbols)
    {
        // ── 1. Declarations ──
        Dictionary<string, List<AtlasSymbolDto>> map;

        // ── 2. Intent-First Narrative ──
        map = new Dictionary<string, List<AtlasSymbolDto>>(StringComparer.Ordinal);
        foreach (var sym in fileSymbols)
        {
            if (string.IsNullOrWhiteSpace(sym.ClassName) ||
                string.Equals(sym.Type, "Class", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!map.TryGetValue(sym.ClassName, out var list))
            {
                list = [];
                map[sym.ClassName] = list;
            }

            list.Add(sym);
        }

        // ── 3. Output / Return ──
        return map;
    }

    private static string ExtractClassHeaderLine(AtlasSymbolDto classSymbol, byte[] fileBytes)
    {
        // ── 1. Declarations ──
        string rawSlice;
        int braceIndex;

        // ── 2. Preconditions & Guard Clauses ──
        if (!string.IsNullOrWhiteSpace(classSymbol.Signature))
        {
            return $"class {classSymbol.Name} : {classSymbol.Signature}".NormaliseWhitespace();
        }

        // ── 3. Intent-First Narrative ──
        rawSlice = fileBytes.SliceUtf8OrLines(
            classSymbol.StartByte,
            classSymbol.EndByte,
            classSymbol.StartLine,
            classSymbol.EndLine);

        if (string.IsNullOrWhiteSpace(rawSlice))
        {
            return $"class {classSymbol.Name}";
        }

        braceIndex = rawSlice.IndexOf('{');
        if (braceIndex > 0)
        {
            return rawSlice[..braceIndex].NormaliseWhitespace();
        }

        // ── 4. Output / Return ──
        return rawSlice.Split('\n')[0].NormaliseWhitespace();
    }

    private static string ExtractMemberHeaderLine(AtlasSymbolDto member, string rawSlice)
    {
        // ── 1. Declarations ──
        int braceIndex;
        int arrowIndex;
        int cutIndex;

        // ── 2. Preconditions & Guard Clauses ──
        if (!string.IsNullOrWhiteSpace(member.Signature))
        {
            return $"{member.Name}({member.Signature})".NormaliseWhitespace();
        }

        if (string.IsNullOrWhiteSpace(rawSlice))
        {
            return member.Name;
        }

        // ── 3. Intent-First Narrative ──
        braceIndex = rawSlice.IndexOf('{');
        arrowIndex = rawSlice.IndexOf("=>", StringComparison.Ordinal);

        cutIndex = (braceIndex >= 0, arrowIndex >= 0) switch
        {
            (true, true)  => Math.Min(braceIndex, arrowIndex),
            (true, false) => braceIndex,
            (false, true) => arrowIndex,
            _             => -1
        };

        if (cutIndex > 0)
        {
            return rawSlice[..cutIndex].NormaliseWhitespace();
        }

        // ── 4. Output / Return ──
        return rawSlice.Split('\n')[0].NormaliseWhitespace();
    }

    private static bool IsConstructorMember(string className, AtlasSymbolDto member, string headerSig)
    {
        return string.Equals(member.Name, className, StringComparison.Ordinal) ||
               string.Equals(member.Name, "constructor", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(member.Type, "Constructor", StringComparison.OrdinalIgnoreCase) ||
               headerSig.StartsWith($"{className}(", StringComparison.Ordinal);
    }

    private static byte[] ReadFileBytesQuietly(string repoRoot, string relativeFilePath)
    {
        // ── 1. Declarations ──
        string fullPath;

        // ── 2. Preconditions & Guard Clauses ──
        fullPath = Path.IsPathRooted(relativeFilePath)
            ? relativeFilePath
            : Path.Combine(repoRoot, relativeFilePath);

        if (!File.Exists(fullPath))
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        try
        {
            return File.ReadAllBytes(fullPath);
        }
        catch
        {
            return [];
        }
    }
}
