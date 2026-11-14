namespace CodeAtlas.Traits.Core.Config;

/// <summary>
/// Normalises language aliases (e.g. <c>"c#"</c>, <c>"cs"</c>, <c>"ts"</c>, <c>"ps1"</c>)
/// to canonical CodeAtlas language identifiers and distinguishes language tags from capability tags.
/// </summary>
public static class LanguageNormalizer
{
    private static readonly HashSet<string> TagFilterLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "csharp",
        "typescript",
        "javascript",
        "powershell"
    };

    /// <summary>
    /// Normalises a language alias to its canonical CodeAtlas identifier.
    /// </summary>
    public static string? Normalise(string? rawLanguage)
    {
        // ── 1. Declarations ──
        string cleaned;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(rawLanguage))
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        cleaned = rawLanguage.Trim().TrimStart('.').ToLowerInvariant();

        // ── 4. Output / Return ──
        return cleaned switch
        {
            "c#" or "cs" or "csharp" or "dotnet" or ".net" => "csharp",
            "ts" or "tsx" or "typescript"                  => "typescript",
            "js" or "jsx" or "mjs" or "cjs" or "javascript"=> "javascript",
            "ps" or "ps1" or "psm1" or "pwsh" or "powershell" => "powershell",
            "htm" or "html"                                => "html",
            "css" or "scss"                                => "css",
            "json" or "jsonc"                              => "json",
            "yml" or "yaml"                                => "yaml",
            "sql" or "tsql" or "t-sql"                     => "sql",
            _                                              => cleaned
        };
    }

    /// <summary>
    /// Determines whether a query tag actually refers to a programming language filter
    /// (e.g. <c>"c#"</c>, <c>"csharp"</c>, <c>"typescript"</c>) rather than a capability tag.
    /// </summary>
    public static bool TryExtractLanguageTag(string? tag, out string? canonicalLanguage)
    {
        // ── 1. Declarations ──
        string? normalised;

        // ── 2. Preconditions & Guard Clauses ──
        canonicalLanguage = null;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        normalised = Normalise(tag);
        if (normalised is not null && TagFilterLanguages.Contains(normalised))
        {
            canonicalLanguage = normalised;
            return true;
        }

        // ── 4. Output / Return ──
        return false;
    }

    /// <summary>
    /// Separates any programming language filter tags (e.g. <c>"c#"</c>, <c>"typescript"</c>) from capability tags
    /// so the language filter can be forwarded directly to <c>CodeAtlas</c> CLI queries (<c>--lang</c>).
    /// </summary>
    public static (string? ResolvedLanguage, IReadOnlyList<string> CapabilityTags) SplitTagsAndLanguage(
        IReadOnlyList<string>? rawTags,
        string? explicitLanguage = null)
    {
        // ── 1. Declarations ──
        string? resolvedLanguage;
        List<string> capabilityTags;

        // ── 2. Intent-First Narrative ──
        resolvedLanguage = Normalise(explicitLanguage);
        capabilityTags   = [];

        if (rawTags is not null)
        {
            foreach (var raw in rawTags)
            {
                if (TryExtractLanguageTag(raw, out var langFromTag))
                {
                    resolvedLanguage ??= langFromTag;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    capabilityTags.Add(raw.Trim());
                }
            }
        }

        // ── 3. Output / Return ──
        return (resolvedLanguage, capabilityTags);
    }
}
