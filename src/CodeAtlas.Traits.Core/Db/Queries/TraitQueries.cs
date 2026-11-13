using CodeAtlas.Traits.Core.Config;
using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Core.Onnx;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;
using Microsoft.Data.Sqlite;

namespace CodeAtlas.Traits.Core.Db.Queries;

/// <summary>
/// Provides high-speed SQLite queries for storing symbol snapshots, upserting content-addressable traits,
/// searching by capability/technology/language tags, and detecting duplicate method bodies.
/// </summary>
public sealed class TraitQueries(SqliteConnection connection)
{
    /// <summary>
    /// Replaces the current symbol snapshot table (or only rows for <paramref name="scopedLanguage"/> when specified)
    /// while leaving <c>trait_entries</c> and <c>trait_tags</c> intact (since traits are keyed by <c>body_hash</c>).
    /// </summary>
    public void ReplaceSymbolSnapshots(IReadOnlyList<SymbolSnapshot> snapshots, string? scopedLanguage = null)
    {
        // ── 1. Declarations ──
        SqliteTransaction tx;
        string? normalisedLanguage;

        // ── 2. Intent-First Narrative ──
        normalisedLanguage = LanguageNormalizer.Normalise(scopedLanguage);
        using (tx = connection.BeginTransaction())
        {
            ClearSnapshotsInTransaction(tx, normalisedLanguage);
            InsertSnapshotsInTransaction(tx, snapshots);
            tx.Commit();
        }
    }

