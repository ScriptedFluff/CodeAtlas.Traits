using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Core.Db;

public sealed record AiTraitJsonlRecord(
    [property: JsonPropertyName("bodyHash")] string BodyHash,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("isUtility")] bool IsUtility,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("category")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category = null,
    [property: JsonPropertyName("isActionableDebt")] bool IsActionableDebt = true,
    [property: JsonPropertyName("clusterName")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ClusterName = null,
    [property: JsonPropertyName("taggedAtUtc")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TaggedAtUtc = null);

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(AiTraitJsonlRecord))]
internal sealed partial class AiTraitJsonlContext : JsonSerializerContext { }

/// <summary>
/// Reads and writes git-trackable verified <c>'ai'</c> trait entries to <c>.codeatlas/traits.ai.jsonl</c>.
/// Entries are keyed strictly by <c>body_hash</c> and sorted deterministically so branches merge cleanly in Git.
/// </summary>
public static class AiTraitsJsonlStore
{
    public const string DefaultFileName = "traits.ai.jsonl";

    /// <summary>
    /// Resolves the companion <c>traits.ai.jsonl</c> file path located alongside <c>traits.db</c>.
    /// </summary>
    public static string ResolveJsonlPathFromDbPath(string traitsDbPath)
    {
        var dir = Path.GetDirectoryName(traitsDbPath) ?? Environment.CurrentDirectory;
        return Path.Combine(dir, DefaultFileName);
    }

    /// <summary>
    /// Exports all verified <c>'ai'</c> trait entries to <paramref name="jsonlFilePath"/> sorted by <c>BodyHash</c>.
    /// </summary>
    public static int WriteAllAiEntriesQuietly(string jsonlFilePath, IReadOnlyList<TraitEntry> aiEntries)
    {
        // ── 1. Declarations ──
        List<TraitEntry> sortedEntries;
        StringBuilder sb;
        string? directory;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(jsonlFilePath))
        {
            return 0;
        }

        // ── 3. Intent-First Narrative ──
        try
        {
            sortedEntries = aiEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.BodyHash) &&
                            string.Equals(e.TagSource, TraitEntry.SourceAiDetailed, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.BodyHash, StringComparer.Ordinal)
                .ToList();

            if (sortedEntries.Count == 0)
            {
                if (File.Exists(jsonlFilePath))
                {
                    File.Delete(jsonlFilePath);
                }

                return 0;
            }

            sb = new StringBuilder(sortedEntries.Count * 160);
            foreach (var entry in sortedEntries)
            {
                var sortedTags = entry.Tags
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToList();

                var record = new AiTraitJsonlRecord(
                    BodyHash:         entry.BodyHash,
                    Summary:          entry.Summary ?? string.Empty,
                    IsUtility:        entry.IsUtility,
                    Tags:             sortedTags,
                    Category:         entry.Category,
                    IsActionableDebt: entry.IsActionableDebt,
                    ClusterName:      entry.ClusterName,
                    TaggedAtUtc:      entry.TaggedAtUtc);

                sb.AppendLine(JsonSerializer.Serialize(record, AiTraitJsonlContext.Default.AiTraitJsonlRecord));
            }

            directory = Path.GetDirectoryName(jsonlFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(jsonlFilePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return sortedEntries.Count;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Reads all verified <c>'ai'</c> trait entries from <paramref name="jsonlFilePath"/> if the file exists.
    /// </summary>
    public static IReadOnlyList<TraitEntry> ReadAllAiEntriesQuietly(string jsonlFilePath)
    {
        // ── 1. Declarations ──
        string[] lines;
        List<TraitEntry> entries;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(jsonlFilePath) || !File.Exists(jsonlFilePath))
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        entries = [];
        try
        {
            lines = File.ReadAllLines(jsonlFilePath, Encoding.UTF8);
            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                var record = JsonSerializer.Deserialize(trimmed, AiTraitJsonlContext.Default.AiTraitJsonlRecord);
                if (record is null || string.IsNullOrWhiteSpace(record.BodyHash))
                {
                    continue;
                }

                entries.Add(new TraitEntry
                {
                    BodyHash         = record.BodyHash.Trim(),
                    Summary          = record.Summary ?? string.Empty,
                    IsUtility        = record.IsUtility,
                    Tags             = record.Tags ?? [],
                    TagSource        = TraitEntry.SourceAiDetailed,
                    Category         = record.Category,
                    IsActionableDebt = record.IsActionableDebt,
                    ClusterName      = record.ClusterName,
                    TaggedAtUtc      = record.TaggedAtUtc
                });
            }
        }
        catch
        {
            // Ignore malformed JSONL files so database startup is never blocked.
        }

        // ── 4. Output / Return ──
        return entries;
    }
}
