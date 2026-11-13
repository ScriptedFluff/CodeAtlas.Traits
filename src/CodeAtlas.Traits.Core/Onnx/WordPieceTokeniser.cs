using CodeAtlas.Traits.Core.Extensions;

namespace CodeAtlas.Traits.Core.Onnx;

/// <summary>
/// Pure C# BERT WordPiece tokeniser used to feed source code and signatures into CPU ONNX models
/// such as <c>all-MiniLM-L6-v2</c> or <c>bge-small</c>.
/// </summary>
public sealed class WordPieceTokeniser
{
    private readonly Dictionary<string, long> _vocab;
    private readonly long _clsId;
    private readonly long _sepId;
    private readonly long _unkId;

    private WordPieceTokeniser(Dictionary<string, long> vocab)
    {
        _vocab = vocab;
        _clsId = vocab.GetValueOrDefault("[CLS]", 101L);
        _sepId = vocab.GetValueOrDefault("[SEP]", 102L);
        _unkId = vocab.GetValueOrDefault("[UNK]", 100L);
    }

    /// <summary>
    /// Loads a <see cref="WordPieceTokeniser"/> from a line-delimited <c>vocab.txt</c> file.
    /// </summary>
    public static WordPieceTokeniser LoadFromFile(string vocabFilePath)
    {
        // ── 1. Declarations ──
        string[] lines;
        Dictionary<string, long> map;

        // ── 2. Preconditions & Guard Clauses ──
        if (!File.Exists(vocabFilePath))
        {
            throw new FileNotFoundException("WordPiece vocabulary file not found.", vocabFilePath);
        }

        // ── 3. Intent-First Narrative ──
        lines = File.ReadAllLines(vocabFilePath);
        map   = new Dictionary<string, long>(lines.Length, StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            var token = lines[i].TrimEnd('\r', '\n');
            if (token.Length > 0 && !map.ContainsKey(token))
            {
                map[token] = i;
            }
        }

        // ── 4. Output / Return ──
        return new WordPieceTokeniser(map);
    }

    /// <summary>
    /// Encodes source/text into <c>input_ids</c>, <c>attention_mask</c>, and <c>token_type_ids</c> tensors.
    /// </summary>
    public (long[] InputIds, long[] AttentionMask, long[] TokenTypeIds) Encode(
        string text,
        int maxTokens = 128)
    {
        // ── 1. Declarations ──
        IReadOnlyList<string> words;
        List<long> ids;
        int limit;
        long[] inputIds;
        long[] attentionMask;
        long[] tokenTypeIds;

        // ── 2. Preconditions & Guard Clauses ──
        limit = Math.Clamp(maxTokens, 8, 512);
        ids   = new List<long>(limit) { _clsId };
        words = text.ExtractCodeWordTokens();

        // ── 3. Intent-First Narrative ──
        foreach (var word in words)
        {
            if (ids.Count >= limit - 1)
            {
                break;
            }

            AppendWordPieces(word, ids, limit - 1);
        }

        ids.Add(_sepId);

        inputIds      = ids.ToArray();
        attentionMask = new long[inputIds.Length];
        tokenTypeIds  = new long[inputIds.Length];
        Array.Fill(attentionMask, 1L);

        // ── 4. Output / Return ──
        return (inputIds, attentionMask, tokenTypeIds);
    }

    private void AppendWordPieces(string word, List<long> targetIds, int maxCount)
    {
        // ── 1. Declarations ──
        int start;

        // ── 2. Preconditions & Guard Clauses ──
        if (_vocab.TryGetValue(word, out var directId))
        {
            targetIds.Add(directId);
            return;
        }

        // ── 3. Intent-First Narrative ──
        start = 0;
        while (start < word.Length && targetIds.Count < maxCount)
        {
            var end   = word.Length;
            var found = false;

            while (start < end)
            {
                var substr = start == 0 ? word[start..end] : $"##{word[start..end]}";
                if (_vocab.TryGetValue(substr, out var pieceId))
                {
                    targetIds.Add(pieceId);
                    start = end;
                    found = true;
                    break;
                }

                end--;
            }

            if (!found)
            {
                targetIds.Add(_unkId);
                break;
            }
        }
    }
}