    /// <summary>
    /// Upserts a batch of AI-generated capability traits and tags keyed by <c>body_hash</c>.
    /// </summary>
    public int SaveTraitEntries(IReadOnlyList<TraitEntry> entries)
    {
        // ── 1. Declarations ──
        int savedCount;
        string nowUtc;

        // ── 2. Preconditions & Guard Clauses ──
        if (entries.Count == 0)
        {
            return 0;
        }

        // ── 3. Intent-First Narrative ──
        savedCount = 0;
        nowUtc     = DateTime.UtcNow.ToString("O");

        using var tx = connection.BeginTransaction();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.BodyHash))
            {
                continue;
            }

            UpsertSingleTraitEntryInTransaction(tx, entry, nowUtc);
            savedCount++;
        }

        tx.Commit();

        // ── 4. Output / Return ──
        return savedCount;
    }

    /// <summary>
    /// Searches symbols matching the requested capability/technology tags and optional language/utility filters.
    /// Automatically extracts language names passed inside <paramref name="rawTags"/> (e.g. <c>"c#"</c> -> <c>language = "csharp"</c>)
    /// and expands capability tags into concept synonym clusters via <see cref="ThesaurusEngine"/>.
    /// </summary>
    public IReadOnlyList<TraitSearchMatch> SearchByTraits(
        IReadOnlyList<string>? rawTags,
        string? explicitLanguage = null,
        string? symbolType = null,
        bool? onlyUtilities = null,
        bool matchAllTags = true,
        int limit = 50,
        bool useThesaurus = true,
        ThesaurusEngine? customThesaurus = null)
    {
        // ── 1. Declarations ──
        ThesaurusEngine? activeThesaurus;
        ParsedSearchFilter filter;
        List<TraitSearchMatch> matches;
        int effectiveLimit;

        // ── 2. Preconditions & Guard Clauses ──
        effectiveLimit  = Math.Clamp(limit, 1, 500);
        activeThesaurus = useThesaurus ? (customThesaurus ?? ThesaurusEngine.LoadForWorkspace()) : null;
        filter          = ParseSearchFilter(rawTags, explicitLanguage, symbolType, onlyUtilities, matchAllTags, activeThesaurus);

        // ── 3. Intent-First Narrative ──
        matches = ExecuteTraitSearchQuery(filter, effectiveLimit);

        // ── 4. Output / Return ──
        return matches;
    }

    /// <summary>
    /// Returns a batch of untagged non-trivial symbols (or also baseline <c>'auto-cpu'</c> tagged symbols when
    /// <paramref name="includeAutoTagged"/> is <c>true</c>) ready for detailed AI analysis by Gemini, Copilot, or Claude.
    /// </summary>
    public IReadOnlyList<UntaggedSymbolCandidate> GetUntaggedBatch(
        string? language = null,
        int batchSize = 25,
        bool includeAutoTagged = false)
    {
        // ── 1. Declarations ──
        string? normalisedLanguage;
        int effectiveBatchSize;
        List<UntaggedSymbolCandidate> rawCandidates;
        List<UntaggedSymbolCandidate> enrichedCandidates;

        // ── 2. Preconditions & Guard Clauses ──
        normalisedLanguage = LanguageNormalizer.Normalise(language);
        effectiveBatchSize = Math.Clamp(batchSize, 1, 100);
        rawCandidates      = [];
        enrichedCandidates = [];

        // ── 3. Intent-First Narrative ──
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.body_hash, s.symbol_name, s.symbol_type, s.class_name, s.language,
                       s.file_path, s.start_line, s.end_line, s.signature, s.doc_comment,
                       s.extracted_content, e.tag_source, e.summary
                FROM symbol_snapshots s
                LEFT JOIN trait_entries e ON e.body_hash = s.body_hash
                WHERE s.is_trivial = 0
                  AND (e.body_hash IS NULL OR (@includeAuto = 1 AND e.tag_source = 'auto-cpu'))
                  AND (@lang IS NULL OR s.language = @lang COLLATE NOCASE)
                GROUP BY s.body_hash
                ORDER BY CASE WHEN e.body_hash IS NULL THEN 0 ELSE 1 END ASC, s.file_path, s.start_line
                LIMIT @limit;
                """;

            cmd.Parameters.AddWithValue("@includeAuto", includeAutoTagged ? 1 : 0);
            cmd.Parameters.AddWithValue("@lang", (object?)normalisedLanguage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@limit", effectiveBatchSize);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rawCandidates.Add(new UntaggedSymbolCandidate(
                    BodyHash:         reader.GetString(0),
                    SymbolName:       reader.GetString(1),
                    SymbolType:       reader.GetString(2),
                    ClassName:        reader.IsDBNull(3) ? null : reader.GetString(3),
                    Language:         reader.GetString(4),
                    FilePath:         reader.GetString(5),
                    StartLine:        reader.GetInt32(6),
                    EndLine:          reader.GetInt32(7),
                    Signature:        reader.IsDBNull(8) ? null : reader.GetString(8),
                    DocComment:       reader.IsDBNull(9) ? null : reader.GetString(9),
                    ContentToAnalyse: reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    CurrentTagSource: reader.IsDBNull(11) ? null : reader.GetString(11),
                    CurrentTags:      null,
                    CurrentSummary:   reader.IsDBNull(12) ? null : reader.GetString(12)));
            }
        }

        foreach (var cand in rawCandidates)
        {
            if (cand.CurrentTagSource is not null)
            {
                enrichedCandidates.Add(cand with { CurrentTags = GetTagsForBodyHash(cand.BodyHash) });
            }
            else
            {
                enrichedCandidates.Add(cand);
            }
        }

        // ── 4. Output / Return ──
        return enrichedCandidates;
    }

    /// <summary>
    /// Resolves a target symbol (<c>MyNamespace.MyClass:MyMethod</c>, <c>MyClass:MyMethod</c>, or <c>MyClass.cs:L35</c>)
    /// and returns its full source, signature, <c>body_hash</c>, and existing tags so Gemini or GitHub Copilot can
    /// analyse it and insert detailed traits.
    /// </summary>
    public UntaggedSymbolCandidate? GetSymbolForAnalysis(string rawTargetReference)
    {
        // ── 1. Declarations ──
        SymbolSnapshot? snapshot;
        string? currentTagSource;
        string? currentSummary;
        IReadOnlyList<string>? currentTags;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(rawTargetReference))
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        snapshot = ResolveSnapshotFromTargetReference(rawTargetReference);
        if (snapshot is null)
        {
            return null;
        }

        (currentTagSource, currentSummary) = GetTraitEntryMetaForBodyHash(snapshot.BodyHash);
        currentTags = currentTagSource is not null ? GetTagsForBodyHash(snapshot.BodyHash) : null;

        // ── 4. Output / Return ──
        return new UntaggedSymbolCandidate(
            BodyHash:         snapshot.BodyHash,
            SymbolName:       snapshot.SymbolName,
            SymbolType:       snapshot.SymbolType,
            ClassName:        snapshot.ClassName,
            Language:         snapshot.Language,
            FilePath:         snapshot.FilePath,
            StartLine:        snapshot.StartLine,
            EndLine:          snapshot.EndLine,
            Signature:        snapshot.Signature,
            DocComment:       snapshot.DocComment,
            ContentToAnalyse: snapshot.ExtractedContent ?? string.Empty,
            CurrentTagSource: currentTagSource,
            CurrentTags:      currentTags,
            CurrentSummary:   currentSummary);
    }

    /// <summary>
    /// Resolves a target symbol (<c>MyNamespace.MyClass:MyMethod</c> or <c>MyClass.cs:L35</c>) and directly
    /// saves detailed AI traits for its <c>body_hash</c>.
    /// </summary>
    public TraitSearchMatch? TagSymbolByTarget(
        string rawTargetReference,
        IReadOnlyList<string> tags,
        string summary,
        bool isUtility,
        string tagSource = TraitEntry.SourceAiDetailed,
        string? category = null,
        bool isActionableDebt = true,
        string? clusterName = null)
    {
        // ── 1. Declarations ──
        SymbolSnapshot? snapshot;
        CpuSemanticEngine engine;
        ThesaurusEngine thesaurus;
        string combinedText;
        byte[] embeddingBlob;
        TraitEntry entry;

        // ── 2. Preconditions & Guard Clauses ──
        snapshot = ResolveSnapshotFromTargetReference(rawTargetReference);
        if (snapshot is null)
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        thesaurus     = ThesaurusEngine.LoadForWorkspace();
        engine        = CpuSemanticEngine.LoadForWorkspace();
        combinedText  = $"{snapshot.SymbolName} {snapshot.Signature} {summary} {string.Join(' ', tags)} {snapshot.ExtractedContent}";
        embeddingBlob = CpuSemanticEngine.SerialiseEmbedding(engine.ComputeEmbedding(combinedText, thesaurus));

        entry = new TraitEntry
        {
            BodyHash         = snapshot.BodyHash,
            Summary          = summary?.Trim() ?? string.Empty,
            IsUtility        = isUtility,
            Tags             = tags ?? [],
            TagSource        = string.IsNullOrWhiteSpace(tagSource) ? TraitEntry.SourceAiDetailed : tagSource,
            EmbeddingBlob    = embeddingBlob,
            Category         = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            IsActionableDebt = isActionableDebt,
            ClusterName      = string.IsNullOrWhiteSpace(clusterName) ? null : clusterName.Trim()
        };

        SaveTraitEntries([entry]);

        // ── 4. Output / Return ──
        return GetSymbolTraitMatchById(snapshot.Id);
    }

    /// <summary>
    /// Finds non-trivial symbols across the codebase that share an identical <c>body_hash</c>.
    /// By default, excludes non-executable contract signatures (<c>has_executable_body = 0</c>),
    /// suppresses standalone 1-statement / 1-property noise unless part of a multi-symbol file-pair cluster,
    /// and respects <c>.codeatlas/settings.json</c> ignore patterns.
    /// </summary>
    public IReadOnlyList<DuplicateBodyGroup> FindExactBodyDuplicates(
        string? language = null,
        int limit = 50,
        bool includeContracts = false,
        int minStatements = 2,
        bool onlyActionableDebt = true,
        string? workspaceRoot = null)
    {
        // ── 1. Declarations ──
        List<DuplicateBodyGroup> rawGroups;
        IReadOnlyList<SubsystemDuplicateCluster> clusters;
        HashSet<string> clusteredHashes;
        List<DuplicateBodyGroup> filteredGroups;

        // ── 2. Preconditions & Guard Clauses ──
        rawGroups = QueryRawDuplicateBodyGroups(language, Math.Clamp(limit * 3, 50, 500), includeContracts, onlyActionableDebt, workspaceRoot);
        if (rawGroups.Count == 0)
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        clusters        = BuildSubsystemClustersFromGroups(rawGroups);
        clusteredHashes = clusters.SelectMany(c => c.BodyHashes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        filteredGroups  = new List<DuplicateBodyGroup>(rawGroups.Count);

        foreach (var group in rawGroups)
        {
            var maxLineSpan = group.Locations.Count > 0
                ? group.Locations.Max(l => Math.Max(1, l.EndLine - l.StartLine + 1))
                : 1;

            // Suppress standalone 1-statement / 1-property short boilerplate (<= 5 lines) unless it belongs to a multi-symbol subsystem cluster.
            if (group.HasExecutableBody &&
                minStatements > 1 &&
                group.StatementCount < minStatements &&
                maxLineSpan <= 5 &&
                !clusteredHashes.Contains(group.BodyHash))
            {
                continue;
            }

            filteredGroups.Add(group);
            if (filteredGroups.Count >= Math.Clamp(limit, 1, 200))
            {
                break;
            }
        }

        // ── 4. Output / Return ──
        return filteredGroups;
    }

    /// <summary>
    /// Detects file-pair / subsystem duplication clusters where multiple sibling symbols (<c>&gt;= 2</c>)
    /// in one file share identical <c>body_hash</c> values with sibling symbols in another file
    /// (e.g. copied GraphQL DTO/stub hierarchies or duplicated utility files).
    /// </summary>
    public IReadOnlyList<SubsystemDuplicateCluster> DetectSubsystemDuplicateClusters(
        string? language = null,
        bool includeContracts = false,
        bool onlyActionableDebt = true,
        string? workspaceRoot = null,
        int limit = 50)
    {
        // ── 1. Declarations ──
        List<DuplicateBodyGroup> rawGroups;
        IReadOnlyList<SubsystemDuplicateCluster> clusters;

        // ── 2. Intent-First Narrative ──
        rawGroups = QueryRawDuplicateBodyGroups(language, 500, includeContracts, onlyActionableDebt, workspaceRoot);
        clusters  = BuildSubsystemClustersFromGroups(rawGroups);

        // ── 3. Output / Return ──
        return clusters.Take(Math.Clamp(limit, 1, 100)).ToList();
    }

    /// <summary>
    /// Automatically generates baseline <c>'auto-cpu'</c> capability/technology tags and CPU ONNX embeddings
    /// across all CPU cores for any untagged non-trivial symbols in <c>traits.db</c>.
    /// </summary>
    public int AutoTagAllUntaggedSymbols(
        string? workspaceRoot = null,
        string? language = null,
        Action<int, int>? onProgress = null)
    {
        // ── 1. Declarations ──
        string? normalisedLanguage;
        List<SymbolSnapshot> untaggedSnapshots;
        ThesaurusEngine thesaurus;
        CpuSemanticEngine semanticEngine;
        HashSet<string> existingVocab;
        TraitEntry[] generatedEntries;
        int totalToTag;
        int completedCount;

        // ── 2. Preconditions & Guard Clauses ──
        normalisedLanguage = LanguageNormalizer.Normalise(language);
        untaggedSnapshots  = GetAllUntaggedNonTrivialSnapshots(normalisedLanguage);
        if (untaggedSnapshots.Count == 0)
        {
            return 0;
        }

        // ── 3. Intent-First Narrative ──
        thesaurus        = ThesaurusEngine.LoadForWorkspace(workspaceRoot);
        semanticEngine   = CpuSemanticEngine.LoadForWorkspace(workspaceRoot);
        existingVocab    = GetAllDistinctTagsInDatabase();
        totalToTag       = untaggedSnapshots.Count;
        generatedEntries = new TraitEntry[totalToTag];
        completedCount   = 0;

        Parallel.For(0, totalToTag, index =>
        {
            var snap = untaggedSnapshots[index];
            generatedEntries[index] = AutoTraitTagger.GenerateTraitsForSnapshot(
                snap,
                thesaurus,
                existingVocab,
                semanticEngine);

            var current = Interlocked.Increment(ref completedCount);
            if (onProgress is not null && (current % 250 == 0 || current == totalToTag))
            {
                onProgress(current, totalToTag);
            }
        });

        // ── 4. Output / Return ──
        return SaveTraitEntries(generatedEntries);
    }

    /// <summary>
    /// Returns fast snapshot counts from <c>symbol_snapshots</c> so <see cref="ScanOrchestrator"/> can
    /// detect whether <c>traits.db</c> has already been populated on disk across CLI invocations.
    /// </summary>
    public (int TotalSnapshots, int TrackableSnapshots, int UntaggedSnapshots) GetQuickSnapshotCounts(string? language = null)
    {
        // ── 1. Declarations ──
        string? normalisedLanguage;
        int totalSnapshots;
        int trackableSnapshots;
        int untaggedSnapshots;

        // ── 2. Preconditions & Guard Clauses ──
        normalisedLanguage = LanguageNormalizer.Normalise(language);
        totalSnapshots     = 0;
        trackableSnapshots = 0;
        untaggedSnapshots  = 0;

        // ── 3. Intent-First Narrative ──
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*) AS total_cnt,
                SUM(CASE WHEN s.is_trivial = 0 THEN 1 ELSE 0 END) AS trackable_cnt,
                SUM(CASE WHEN s.is_trivial = 0 AND e.body_hash IS NULL THEN 1 ELSE 0 END) AS untagged_cnt
            FROM symbol_snapshots s
            LEFT JOIN trait_entries e ON e.body_hash = s.body_hash
            WHERE (@lang IS NULL OR s.language = @lang COLLATE NOCASE);
            """;
        cmd.Parameters.AddWithValue("@lang", (object?)normalisedLanguage ?? DBNull.Value);

        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                totalSnapshots     = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                trackableSnapshots = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                untaggedSnapshots  = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            }
        }

        // ── 4. Output / Return ──
        return (totalSnapshots, trackableSnapshots, untaggedSnapshots);
    }

    /// <summary>
    /// Returns the most frequently used capability and technology tags across non-trivial symbols in the repository.
    /// </summary>
    public IReadOnlyList<TagUsageCount> GetTopTags(
        string? language = null,
        string? symbolType = null,
        bool? onlyUtilities = null,
        int limit = 50)
    {
        // ── 1. Declarations ──
        string? normalisedLanguage;
        string? normalisedType;
        int? utilityFlag;
        int effectiveLimit;
        List<TagUsageCount> results;

        // ── 2. Preconditions & Guard Clauses ──
        normalisedLanguage = LanguageNormalizer.Normalise(language);
        normalisedType     = string.IsNullOrWhiteSpace(symbolType) ? null : symbolType.Trim();
        utilityFlag        = onlyUtilities.HasValue ? (onlyUtilities.Value ? 1 : 0) : null;
        effectiveLimit     = Math.Clamp(limit, 1, 500);
        results            = [];

        // ── 3. Intent-First Narrative ──
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT t.tag,
                       COUNT(DISTINCT s.id)        AS symbol_cnt,
                       COUNT(DISTINCT t.body_hash) AS body_cnt
                FROM trait_tags t
                JOIN trait_entries e    ON e.body_hash = t.body_hash
                JOIN symbol_snapshots s ON s.body_hash = t.body_hash
                WHERE s.is_trivial = 0
                  AND (@lang IS NULL OR s.language = @lang COLLATE NOCASE)
                  AND (@type IS NULL OR s.symbol_type = @type COLLATE NOCASE)
                  AND (@util IS NULL OR e.is_utility = @util)
                GROUP BY t.tag
                ORDER BY symbol_cnt DESC, body_cnt DESC, t.tag ASC
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@lang", (object?)normalisedLanguage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@type", (object?)normalisedType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@util", (object?)utilityFlag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@limit", effectiveLimit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new TagUsageCount(
                    Tag:             reader.GetString(0),
                    SymbolCount:     reader.GetInt32(1),
                    UniqueBodyCount: reader.GetInt32(2)));
            }
        }

        // ── 4. Output / Return ──
        return results;
    }

    /// <summary>
    /// Returns the most frequently recurring multi-trait combinations across non-trivial symbols
    /// (requiring at least <paramref name="minTags"/> traits per symbol) so multi-trait capability duplication can be spotted.
    /// </summary>
    public IReadOnlyList<TagCombinationCount> GetTopTagCombinations(
        string? language = null,
        string? symbolType = null,
        bool? onlyUtilities = null,
        int minTags = 3,
        int limit = 50)
    {
        // ── 1. Declarations ──
        string? normalisedLanguage;
        string? normalisedType;
        int? utilityFlag;
        int effectiveMinTags;
        int effectiveLimit;
        List<TagCombinationCount> results;

        // ── 2. Preconditions & Guard Clauses ──
        normalisedLanguage = LanguageNormalizer.Normalise(language);
        normalisedType     = string.IsNullOrWhiteSpace(symbolType) ? null : symbolType.Trim();
        utilityFlag        = onlyUtilities.HasValue ? (onlyUtilities.Value ? 1 : 0) : null;
        effectiveMinTags   = Math.Clamp(minTags, 2, 12);
        effectiveLimit     = Math.Clamp(limit, 1, 500);
        results            = [];

        // ── 3. Intent-First Narrative ──
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                WITH body_tag_sets AS (
                    SELECT t.body_hash,
                           COUNT(*) AS tag_cnt,
                           GROUP_CONCAT(t.tag, ', ') AS tag_sig
                    FROM (
                        SELECT body_hash, LOWER(tag) AS tag
                        FROM trait_tags
                        GROUP BY body_hash, LOWER(tag)
                        ORDER BY body_hash, LOWER(tag)
                    ) t
                    GROUP BY t.body_hash
                    HAVING COUNT(*) >= @minTags
                )
                SELECT b.tag_sig,
                       b.tag_cnt,
                       COUNT(DISTINCT s.id)        AS symbol_cnt,
                       COUNT(DISTINCT s.body_hash) AS body_cnt
                FROM body_tag_sets b
                JOIN trait_entries e    ON e.body_hash = b.body_hash
                JOIN symbol_snapshots s ON s.body_hash = b.body_hash
                WHERE s.is_trivial = 0
                  AND (@lang IS NULL OR s.language = @lang COLLATE NOCASE)
                  AND (@type IS NULL OR s.symbol_type = @type COLLATE NOCASE)
                  AND (@util IS NULL OR e.is_utility = @util)
                GROUP BY b.tag_sig, b.tag_cnt
                HAVING COUNT(DISTINCT s.id) > 1
                ORDER BY body_cnt DESC, symbol_cnt DESC, b.tag_cnt DESC, b.tag_sig ASC
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@minTags", effectiveMinTags);
            cmd.Parameters.AddWithValue("@lang", (object?)normalisedLanguage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@type", (object?)normalisedType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@util", (object?)utilityFlag ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@limit", effectiveLimit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var sig  = reader.GetString(0);
                var tags = sig.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                results.Add(new TagCombinationCount(
                    TagSignature:    sig,
                    Tags:            tags,
                    TagCount:        reader.GetInt32(1),
                    SymbolCount:     reader.GetInt32(2),
                    UniqueBodyCount: reader.GetInt32(3)));
            }
        }

        // ── 4. Output / Return ──
        return results;
    }

    /// <summary>
    /// Computes the current traits coverage statistics for the repository.
    /// </summary>
    public TraitsCoverageSummary GetCoverageSummary(bool isScanInProgress, string? language = null)
    {
        // ── 1. Declarations ──
        string? normalisedLanguage;
        int totalTrackable;
        int taggedCount;
        int aiTaggedCount;
        int autoCpuTaggedCount;
        int trivialCount;
        int untaggedCount;
        string notice;

        // ── 2. Preconditions & Guard Clauses ──
        normalisedLanguage = LanguageNormalizer.Normalise(language);

        // ── 3. Intent-First Narrative ──
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                SUM(CASE WHEN s.is_trivial = 0 THEN 1 ELSE 0 END) AS trackable,
                SUM(CASE WHEN s.is_trivial = 0 AND e.body_hash IS NOT NULL THEN 1 ELSE 0 END) AS tagged,
                SUM(CASE WHEN s.is_trivial = 0 AND e.tag_source = 'ai' THEN 1 ELSE 0 END) AS ai_tagged,
                SUM(CASE WHEN s.is_trivial = 0 AND e.tag_source = 'auto-cpu' THEN 1 ELSE 0 END) AS auto_tagged,
                SUM(CASE WHEN s.is_trivial = 1 THEN 1 ELSE 0 END) AS trivial
            FROM symbol_snapshots s
            LEFT JOIN trait_entries e ON e.body_hash = s.body_hash
            WHERE (@lang IS NULL OR s.language = @lang COLLATE NOCASE);
            """;
        cmd.Parameters.AddWithValue("@lang", (object?)normalisedLanguage ?? DBNull.Value);

        totalTrackable     = 0;
        taggedCount        = 0;
        aiTaggedCount      = 0;
        autoCpuTaggedCount = 0;
        trivialCount       = 0;

        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                totalTrackable     = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                taggedCount        = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                aiTaggedCount      = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                autoCpuTaggedCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                trivialCount       = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            }
        }

        untaggedCount = Math.Max(0, totalTrackable - taggedCount);
        notice        = BuildCoverageNotice(
            totalTrackable,
            taggedCount,
            aiTaggedCount,
            autoCpuTaggedCount,
            untaggedCount,
            isScanInProgress);

        // ── 4. Output / Return ──
        return new TraitsCoverageSummary(
            TotalTrackableSymbols: totalTrackable,
            TaggedSymbols:         taggedCount,
            UntaggedSymbols:       untaggedCount,
            TrivialSkippedSymbols: trivialCount,
            IsScanInProgress:      isScanInProgress,
            Notice:                notice);
    }

    /// <summary>
    /// Finds pairs of non-trivial symbols that have different <c>body_hash</c> values
    /// yet share at least <paramref name="minSharedTags"/> overlapping capability/technology tags.
    /// </summary>
    public IReadOnlyList<SemanticSimilarityPair> FindSemanticSimilarPairs(
        string? language = null,
        int minSharedTags = 3,
        int limit = 50)
    {
        // ── 1. Declarations ──
        string? normalisedLanguage;
        int effectiveMinTags;
        int effectiveLimit;
        List<(long IdA, long IdB, int SharedCount)> candidatePairs;
        List<SemanticSimilarityPair> resultPairs;

        // ── 2. Preconditions & Guard Clauses ──
        normalisedLanguage = LanguageNormalizer.Normalise(language);
        effectiveMinTags   = Math.Max(2, minSharedTags);
        effectiveLimit     = Math.Clamp(limit, 1, 200);
        candidatePairs     = [];
        resultPairs        = [];

        // ── 3. Intent-First Narrative ──
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s1.id, s2.id, COUNT(DISTINCT t1.tag) AS shared_count
                FROM symbol_snapshots s1
                JOIN trait_tags t1       ON t1.body_hash = s1.body_hash
                JOIN trait_tags t2       ON t2.tag = t1.tag AND t2.body_hash != t1.body_hash
                JOIN symbol_snapshots s2 ON s2.body_hash = t2.body_hash AND s2.id > s1.id
                WHERE s1.is_trivial = 0
                  AND s2.is_trivial = 0
                  AND s1.symbol_type = s2.symbol_type
                  AND (@lang IS NULL OR (s1.language = @lang COLLATE NOCASE AND s2.language = @lang COLLATE NOCASE))
                GROUP BY s1.id, s2.id
                HAVING COUNT(DISTINCT t1.tag) >= @minTags
                ORDER BY shared_count DESC, s1.file_path, s2.file_path
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@lang", (object?)normalisedLanguage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@minTags", effectiveMinTags);
            cmd.Parameters.AddWithValue("@limit", effectiveLimit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                candidatePairs.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2)));
            }
        }

        foreach (var (idA, idB, _) in candidatePairs)
        {
            var matchA = GetSymbolTraitMatchById(idA);
            var matchB = GetSymbolTraitMatchById(idB);
            if (matchA is null || matchB is null)
            {
                continue;
            }

            var sharedTags = matchA.Tags
                .Intersect(matchB.Tags, StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            resultPairs.Add(new SemanticSimilarityPair(
                SharedTagCount: sharedTags.Count,
                SharedTags:     sharedTags,
                PrimarySymbol:  matchA,
                SimilarSymbol:  matchB));
        }

        // ── 4. Output / Return ──
        return resultPairs;
    }

    /// <summary>
    /// Audits symbols within a set of modified files (e.g. from <c>git diff</c>) to identify:
    /// 1. Untagged symbols in the diff that need tagging.
    /// 2. Tagged symbols in the diff that duplicate or overlap with existing codebase capabilities.
    /// </summary>
    public DiffTraitAuditReport AuditFilesTraits(
        IReadOnlyList<string> relativeFilePaths,
        ThesaurusEngine? thesaurus = null)
    {
        // ── 1. Declarations ──
        HashSet<string> normalisedFiles;
        List<UntaggedSymbolCandidate> untaggedInDiff;
        List<TraitSearchMatch> taggedInDiff;
        List<DiffSymbolAuditEntry> overlappingEntries;

        // ── 2. Preconditions & Guard Clauses ──
        if (relativeFilePaths is null || relativeFilePaths.Count == 0)
        {
            return new DiffTraitAuditReport([], [], []);
        }

        // ── 3. Intent-First Narrative ──
        normalisedFiles = relativeFilePaths
            .Select(p => p.NormaliseRelativePath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        untaggedInDiff     = GetUntaggedSymbolsInFiles(normalisedFiles);
        taggedInDiff       = GetTaggedSymbolsInFiles(normalisedFiles);
        overlappingEntries = [];

        foreach (var diffSym in taggedInDiff)
        {
            if (!diffSym.IsActionableDebt)
            {
                continue;
            }

            var exactDups = GetLocationsForBodyHash(diffSym.BodyHash)
                .Where(loc => !string.Equals(loc.FilePath, diffSym.FilePath, StringComparison.OrdinalIgnoreCase) ||
                              loc.StartLine != diffSym.StartLine)
                .ToList();

            var similarMatches = FindOverlappingSymbolsForDiffEntry(diffSym, thesaurus);
            if (exactDups.Count > 0 || similarMatches.Count > 0)
            {
                overlappingEntries.Add(new DiffSymbolAuditEntry(
                    DiffSymbol:                 diffSym,
                    ExactBodyDuplicates:        exactDups,
                    OverlappingExistingSymbols: similarMatches));
            }
        }

        // ── 4. Output / Return ──
        return new DiffTraitAuditReport(
            ModifiedFiles:            normalisedFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
            UntaggedSymbolsInDiff:    untaggedInDiff,
            OverlappingSymbolsInDiff: overlappingEntries);
    }

    /// <summary>
    /// Resolves a target reference in either symbol format (<c>MyNamespace.MyClass:MyMethod</c>,
    /// <c>MyClass:MyMethod</c>, <c>MyNamespace.MyClass.MyMethod</c>) or location format (<c>MyClass.cs:L35</c>),
    /// auto-tags any untagged symbols if needed, and ranks similar symbols across the repository.
    /// </summary>
    public SimilarToLocationResult FindSimilarToTarget(
        string rawTargetReference,
        ThesaurusEngine? customThesaurus = null,
        int limit = 25)
    {
        // ── 1. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(rawTargetReference))
        {
            return new SimilarToLocationResult(
                TargetFound:    false,
                WasAutoTagged:  false,
                Message:        "Target reference cannot be empty. Expected 'MyNamespace.MyClass:MyMethod' or 'MyClass.cs:L35'.",
                TargetSymbol:   null,
                SimilarMatches: []);
        }

        // ── 2. Intent-First Narrative ──
        if (rawTargetReference.TryParseFileLineReference(out var filePathPattern, out var lineNumber))
        {
            return FindSimilarToLocation(filePathPattern, lineNumber, customThesaurus, limit);
        }

        if (rawTargetReference.TryParseSymbolTargetReference(out var containerPattern, out var symbolName))
        {
            return FindSimilarToSymbol(containerPattern, symbolName, customThesaurus, limit);
        }

        // ── 3. Output / Return ──
        return new SimilarToLocationResult(
            TargetFound:    false,
            WasAutoTagged:  false,
            Message:        $"Invalid target reference '{rawTargetReference}'. Expected 'MyNamespace.MyClass:MyMethod' or 'MyClass.cs:L35'.",
            TargetSymbol:   null,
            SimilarMatches: []);
    }

    /// <summary>
    /// Locates the symbol matching <paramref name="containerPattern"/> and <paramref name="symbolName"/>
    /// (e.g. <c>MyNamespace.MyClass:MyMethod</c> or <c>MyClass:MyMethod</c>), auto-tags any untagged symbols
    /// if needed, and ranks similar symbols across the repository by exact body hash, capability/synonym tag overlap,
    /// and line-count size similarity.
    /// </summary>
    public SimilarToLocationResult FindSimilarToSymbol(
        string? containerPattern,
        string symbolName,
        ThesaurusEngine? customThesaurus = null,
        int limit = 25)
    {
        // ── 1. Declarations ──
        ThesaurusEngine thesaurus;
        SymbolSnapshot? targetSnapshot;
        string targetDisplay;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(symbolName))
        {
            return new SimilarToLocationResult(
                TargetFound:    false,
                WasAutoTagged:  false,
                Message:        "Symbol name cannot be empty.",
                TargetSymbol:   null,
                SimilarMatches: []);
        }

        // ── 3. Intent-First Narrative ──
        thesaurus      = customThesaurus ?? ThesaurusEngine.LoadForWorkspace();
        targetSnapshot = FindSnapshotBySymbolTarget(containerPattern, symbolName);
        targetDisplay  = string.IsNullOrWhiteSpace(containerPattern)
            ? symbolName
            : $"{containerPattern}:{symbolName}";

        if (targetSnapshot is null)
        {
            return new SimilarToLocationResult(
                TargetFound:    false,
                WasAutoTagged:  false,
                Message:        $"No symbol found matching '{targetDisplay}'.",
                TargetSymbol:   null,
                SimilarMatches: []);
        }

        // ── 4. Output / Return ──
        return CompareResolvedTargetSnapshot(targetSnapshot, thesaurus, limit);
    }

    /// <summary>
    /// Locates the innermost symbol at <paramref name="filePathPattern"/> and <paramref name="lineNumber"/>
    /// (e.g. <c>MyClass.cs:L35</c>), auto-tags any untagged symbols if needed, and ranks similar symbols
    /// across the repository by exact body hash, capability/synonym tag overlap, and line-count size similarity.
    /// </summary>
    public SimilarToLocationResult FindSimilarToLocation(
        string filePathPattern,
        int lineNumber,
        ThesaurusEngine? customThesaurus = null,
        int limit = 25)
    {
        // ── 1. Declarations ──
        ThesaurusEngine thesaurus;
        SymbolSnapshot? targetSnapshot;

        // ── 2. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(filePathPattern) || lineNumber <= 0)
        {
            return new SimilarToLocationResult(
                TargetFound:    false,
                WasAutoTagged:  false,
                Message:        "Invalid file path or line number.",
                TargetSymbol:   null,
                SimilarMatches: []);
        }

        // ── 3. Intent-First Narrative ──
        thesaurus      = customThesaurus ?? ThesaurusEngine.LoadForWorkspace();
        targetSnapshot = FindSnapshotAtFileLine(filePathPattern, lineNumber);

        if (targetSnapshot is null)
        {
            return new SimilarToLocationResult(
                TargetFound:    false,
                WasAutoTagged:  false,
                Message:        $"No symbol found covering '{filePathPattern}:L{lineNumber}'.",
                TargetSymbol:   null,
                SimilarMatches: []);
        }

        // ── 4. Output / Return ──
        return CompareResolvedTargetSnapshot(targetSnapshot, thesaurus, limit);
    }

    private static string BuildCoverageNotice(
        int totalTrackable,
        int taggedCount,
        int aiTaggedCount,
        int autoCpuTaggedCount,
        int untaggedCount,
        bool isScanInProgress)
    {
        if (isScanInProgress)
        {
            return "CodeAtlas.Traits is currently scanning missing repository information in the background.";
        }

        if (totalTrackable == 0)
        {
            return "No symbols have been scanned into traits.db yet.";
        }

        if (untaggedCount > 0)
        {
            return $"{taggedCount}/{totalTrackable} symbols tagged ({autoCpuTaggedCount} auto-cpu, {aiTaggedCount} ai). {untaggedCount} symbols are untagged—call 'get_untagged_batch' to tag them.";
        }

        if (autoCpuTaggedCount > 0)
        {
            return $"100% coverage ({taggedCount}/{totalTrackable} non-trivial symbols tagged: {autoCpuTaggedCount} auto-cpu ONNX baseline, {aiTaggedCount} AI-detailed).";
        }

        return $"100% coverage ({taggedCount}/{totalTrackable} non-trivial symbols tagged).";
    }

    private void ClearSnapshotsInTransaction(SqliteTransaction tx, string? scopedLanguage)
    {
        using var delCmd = connection.CreateCommand();
        delCmd.Transaction = tx;

        if (!string.IsNullOrWhiteSpace(scopedLanguage))
        {
            delCmd.CommandText = "DELETE FROM symbol_snapshots WHERE language = @lang COLLATE NOCASE;";
            delCmd.Parameters.AddWithValue("@lang", scopedLanguage);
        }
        else
        {
            delCmd.CommandText = "DELETE FROM symbol_snapshots;";
        }

        delCmd.ExecuteNonQuery();
    }

    private void InsertSnapshotsInTransaction(SqliteTransaction tx, IReadOnlyList<SymbolSnapshot> snapshots)
    {
        using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO symbol_snapshots
                (symbol_name, symbol_type, class_name, language, file_path,
                 start_line, end_line, start_byte, end_byte, signature,
                 doc_comment, body_hash, is_trivial, has_executable_body, statement_count, extracted_content)
            VALUES
                (@name, @type, @className, @lang, @filePath,
                 @startLine, @endLine, @startByte, @endByte, @sig,
                 @doc, @hash, @trivial, @execBody, @stmtCount, @content);
            """;

        var pName      = insert.Parameters.Add("@name",      SqliteType.Text);
        var pType      = insert.Parameters.Add("@type",      SqliteType.Text);
        var pClassName = insert.Parameters.Add("@className", SqliteType.Text);
        var pLang      = insert.Parameters.Add("@lang",      SqliteType.Text);
        var pFilePath  = insert.Parameters.Add("@filePath",  SqliteType.Text);
        var pStartLine = insert.Parameters.Add("@startLine", SqliteType.Integer);
        var pEndLine   = insert.Parameters.Add("@endLine",   SqliteType.Integer);
        var pStartByte = insert.Parameters.Add("@startByte", SqliteType.Integer);
        var pEndByte   = insert.Parameters.Add("@endByte",   SqliteType.Integer);
        var pSig       = insert.Parameters.Add("@sig",       SqliteType.Text);
        var pDoc       = insert.Parameters.Add("@doc",       SqliteType.Text);
        var pHash      = insert.Parameters.Add("@hash",      SqliteType.Text);
        var pTrivial   = insert.Parameters.Add("@trivial",   SqliteType.Integer);
        var pExecBody  = insert.Parameters.Add("@execBody",  SqliteType.Integer);
        var pStmtCount = insert.Parameters.Add("@stmtCount", SqliteType.Integer);
        var pContent   = insert.Parameters.Add("@content",   SqliteType.Text);

        foreach (var s in snapshots)
        {
            pName.Value      = s.SymbolName;
            pType.Value      = s.SymbolType;
            pClassName.Value = (object?)s.ClassName ?? DBNull.Value;
            pLang.Value      = s.Language;
            pFilePath.Value  = s.FilePath;
            pStartLine.Value = s.StartLine;
            pEndLine.Value   = s.EndLine;
            pStartByte.Value = s.StartByte;
            pEndByte.Value   = s.EndByte;
            pSig.Value       = (object?)s.Signature ?? DBNull.Value;
            pDoc.Value       = (object?)s.DocComment ?? DBNull.Value;
            pHash.Value      = s.BodyHash;
            pTrivial.Value   = s.IsTrivial ? 1 : 0;
            pExecBody.Value  = s.HasExecutableBody ? 1 : 0;
            pStmtCount.Value = s.StatementCount;
            pContent.Value   = (object?)s.ExtractedContent ?? DBNull.Value;

            insert.ExecuteNonQuery();
        }
    }

    private void UpsertSingleTraitEntryInTransaction(SqliteTransaction tx, TraitEntry entry, string nowUtc)
    {
        // ── 1. Declarations ──
        string effectiveSource;
        byte[]? effectiveEmbedding;
        string? existingSource;
        HashSet<string> distinctTags;

        // ── 2. Intent-First Narrative ──
        effectiveSource    = string.IsNullOrWhiteSpace(entry.TagSource) ? TraitEntry.SourceAiDetailed : entry.TagSource;
        effectiveEmbedding = entry.EmbeddingBlob ?? ComputeDefaultEmbeddingForEntry(entry);
        existingSource     = GetExistingTagSourceInTransaction(tx, entry.BodyHash);

        // Never overwrite detailed AI traits ('ai') with baseline CPU auto-tags ('auto-cpu')
        if (string.Equals(existingSource, TraitEntry.SourceAiDetailed, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(effectiveSource, TraitEntry.SourceAutoCpu, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using (var upsertEntry = connection.CreateCommand())
        {
            upsertEntry.Transaction = tx;
            upsertEntry.CommandText = """
                INSERT INTO trait_entries
                    (body_hash, summary, is_utility, tag_source, embedding_blob, category, is_actionable_debt, cluster_name, tagged_at_utc)
                VALUES
                    (@hash, @summary, @utility, @source, @embedding, @category, @actionable, @cluster, @now)
                ON CONFLICT(body_hash) DO UPDATE SET
                    summary            = excluded.summary,
                    is_utility         = excluded.is_utility,
                    tag_source         = excluded.tag_source,
                    embedding_blob     = COALESCE(excluded.embedding_blob, trait_entries.embedding_blob),
                    category           = COALESCE(excluded.category, trait_entries.category),
                    is_actionable_debt = excluded.is_actionable_debt,
                    cluster_name       = COALESCE(excluded.cluster_name, trait_entries.cluster_name),
                    tagged_at_utc      = excluded.tagged_at_utc;
                """;
            upsertEntry.Parameters.AddWithValue("@hash", entry.BodyHash);
            upsertEntry.Parameters.AddWithValue("@summary", entry.Summary ?? string.Empty);
            upsertEntry.Parameters.AddWithValue("@utility", entry.IsUtility ? 1 : 0);
            upsertEntry.Parameters.AddWithValue("@source", effectiveSource);
            upsertEntry.Parameters.AddWithValue("@embedding", (object?)effectiveEmbedding ?? DBNull.Value);
            upsertEntry.Parameters.AddWithValue("@category", (object?)entry.Category ?? DBNull.Value);
            upsertEntry.Parameters.AddWithValue("@actionable", entry.IsActionableDebt ? 1 : 0);
            upsertEntry.Parameters.AddWithValue("@cluster", (object?)entry.ClusterName ?? DBNull.Value);
            upsertEntry.Parameters.AddWithValue("@now", entry.TaggedAtUtc ?? nowUtc);
            upsertEntry.ExecuteNonQuery();
        }

        using (var delTags = connection.CreateCommand())
        {
            delTags.Transaction = tx;
            delTags.CommandText = "DELETE FROM trait_tags WHERE body_hash = @hash;";
            delTags.Parameters.AddWithValue("@hash", entry.BodyHash);
            delTags.ExecuteNonQuery();
        }

        distinctTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var insTag = connection.CreateCommand();
        insTag.Transaction = tx;
        insTag.CommandText = "INSERT INTO trait_tags (body_hash, tag) VALUES (@hash, @tag);";
        var pHash = insTag.Parameters.Add("@hash", SqliteType.Text);
        var pTag  = insTag.Parameters.Add("@tag",  SqliteType.Text);

        foreach (var rawTag in entry.Tags)
        {
            var normTag = rawTag.NormaliseTag();
            // Never store programming language names in trait_tags; CodeAtlas already tracks file languages.
            if (string.IsNullOrWhiteSpace(normTag) ||
                LanguageNormalizer.TryExtractLanguageTag(normTag, out _) ||
                !distinctTags.Add(normTag))
            {
                continue;
            }

            pHash.Value = entry.BodyHash;
            pTag.Value  = normTag;
            insTag.ExecuteNonQuery();
        }
    }

    private string? GetExistingTagSourceInTransaction(SqliteTransaction tx, string bodyHash)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT tag_source FROM trait_entries WHERE body_hash = @hash LIMIT 1;";
        cmd.Parameters.AddWithValue("@hash", bodyHash);
        var val = cmd.ExecuteScalar();
        return val is DBNull or null ? null : Convert.ToString(val);
    }

    private static byte[] ComputeDefaultEmbeddingForEntry(TraitEntry entry)
    {
        var engine    = CpuSemanticEngine.LoadForWorkspace();
        var thesaurus = ThesaurusEngine.LoadForWorkspace();
        var text      = $"{entry.Summary} {string.Join(' ', entry.Tags)}";
        return CpuSemanticEngine.SerialiseEmbedding(engine.ComputeEmbedding(text, thesaurus));
    }

    private static ParsedSearchFilter ParseSearchFilter(
        IReadOnlyList<string>? rawTags,
        string? explicitLanguage,
        string? symbolType,
        bool? onlyUtilities,
        bool matchAllTags,
        ThesaurusEngine? thesaurus)
    {
        // ── 1. Declarations ──
        string? resolvedLanguage;
        List<string> capabilityTags;
        List<IReadOnlyList<string>> tagClusters;

        // ── 2. Intent-First Narrative ──
        resolvedLanguage = LanguageNormalizer.Normalise(explicitLanguage);
        capabilityTags   = [];
        tagClusters      = [];

        if (rawTags is not null)
        {
            foreach (var raw in rawTags)
            {
                if (LanguageNormalizer.TryExtractLanguageTag(raw, out var langFromTag))
                {
                    resolvedLanguage ??= langFromTag;
                    continue;
                }

                var norm = raw.NormaliseTag();
                if (!string.IsNullOrWhiteSpace(norm) && !capabilityTags.Contains(norm, StringComparer.Ordinal))
                {
                    capabilityTags.Add(norm);
                    var cluster = thesaurus is not null
                        ? thesaurus.ExpandTagToCluster(norm)
                        : [norm];
                    tagClusters.Add(cluster.Count > 0 ? cluster : [norm]);
                }
            }
        }

        // ── 3. Output / Return ──
        return new ParsedSearchFilter(
            CapabilityTags: capabilityTags,
            TagClusters:    tagClusters,
            Language:       resolvedLanguage,
            SymbolType:     string.IsNullOrWhiteSpace(symbolType) ? null : symbolType.Trim(),
            OnlyUtilities:  onlyUtilities,
            MatchAllTags:   matchAllTags);
    }

    private List<TraitSearchMatch> ExecuteTraitSearchQuery(ParsedSearchFilter filter, int limit)
    {
        // ── 1. Declarations ──
        List<TraitSearchMatch> results;
        List<(SymbolSnapshot Snapshot, string Summary, bool IsUtility)> matchedRows;

        // ── 2. Intent-First Narrative ──
        results     = [];
        matchedRows = [];

        using (var cmd = connection.CreateCommand())
        {
            BuildTraitSearchSql(cmd, filter, limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var snap = new SymbolSnapshot
                {
                    SymbolName = reader.GetString(0),
                    SymbolType = reader.GetString(1),
                    ClassName  = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Language   = reader.GetString(3),
                    FilePath   = reader.GetString(4),
                    StartLine  = reader.GetInt32(5),
                    EndLine    = reader.GetInt32(6),
                    StartByte  = 0,
                    EndByte    = 0,
                    Signature  = reader.IsDBNull(7) ? null : reader.GetString(7),
                    BodyHash   = reader.GetString(8)
                };
                var summary   = reader.GetString(9);
                var isUtility = reader.GetInt32(10) == 1;
                matchedRows.Add((snap, summary, isUtility));
            }
        }

        foreach (var (snap, summary, isUtility) in matchedRows)
        {
            var tags = GetTagsForBodyHash(snap.BodyHash);
            results.Add(new TraitSearchMatch(
                SymbolName: snap.SymbolName,
                SymbolType: snap.SymbolType,
                ClassName:  snap.ClassName,
                Language:   snap.Language,
                FilePath:   snap.FilePath,
                StartLine:  snap.StartLine,
                EndLine:    snap.EndLine,
                Signature:  snap.Signature,
                BodyHash:   snap.BodyHash,
                Summary:    summary,
                IsUtility:  isUtility,
                Tags:       tags));
        }

        // ── 3. Output / Return ──
        return results;
    }

    private static void BuildTraitSearchSql(SqliteCommand cmd, ParsedSearchFilter filter, int limit)
    {
        // ── 1. Declarations ──
        List<string> allUnionParamNames;
        List<string> clusterScoreExpressions;
        List<string> exactScoreExpressions;

        // ── 2. Intent-First Narrative ──
        cmd.Parameters.AddWithValue("@lang", (object?)filter.Language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", (object?)filter.SymbolType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@util", filter.OnlyUtilities.HasValue ? (filter.OnlyUtilities.Value ? 1 : 0) : DBNull.Value);
        cmd.Parameters.AddWithValue("@limit", limit);

        if (filter.TagClusters.Count == 0)
        {
            cmd.CommandText = """
                SELECT s.symbol_name, s.symbol_type, s.class_name, s.language,
                       s.file_path, s.start_line, s.end_line, s.signature,
                       s.body_hash, e.summary, e.is_utility
                FROM symbol_snapshots s
                JOIN trait_entries e ON e.body_hash = s.body_hash
                WHERE (@lang IS NULL OR s.language = @lang COLLATE NOCASE)
                  AND (@type IS NULL OR s.symbol_type = @type COLLATE NOCASE)
                  AND (@util IS NULL OR e.is_utility = @util)
                ORDER BY e.is_utility DESC, s.file_path, s.start_line
                LIMIT @limit;
                """;
            return;
        }

        allUnionParamNames      = [];
        clusterScoreExpressions = new List<string>(filter.TagClusters.Count);
        exactScoreExpressions   = new List<string>(filter.TagClusters.Count);

        for (var cIdx = 0; cIdx < filter.TagClusters.Count; cIdx++)
        {
            var cluster         = filter.TagClusters[cIdx];
            var clusterParams   = new List<string>(cluster.Count);
            var exactParamName  = $"@exact_{cIdx}";
            cmd.Parameters.AddWithValue(exactParamName, filter.CapabilityTags[cIdx]);
            exactScoreExpressions.Add($"MAX(CASE WHEN t.tag = {exactParamName} THEN 1 ELSE 0 END)");

            for (var sIdx = 0; sIdx < cluster.Count; sIdx++)
            {
                var pName = $"@c{cIdx}_{sIdx}";
                clusterParams.Add(pName);
                allUnionParamNames.Add(pName);
                cmd.Parameters.AddWithValue(pName, cluster[sIdx]);
            }

            var clusterInClause = string.Join(", ", clusterParams);
            clusterScoreExpressions.Add($"MAX(CASE WHEN t.tag IN ({clusterInClause}) THEN 1 ELSE 0 END)");
        }

        var unionInClause    = string.Join(", ", allUnionParamNames);
        var clusterSumExpr   = string.Join(" + ", clusterScoreExpressions);
        var exactSumExpr     = string.Join(" + ", exactScoreExpressions);
        var requiredClusters = filter.MatchAllTags ? filter.TagClusters.Count : 1;
        cmd.Parameters.AddWithValue("@reqCount", requiredClusters);

        cmd.CommandText = $"""
            SELECT s.symbol_name, s.symbol_type, s.class_name, s.language,
                   s.file_path, s.start_line, s.end_line, s.signature,
                   s.body_hash, e.summary, e.is_utility
            FROM symbol_snapshots s
            JOIN trait_entries e ON e.body_hash = s.body_hash
            JOIN trait_tags t    ON t.body_hash = s.body_hash
            WHERE t.tag IN ({unionInClause})
              AND (@lang IS NULL OR s.language = @lang COLLATE NOCASE)
              AND (@type IS NULL OR s.symbol_type = @type COLLATE NOCASE)
              AND (@util IS NULL OR e.is_utility = @util)
            GROUP BY s.id
            HAVING ({clusterSumExpr}) >= @reqCount
            ORDER BY ({clusterSumExpr}) DESC, ({exactSumExpr}) DESC, e.is_utility DESC, s.file_path, s.start_line
            LIMIT @limit;
            """;
    }

    private IReadOnlyList<string> GetTagsForBodyHash(string bodyHash)
    {
        // ── 1. Declarations ──
        List<string> tags;

        // ── 2. Intent-First Narrative ──
        tags = [];
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT tag FROM trait_tags WHERE body_hash = @hash ORDER BY tag;";
        cmd.Parameters.AddWithValue("@hash", bodyHash);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tags.Add(reader.GetString(0));
        }

        // ── 3. Output / Return ──
        return tags;
    }

    private IReadOnlyList<DuplicateSymbolLocation> GetLocationsForBodyHash(string bodyHash)
    {
        // ── 1. Declarations ──
        List<DuplicateSymbolLocation> locations;

        // ── 2. Intent-First Narrative ──
        locations = [];
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT symbol_name, symbol_type, class_name, language,
                   file_path, start_line, end_line, signature
            FROM symbol_snapshots
            WHERE body_hash = @hash
            ORDER BY file_path, start_line;
            """;
        cmd.Parameters.AddWithValue("@hash", bodyHash);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            locations.Add(new DuplicateSymbolLocation(
                SymbolName: reader.GetString(0),
                SymbolType: reader.GetString(1),
                ClassName:  reader.IsDBNull(2) ? null : reader.GetString(2),
                Language:   reader.GetString(3),
                FilePath:   reader.GetString(4),
                StartLine:  reader.GetInt32(5),
                EndLine:    reader.GetInt32(6),
                Signature:  reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        // ── 3. Output / Return ──
        return locations;
    }

    private List<DuplicateBodyGroup> QueryRawDuplicateBodyGroups(
        string? language,
        int limit,
        bool includeContracts,
        bool onlyActionableDebt,
        string? workspaceRoot)
    {
        // ── 1. Declarations ──
        string? normalisedLanguage;
        TraitsAppConfig config;
        List<(string BodyHash, string? Summary, bool HasExecBody, int StmtCount, string? Category, bool Actionable, string? Cluster)> duplicateRows;
        List<DuplicateBodyGroup> groups;

        // ── 2. Preconditions & Guard Clauses ──
        normalisedLanguage = LanguageNormalizer.Normalise(language);
        config             = TraitsAppConfig.Load(workspaceRoot);
        duplicateRows      = [];
        groups             = [];

        // ── 3. Intent-First Narrative ──
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.body_hash,
                       e.summary,
                       MAX(s.has_executable_body) AS has_exec,
                       MAX(s.statement_count)     AS stmt_cnt,
                       e.category,
                       COALESCE(e.is_actionable_debt, 1) AS actionable,
                       e.cluster_name
                FROM symbol_snapshots s
                LEFT JOIN trait_entries e ON e.body_hash = s.body_hash
                WHERE s.is_trivial = 0
                  AND (@includeContracts = 1 OR s.has_executable_body = 1)
                  AND (@onlyActionable = 0 OR COALESCE(e.is_actionable_debt, 1) = 1)
                  AND (@lang IS NULL OR s.language = @lang COLLATE NOCASE)
                GROUP BY s.body_hash
                HAVING COUNT(*) > 1
                ORDER BY COUNT(*) DESC, stmt_cnt DESC
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@includeContracts", includeContracts ? 1 : 0);
            cmd.Parameters.AddWithValue("@onlyActionable", onlyActionableDebt ? 1 : 0);
            cmd.Parameters.AddWithValue("@lang", (object?)normalisedLanguage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 500));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                duplicateRows.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) || reader.GetInt32(2) == 1,
                    reader.IsDBNull(3) ? 1 : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) || reader.GetInt32(5) == 1,
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        foreach (var row in duplicateRows)
        {
            var locations = GetLocationsForBodyHash(row.BodyHash)
                .Where(loc => !config.IsPathIgnored(loc.FilePath))
                .ToList();

            if (locations.Count < 2)
            {
                continue;
            }

            var tags = GetTagsForBodyHash(row.BodyHash);
            groups.Add(new DuplicateBodyGroup(
                BodyHash:          row.BodyHash,
                OccurrenceCount:   locations.Count,
                Summary:           row.Summary,
                Tags:              tags,
                Locations:         locations,
                HasExecutableBody: row.HasExecBody,
                StatementCount:    row.StmtCount,
                Category:          row.Category,
                IsActionableDebt:  row.Actionable,
                ClusterName:       row.Cluster));
        }

        // ── 4. Output / Return ──
        return groups;
    }

    private static IReadOnlyList<SubsystemDuplicateCluster> BuildSubsystemClustersFromGroups(
        IReadOnlyList<DuplicateBodyGroup> groups)
    {
        // ── 1. Declarations ──
        Dictionary<(string FileA, string FileB), (HashSet<string> Symbols, HashSet<string> Hashes)> pairMap;
        List<SubsystemDuplicateCluster> clusters;

        // ── 2. Preconditions & Guard Clauses ──
        if (groups.Count == 0)
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        pairMap  = new Dictionary<(string FileA, string FileB), (HashSet<string> Symbols, HashSet<string> Hashes)>();
        clusters = [];

        foreach (var group in groups)
        {
            var distinctFiles = group.Locations
                .Select(l => l.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < distinctFiles.Count; i++)
            {
                for (var j = i + 1; j < distinctFiles.Count; j++)
                {
                    var key = (distinctFiles[i], distinctFiles[j]);
                    if (!pairMap.TryGetValue(key, out var bucket))
                    {
                        bucket = (
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                        pairMap[key] = bucket;
                    }

                    foreach (var loc in group.Locations.Where(l =>
                        string.Equals(l.FilePath, key.Item1, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(l.FilePath, key.Item2, StringComparison.OrdinalIgnoreCase)))
                    {
                        bucket.Symbols.Add(loc.SymbolName);
                    }

                    bucket.Hashes.Add(group.BodyHash);
                }
            }
        }

        foreach (var (pair, bucket) in pairMap)
        {
            if (bucket.Hashes.Count < 2)
            {
                continue;
            }

            var sortedSymbols = bucket.Symbols.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var sortedHashes  = bucket.Hashes.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToList();
            var summary       = $"Subsystem Duplication: {pair.FileA} <-> {pair.FileB} ({sortedHashes.Count} identical types/methods: {string.Join(", ", sortedSymbols)})";

            clusters.Add(new SubsystemDuplicateCluster(
                PrimaryFilePath:      pair.FileA,
                SecondaryFilePath:    pair.FileB,
                IdenticalSymbolCount: sortedHashes.Count,
                SymbolNames:          sortedSymbols,
                BodyHashes:           sortedHashes,
                Summary:              summary));
        }

        // ── 4. Output / Return ──
        return clusters
            .OrderByDescending(c => c.IdenticalSymbolCount)
            .ThenBy(c => c.PrimaryFilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private TraitSearchMatch? GetSymbolTraitMatchById(long snapshotId)
    {
        // ── 1. Declarations ──
        TraitSearchMatch? match;
        string bodyHash;

        // ── 2. Intent-First Narrative ──
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.symbol_name, s.symbol_type, s.class_name, s.language,
                   s.file_path, s.start_line, s.end_line, s.signature,
                   s.body_hash, e.summary, e.is_utility, e.tag_source,
                   e.category, COALESCE(e.is_actionable_debt, 1), e.cluster_name
            FROM symbol_snapshots s
            JOIN trait_entries e ON e.body_hash = s.body_hash
            WHERE s.id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", snapshotId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        bodyHash = reader.GetString(8);
        match = new TraitSearchMatch(
            SymbolName:       reader.GetString(0),
            SymbolType:       reader.GetString(1),
            ClassName:        reader.IsDBNull(2) ? null : reader.GetString(2),
            Language:         reader.GetString(3),
            FilePath:         reader.GetString(4),
            StartLine:        reader.GetInt32(5),
            EndLine:          reader.GetInt32(6),
            Signature:        reader.IsDBNull(7) ? null : reader.GetString(7),
            BodyHash:         bodyHash,
            Summary:          reader.GetString(9),
            IsUtility:        reader.GetInt32(10) == 1,
            Tags:             [],
            TagSource:        reader.IsDBNull(11) ? TraitEntry.SourceAiDetailed : reader.GetString(11),
            Category:         reader.IsDBNull(12) ? null : reader.GetString(12),
            IsActionableDebt: reader.IsDBNull(13) || reader.GetInt32(13) == 1,
            ClusterName:      reader.IsDBNull(14) ? null : reader.GetString(14));

        reader.Close();

        // ── 3. Output / Return ──
        return match with { Tags = GetTagsForBodyHash(bodyHash) };
    }

    private SymbolSnapshot? ResolveSnapshotFromTargetReference(string rawTargetReference)
    {
        if (rawTargetReference.TryParseFileLineReference(out var filePathPattern, out var lineNumber))
        {
            return FindSnapshotAtFileLine(filePathPattern, lineNumber);
        }

        if (rawTargetReference.TryParseSymbolTargetReference(out var containerPattern, out var symbolName))
        {
            return FindSnapshotBySymbolTarget(containerPattern, symbolName);
        }

        return null;
    }

    private (string? TagSource, string? Summary) GetTraitEntryMetaForBodyHash(string bodyHash)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT tag_source, summary FROM trait_entries WHERE body_hash = @hash LIMIT 1;";
        cmd.Parameters.AddWithValue("@hash", bodyHash);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return (null, null);
        }

        return (
            reader.IsDBNull(0) ? TraitEntry.SourceAiDetailed : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1));
    }

    private float[]? GetOrComputeEmbeddingForBodyHash(string bodyHash, string fallbackText, ThesaurusEngine thesaurus)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT embedding_blob FROM trait_entries WHERE body_hash = @hash LIMIT 1;";
            cmd.Parameters.AddWithValue("@hash", bodyHash);
            var raw = cmd.ExecuteScalar();
            if (raw is byte[] bytes && bytes.Length > 0)
            {
                var parsed = CpuSemanticEngine.DeserialiseEmbedding(bytes);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }

        var engine = CpuSemanticEngine.LoadForWorkspace();
        return engine.ComputeEmbedding(fallbackText, thesaurus);
    }

    private List<UntaggedSymbolCandidate> GetUntaggedSymbolsInFiles(HashSet<string> normalisedFiles)
    {
        // ── 1. Declarations ──
        List<UntaggedSymbolCandidate> list;

        // ── 2. Intent-First Narrative ──
        list = [];
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.body_hash, s.symbol_name, s.symbol_type, s.class_name, s.language,
                   s.file_path, s.start_line, s.end_line, s.signature, s.doc_comment,
                   s.extracted_content
            FROM symbol_snapshots s
            LEFT JOIN trait_entries e ON e.body_hash = s.body_hash
            WHERE s.is_trivial = 0
              AND e.body_hash IS NULL
            ORDER BY s.file_path, s.start_line;
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var filePath = reader.GetString(5);
            if (!normalisedFiles.Contains(filePath))
            {
                continue;
            }

            list.Add(new UntaggedSymbolCandidate(
                BodyHash:         reader.GetString(0),
                SymbolName:       reader.GetString(1),
                SymbolType:       reader.GetString(2),
                ClassName:        reader.IsDBNull(3) ? null : reader.GetString(3),
                Language:         reader.GetString(4),
                FilePath:         filePath,
                StartLine:        reader.GetInt32(6),
                EndLine:          reader.GetInt32(7),
                Signature:        reader.IsDBNull(8) ? null : reader.GetString(8),
                DocComment:       reader.IsDBNull(9) ? null : reader.GetString(9),
                ContentToAnalyse: reader.IsDBNull(10) ? string.Empty : reader.GetString(10)));
        }

        // ── 3. Output / Return ──
        return list;
    }

    private List<TraitSearchMatch> GetTaggedSymbolsInFiles(HashSet<string> normalisedFiles)
    {
        // ── 1. Declarations ──
        List<TraitSearchMatch> rawList;
        List<TraitSearchMatch> populatedList;

        // ── 2. Intent-First Narrative ──
        rawList       = [];
        populatedList = [];

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT s.symbol_name, s.symbol_type, s.class_name, s.language,
                       s.file_path, s.start_line, s.end_line, s.signature,
                       s.body_hash, e.summary, e.is_utility, e.tag_source,
                       e.category, COALESCE(e.is_actionable_debt, 1), e.cluster_name
                FROM symbol_snapshots s
                JOIN trait_entries e ON e.body_hash = s.body_hash
                WHERE s.is_trivial = 0
                ORDER BY s.file_path, s.start_line;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var filePath = reader.GetString(4);
                if (!normalisedFiles.Contains(filePath))
                {
                    continue;
                }

                rawList.Add(new TraitSearchMatch(
                    SymbolName:       reader.GetString(0),
                    SymbolType:       reader.GetString(1),
                    ClassName:        reader.IsDBNull(2) ? null : reader.GetString(2),
                    Language:         reader.GetString(3),
                    FilePath:         filePath,
                    StartLine:        reader.GetInt32(5),
                    EndLine:          reader.GetInt32(6),
                    Signature:        reader.IsDBNull(7) ? null : reader.GetString(7),
                    BodyHash:         reader.GetString(8),
                    Summary:          reader.GetString(9),
                    IsUtility:        reader.GetInt32(10) == 1,
                    Tags:             [],
                    TagSource:        reader.IsDBNull(11) ? TraitEntry.SourceAiDetailed : reader.GetString(11),
                    Category:         reader.IsDBNull(12) ? null : reader.GetString(12),
                    IsActionableDebt: reader.IsDBNull(13) || reader.GetInt32(13) == 1,
                    ClusterName:      reader.IsDBNull(14) ? null : reader.GetString(14)));
            }
        }

        foreach (var item in rawList)
        {
            populatedList.Add(item with { Tags = GetTagsForBodyHash(item.BodyHash) });
        }

        // ── 3. Output / Return ──
        return populatedList;
    }

    private List<TraitSearchMatch> FindOverlappingSymbolsForDiffEntry(
        TraitSearchMatch diffSym,
        ThesaurusEngine? thesaurus)
    {
        // ── 1. Declarations ──
        IReadOnlyList<TraitSearchMatch> candidates;

        // ── 2. Preconditions & Guard Clauses ──
        if (diffSym.Tags.Count == 0)
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        candidates = SearchByTraits(
            rawTags:          diffSym.Tags,
            explicitLanguage: diffSym.Language,
            symbolType:       diffSym.SymbolType,
            onlyUtilities:    null,
            matchAllTags:     diffSym.Tags.Count <= 2,
            limit:            10,
            useThesaurus:     true,
            customThesaurus:  thesaurus);

        // ── 4. Output / Return ──
        return candidates
            .Where(c => !string.Equals(c.BodyHash, diffSym.BodyHash, StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Tags.Count >= 2)
            .ToList();
    }

    private SimilarToLocationResult CompareResolvedTargetSnapshot(
        SymbolSnapshot targetSnapshot,
        ThesaurusEngine thesaurus,
        int limit)
    {
        // ── 1. Declarations ──
        bool wasAutoTagged;
        TraitSearchMatch? targetMatch;
        List<LocationSimilarityMatch> rankedMatches;
        string qualifiedDisplayName;

        // ── 2. Intent-First Narrative ──
        wasAutoTagged = EnsureSnapshotsTaggedForComparison(targetSnapshot, thesaurus);
        targetMatch   = GetSymbolTraitMatchById(targetSnapshot.Id);

        if (targetMatch is null)
        {
            return new SimilarToLocationResult(
                TargetFound:    false,
                WasAutoTagged:  wasAutoTagged,
                Message:        $"Could not resolve traits for symbol '{targetSnapshot.SymbolName}'.",
                TargetSymbol:   null,
                SimilarMatches: []);
        }

        rankedMatches        = ScoreCandidatesAgainstTarget(targetMatch, thesaurus, Math.Clamp(limit, 1, 100));
        qualifiedDisplayName = string.IsNullOrWhiteSpace(targetMatch.ClassName)
            ? targetMatch.SymbolName
            : $"{targetMatch.ClassName}:{targetMatch.SymbolName}";

        // ── 3. Output / Return ──
        return new SimilarToLocationResult(
            TargetFound:    true,
            WasAutoTagged:  wasAutoTagged,
            Message:        $"Found {rankedMatches.Count} symbol(s) similar to '{qualifiedDisplayName}' ({targetMatch.FilePath}:L{targetMatch.StartLine}-{targetMatch.EndLine}).",
            TargetSymbol:   targetMatch,
            SimilarMatches: rankedMatches);
    }

    private SymbolSnapshot? FindSnapshotBySymbolTarget(string? containerPattern, string symbolName)
    {
        // ── 1. Declarations ──
        string? normContainer;
        string? containerSuffix;
        SymbolSnapshot? matched;

        // ── 2. Intent-First Narrative ──
        normContainer   = string.IsNullOrWhiteSpace(containerPattern) ? null : containerPattern.Trim().Trim('.');
        containerSuffix = normContainer is not null ? $"%.{normContainer}" : null;

        matched = QuerySnapshotBySymbolAndContainer(symbolName.Trim(), normContainer, containerSuffix);
        if (matched is not null)
        {
            return matched;
        }

        // Fallback if user passed "MyNamespace.MyClass" for a class whose class_name in DB is null
        if (normContainer is not null)
        {
            return QuerySnapshotBySymbolAndContainer(symbolName.Trim(), null, null);
        }

        // ── 3. Output / Return ──
        return null;
    }

    private SymbolSnapshot? QuerySnapshotBySymbolAndContainer(
        string symbolName,
        string? normContainer,
        string? containerSuffix)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol_name, symbol_type, class_name, language,
                   file_path, start_line, end_line, start_byte, end_byte,
                   signature, doc_comment, body_hash, is_trivial, extracted_content
            FROM symbol_snapshots
            WHERE symbol_name = @symbolName COLLATE NOCASE
              AND (
                  @container IS NULL
                  OR class_name = @container COLLATE NOCASE
                  OR class_name LIKE @containerSuffix COLLATE NOCASE
                  OR (class_name IS NOT NULL AND @container LIKE '%.' || class_name COLLATE NOCASE)
              )
            ORDER BY
              CASE
                  WHEN @container IS NOT NULL AND class_name = @container COLLATE NOCASE THEN 0
                  WHEN @container IS NOT NULL AND class_name LIKE @containerSuffix COLLATE NOCASE THEN 1
                  WHEN @container IS NOT NULL AND class_name IS NOT NULL AND @container LIKE '%.' || class_name COLLATE NOCASE THEN 2
                  ELSE 3
              END ASC,
              is_trivial ASC,
              id ASC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@symbolName", symbolName);
        cmd.Parameters.AddWithValue("@container", (object?)normContainer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@containerSuffix", (object?)containerSuffix ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return ReadSymbolSnapshotFromReader(reader);
    }

    private SymbolSnapshot? FindSnapshotAtFileLine(string filePathPattern, int lineNumber)
    {
        // ── 1. Declarations ──
        string normPattern;
        string suffixPattern;

        // ── 2. Intent-First Narrative ──
        normPattern   = filePathPattern.NormaliseRelativePath();
        suffixPattern = normPattern.StartsWith('/') ? $"%{normPattern}" : $"%/{normPattern}";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol_name, symbol_type, class_name, language,
                   file_path, start_line, end_line, start_byte, end_byte,
                   signature, doc_comment, body_hash, is_trivial, extracted_content
            FROM symbol_snapshots
            WHERE (file_path = @exactPath COLLATE NOCASE OR file_path LIKE @suffixPath COLLATE NOCASE)
              AND start_line <= @line
              AND end_line >= @line
            ORDER BY (end_line - start_line) ASC, start_line DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@exactPath", normPattern);
        cmd.Parameters.AddWithValue("@suffixPath", suffixPattern);
        cmd.Parameters.AddWithValue("@line", lineNumber);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        // ── 3. Output / Return ──
        return ReadSymbolSnapshotFromReader(reader);
    }

    private static SymbolSnapshot ReadSymbolSnapshotFromReader(SqliteDataReader reader)
    {
        return new SymbolSnapshot
        {
            Id                = reader.GetInt64(0),
            SymbolName        = reader.GetString(1),
            SymbolType        = reader.GetString(2),
            ClassName         = reader.IsDBNull(3) ? null : reader.GetString(3),
            Language          = reader.GetString(4),
            FilePath          = reader.GetString(5),
            StartLine         = reader.GetInt32(6),
            EndLine           = reader.GetInt32(7),
            StartByte         = reader.GetInt32(8),
            EndByte           = reader.GetInt32(9),
            Signature         = reader.IsDBNull(10) ? null : reader.GetString(10),
            DocComment        = reader.IsDBNull(11) ? null : reader.GetString(11),
            BodyHash          = reader.GetString(12),
            IsTrivial         = reader.GetInt32(13) == 1,
            ExtractedContent  = reader.IsDBNull(14) ? null : reader.GetString(14),
            HasExecutableBody = reader.FieldCount <= 15 || reader.IsDBNull(15) || reader.GetInt32(15) == 1,
            StatementCount    = reader.FieldCount <= 16 || reader.IsDBNull(16) ? 1 : reader.GetInt32(16)
        };
    }

    private bool EnsureSnapshotsTaggedForComparison(SymbolSnapshot targetSnapshot, ThesaurusEngine thesaurus)
    {
        // ── 1. Declarations ──
        bool targetWasUntagged;
        HashSet<string> existingVocab;
        List<SymbolSnapshot> untaggedPeers;
        List<TraitEntry> generatedEntries;

        // ── 2. Intent-First Narrative ──
        targetWasUntagged = !HasTraitEntry(targetSnapshot.BodyHash);
        existingVocab     = GetAllDistinctTagsInDatabase();
        untaggedPeers     = GetUntaggedSnapshotsByLanguageAndType(targetSnapshot.Language, targetSnapshot.SymbolType);
        generatedEntries  = [];

        if (targetWasUntagged && !untaggedPeers.Any(p => p.BodyHash == targetSnapshot.BodyHash))
        {
            untaggedPeers.Add(targetSnapshot);
        }

        foreach (var snap in untaggedPeers)
        {
            generatedEntries.Add(AutoTraitTagger.GenerateTraitsForSnapshot(snap, thesaurus, existingVocab));
        }

        if (generatedEntries.Count > 0)
        {
            SaveTraitEntries(generatedEntries);
        }

        // ── 3. Output / Return ──
        return targetWasUntagged;
    }

    private bool HasTraitEntry(string bodyHash)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM trait_entries WHERE body_hash = @hash LIMIT 1;";
        cmd.Parameters.AddWithValue("@hash", bodyHash);
        return cmd.ExecuteScalar() is not null;
    }

    private HashSet<string> GetAllDistinctTagsInDatabase()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT tag FROM trait_tags;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            set.Add(reader.GetString(0));
        }

        return set;
    }

    private List<SymbolSnapshot> GetAllUntaggedNonTrivialSnapshots(string? normalisedLanguage)
    {
        var list = new List<SymbolSnapshot>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.symbol_name, s.symbol_type, s.class_name, s.language,
                   s.file_path, s.start_line, s.end_line, s.start_byte, s.end_byte,
                   s.signature, s.doc_comment, s.body_hash, s.is_trivial, s.extracted_content,
                   s.has_executable_body, s.statement_count
            FROM symbol_snapshots s
            LEFT JOIN trait_entries e ON e.body_hash = s.body_hash
            WHERE s.is_trivial = 0
              AND e.body_hash IS NULL
              AND (@lang IS NULL OR s.language = @lang COLLATE NOCASE)
            GROUP BY s.body_hash;
            """;
        cmd.Parameters.AddWithValue("@lang", (object?)normalisedLanguage ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadSymbolSnapshotFromReader(reader));
        }

        return list;
    }

    private List<SymbolSnapshot> GetUntaggedSnapshotsByLanguageAndType(string language, string symbolType)
    {
        var list = new List<SymbolSnapshot>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.symbol_name, s.symbol_type, s.class_name, s.language,
                   s.file_path, s.start_line, s.end_line, s.start_byte, s.end_byte,
                   s.signature, s.doc_comment, s.body_hash, s.is_trivial, s.extracted_content
            FROM symbol_snapshots s
            LEFT JOIN trait_entries e ON e.body_hash = s.body_hash
            WHERE s.is_trivial = 0
              AND e.body_hash IS NULL
              AND s.language = @lang COLLATE NOCASE
              AND s.symbol_type = @type COLLATE NOCASE
            GROUP BY s.body_hash;
            """;
        cmd.Parameters.AddWithValue("@lang", language);
        cmd.Parameters.AddWithValue("@type", symbolType);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SymbolSnapshot
            {
                Id               = reader.GetInt64(0),
                SymbolName       = reader.GetString(1),
                SymbolType       = reader.GetString(2),
                ClassName        = reader.IsDBNull(3) ? null : reader.GetString(3),
                Language         = reader.GetString(4),
                FilePath         = reader.GetString(5),
                StartLine        = reader.GetInt32(6),
                EndLine          = reader.GetInt32(7),
                StartByte        = reader.GetInt32(8),
                EndByte          = reader.GetInt32(9),
                Signature        = reader.IsDBNull(10) ? null : reader.GetString(10),
                DocComment       = reader.IsDBNull(11) ? null : reader.GetString(11),
                BodyHash         = reader.GetString(12),
                IsTrivial        = reader.GetInt32(13) == 1,
                ExtractedContent = reader.IsDBNull(14) ? null : reader.GetString(14)
            });
        }

        return list;
    }

    private List<LocationSimilarityMatch> ScoreCandidatesAgainstTarget(
        TraitSearchMatch target,
        ThesaurusEngine thesaurus,
        int limit)
    {
        // ── 1. Declarations ──
        int targetLineCount;
        float[]? targetEmbedding;
        List<(string Tag, HashSet<string> ClusterSet)> targetClusters;
        IReadOnlyList<TraitSearchMatch> allSameTypePeers;
        List<LocationSimilarityMatch> matches;

        // ── 2. Intent-First Narrative ──
        targetLineCount = Math.Max(1, target.EndLine - target.StartLine + 1);
        targetEmbedding = GetOrComputeEmbeddingForBodyHash(
            target.BodyHash,
            $"{target.SymbolName} {target.Signature} {target.Summary} {string.Join(' ', target.Tags)}",
            thesaurus);

        targetClusters = target.Tags
            .Select(t => (
                Tag: t,
                ClusterSet: thesaurus.ExpandTagToCluster(t).ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .ToList();

        allSameTypePeers = SearchByTraits(
            rawTags:          null,
            explicitLanguage: target.Language,
            symbolType:       target.SymbolType,
            onlyUtilities:    null,
            matchAllTags:     false,
            limit:            500,
            useThesaurus:     false);

        matches = [];
        foreach (var peer in allSameTypePeers)
        {
            if (string.Equals(peer.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase) &&
                peer.StartLine == target.StartLine)
            {
                continue;
            }

            var isExactBody    = string.Equals(peer.BodyHash, target.BodyHash, StringComparison.OrdinalIgnoreCase);
            var sharedConcepts = FindSharedConcepts(targetClusters, peer.Tags);
            var peerEmbedding  = GetOrComputeEmbeddingForBodyHash(
                peer.BodyHash,
                $"{peer.SymbolName} {peer.Signature} {peer.Summary} {string.Join(' ', peer.Tags)}",
                thesaurus);

            var embSimPercent = (int)Math.Round(CpuSemanticEngine.ComputeCosineSimilarity(targetEmbedding, peerEmbedding) * 100.0);

            if (!isExactBody && sharedConcepts.Count == 0 && embSimPercent < 60)
            {
                continue;
            }

            var peerLineCount  = Math.Max(1, peer.EndLine - peer.StartLine + 1);
            var sizeSimPercent = ComputeSizeSimilarityPercent(targetLineCount, peerLineCount);
            var overallScore   = ComputeOverallSimilarityScore(
                isExactBody,
                sharedConcepts.Count,
                targetClusters.Count,
                embSimPercent,
                sizeSimPercent);

            matches.Add(new LocationSimilarityMatch(
                MatchedSymbol:              peer,
                IsExactBodyDuplicate:       isExactBody,
                SharedConceptCount:         sharedConcepts.Count,
                SharedConcepts:             sharedConcepts,
                TargetLineCount:            targetLineCount,
                CandidateLineCount:         peerLineCount,
                SizeSimilarityPercent:      sizeSimPercent,
                OverallSimilarityScore:     overallScore,
                EmbeddingSimilarityPercent: embSimPercent));
        }

        // ── 3. Output / Return ──
        return matches
            .OrderByDescending(m => m.IsExactBodyDuplicate)
            .ThenByDescending(m => m.OverallSimilarityScore)
            .ThenByDescending(m => m.SharedConceptCount)
            .ThenByDescending(m => m.EmbeddingSimilarityPercent)
            .ThenByDescending(m => m.SizeSimilarityPercent)
            .Take(limit)
            .ToList();
    }

    private static List<string> FindSharedConcepts(
        IReadOnlyList<(string Tag, HashSet<string> ClusterSet)> targetClusters,
        IReadOnlyList<string> candidateTags)
    {
        var shared = new List<string>();
        foreach (var (targetTag, clusterSet) in targetClusters)
        {
            if (candidateTags.Any(clusterSet.Contains))
            {
                shared.Add(targetTag);
            }
        }

        return shared;
    }

    private static int ComputeSizeSimilarityPercent(int linesA, int linesB)
    {
        var minLines = Math.Min(linesA, linesB);
        var maxLines = Math.Max(linesA, linesB);
        return (int)Math.Round(100.0 * minLines / Math.Max(1, maxLines));
    }

    private static int ComputeOverallSimilarityScore(
        bool isExactBody,
        int sharedConceptCount,
        int totalTargetConcepts,
        int embeddingSimPercent,
        int sizeSimPercent)
    {
        if (isExactBody)
        {
            return 100;
        }

        var tagRatio  = (double)sharedConceptCount / Math.Max(1, totalTargetConcepts);
        var tagPoints = (int)Math.Round(Math.Min(1.0, tagRatio) * 55.0);
        var embPoints = (int)Math.Round(embeddingSimPercent * 0.25);
        var sizePts   = (int)Math.Round(sizeSimPercent * 0.20);

        return Math.Clamp(tagPoints + embPoints + sizePts, 1, 99);
    }

    private sealed record ParsedSearchFilter(
        IReadOnlyList<string> CapabilityTags,
        IReadOnlyList<IReadOnlyList<string>> TagClusters,
        string? Language,
        string? SymbolType,
        bool? OnlyUtilities,
        bool MatchAllTags);
}
