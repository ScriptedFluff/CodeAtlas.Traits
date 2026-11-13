using System.Runtime.InteropServices;
using CodeAtlas.Traits.Core.Extensions;
using CodeAtlas.Traits.Core.Thesaurus;
using CodeAtlas.Traits.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CodeAtlas.Traits.Core.Onnx;

/// <summary>
/// CPU-only semantic embedding and zero-shot trait inference engine powered by <c>Microsoft.ML.OnnxRuntime</c>.
/// Runs a local BERT/MiniLM ONNX model on CPU when present in <c>Models/</c>, and automatically falls back
/// to a deterministic 128-dimensional CPU feature-hashing + thesaurus subspace vectoriser when no <c>.onnx</c> file is installed.
/// </summary>
public sealed class CpuSemanticEngine : IDisposable
{
    public const int FallbackDimensions = 128;

    private static readonly object SharedCacheGate = new();
    private static string? _cachedModelPath;
    private static CpuSemanticEngine? _cachedInstance;

    private readonly InferenceSession? _onnxSession;
    private readonly WordPieceTokeniser? _tokeniser;
    private readonly string? _modelPath;
    private readonly string? _vocabPath;
    private readonly int _dimensions;

    private CpuSemanticEngine(
        InferenceSession? onnxSession,
        WordPieceTokeniser? tokeniser,
        string? modelPath,
        string? vocabPath,
        int dimensions)
    {
        _onnxSession = onnxSession;
        _tokeniser   = tokeniser;
        _modelPath   = modelPath;
        _vocabPath   = vocabPath;
        _dimensions  = dimensions;
    }

    public bool IsOnnxModelLoaded => _onnxSession is not null && _tokeniser is not null;
    public int Dimensions => _dimensions;

    /// <summary>
    /// Loads or retrieves the cached <see cref="CpuSemanticEngine"/> for the given workspace.
    /// </summary>
    public static CpuSemanticEngine LoadForWorkspace(string? workspaceRoot = null)
    {
        // ── 1. Declarations ──
        bool hasOnnxFiles;
        string modelPath;
        string vocabPath;

        // ── 2. Intent-First Narrative ──
        hasOnnxFiles = OnnxModelDirectoryResolver.TryResolveModelFiles(workspaceRoot, out modelPath, out vocabPath);

        lock (SharedCacheGate)
        {
            if (hasOnnxFiles &&
                _cachedInstance is not null &&
                string.Equals(_cachedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedInstance;
            }

            if (!hasOnnxFiles && _cachedInstance is { IsOnnxModelLoaded: false })
            {
                return _cachedInstance;
            }

            if (hasOnnxFiles)
            {
                var loaded = TryCreateOnnxEngineQuietly(modelPath, vocabPath);
                if (loaded is not null)
                {
                    _cachedModelPath = modelPath;
                    _cachedInstance  = loaded;
                    return loaded;
                }
            }

            _cachedModelPath = null;
            _cachedInstance  = new CpuSemanticEngine(null, null, null, null, FallbackDimensions);
            return _cachedInstance;
        }
    }

    /// <summary>
    /// Returns diagnostic status about the active CPU semantic engine.
    /// </summary>
    public CpuModelStatusDto GetStatus()
    {
        // ── 1. Declarations ──
        string mode;
        string desc;

        // ── 2. Intent-First Narrative ──
        if (IsOnnxModelLoaded)
        {
            mode = "onnx-cpu";
            desc = $"Running CPU ONNX Runtime session ({_dimensions}-dim) from '{_modelPath}'.";
        }
        else
        {
            mode = "builtin-cpu-vectoriser";
            desc = $"Running built-in 128-dim CPU feature-hashing + thesaurus subspace vectoriser. Run 'codeatlas.traits model download' to install the 23MB all-MiniLM-L6-v2 CPU ONNX model into '{OnnxModelDirectoryResolver.GetUserModelsDirectory()}'.";
        }

        // ── 3. Output / Return ──
        return new CpuModelStatusDto(
            EngineMode:          mode,
            IsOnnxModelLoaded:   IsOnnxModelLoaded,
            ModelPath:           _modelPath,
            VocabPath:           _vocabPath,
            EmbeddingDimensions: _dimensions,
            Description:         desc);
    }

    /// <summary>
    /// Computes an L2-normalised semantic embedding vector on CPU for the given code/text and optional thesaurus.
    /// </summary>
    public float[] ComputeEmbedding(string text, ThesaurusEngine? thesaurus = null)
    {
        // ── 1. Preconditions & Guard Clauses ──
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[_dimensions];
        }

        // ── 2. Intent-First Narrative ──
        if (_onnxSession is not null && _tokeniser is not null)
        {
            var onnxVec = TryRunOnnxInferenceQuietly(text);
            if (onnxVec is not null && onnxVec.Length > 0)
            {
                return onnxVec;
            }
        }

        // ── 3. Output / Return ──
        return ComputeFallbackCpuEmbedding(text, thesaurus);
    }

    /// <summary>
    /// Computes cosine similarity in the range <c>[0.0, 1.0]</c> between two L2-normalised embedding vectors.
    /// </summary>
    public static double ComputeCosineSimilarity(float[]? vecA, float[]? vecB)
    {
        // ── 1. Declarations ──
        double dot;
        double normA;
        double normB;

        // ── 2. Preconditions & Guard Clauses ──
        if (vecA is null || vecB is null || vecA.Length == 0 || vecA.Length != vecB.Length)
        {
            return 0.0;
        }

        // ── 3. Intent-First Narrative ──
        dot   = 0.0;
        normA = 0.0;
        normB = 0.0;

        for (var i = 0; i < vecA.Length; i++)
        {
            dot   += vecA[i] * vecB[i];
            normA += vecA[i] * vecA[i];
            normB += vecB[i] * vecB[i];
        }

        if (normA <= 1e-12 || normB <= 1e-12)
        {
            return 0.0;
        }

        // ── 4. Output / Return ──
        return Math.Clamp(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)), 0.0, 1.0);
    }

    /// <summary>
    /// Serialises a <c>float[]</c> embedding vector to a compact little-endian <c>byte[]</c> blob for SQLite storage.
    /// </summary>
    public static byte[] SerialiseEmbedding(float[] vector)
    {
        // ── 1. Declarations ──
        byte[] bytes;

        // ── 2. Preconditions & Guard Clauses ──
        if (vector.Length == 0)
        {
            return [];
        }

        // ── 3. Intent-First Narrative ──
        bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);

        // ── 4. Output / Return ──
        return bytes;
    }

    /// <summary>
    /// Deserialises a SQLite <c>BLOB</c> back into a <c>float[]</c> embedding vector.
    /// </summary>
    public static float[]? DeserialiseEmbedding(byte[]? blob)
    {
        // ── 1. Declarations ──
        float[] vector;

        // ── 2. Preconditions & Guard Clauses ──
        if (blob is null || blob.Length == 0 || blob.Length % sizeof(float) != 0)
        {
            return null;
        }

        // ── 3. Intent-First Narrative ──
        vector = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);

        // ── 4. Output / Return ──
        return vector;
    }

    public void Dispose()
    {
        _onnxSession?.Dispose();
    }

    private static CpuSemanticEngine? TryCreateOnnxEngineQuietly(string modelPath, string vocabPath)
    {
        try
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode          = ExecutionMode.ORT_SEQUENTIAL,
                IntraOpNumThreads      = 1,
                InterOpNumThreads      = 1
            };

            var session   = new InferenceSession(modelPath, options);
            var tokeniser = WordPieceTokeniser.LoadFromFile(vocabPath);
            var dims      = DetectOutputDimensions(session);

            return new CpuSemanticEngine(session, tokeniser, modelPath, vocabPath, dims);
        }
        catch
        {
            return null;
        }
    }

    private static int DetectOutputDimensions(InferenceSession session)
    {
        foreach (var output in session.OutputMetadata.Values)
        {
            if (output.Dimensions.Length >= 2 && output.Dimensions[^1] > 0)
            {
                return output.Dimensions[^1];
            }
        }

        return 384;
    }

    private float[]? TryRunOnnxInferenceQuietly(string text)
    {
        try
        {
            var (inputIds, attentionMask, tokenTypeIds) = _tokeniser!.Encode(text, maxTokens: 48);
            var seqLen = inputIds.Length;
            var shape  = new[] { 1, seqLen };

            var inputs = new List<NamedOnnxValue>(3)
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, shape)),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionMask, shape))
            };

            if (_onnxSession!.InputMetadata.ContainsKey("token_type_ids"))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(tokenTypeIds, shape)));
            }

            using var results = _onnxSession.Run(inputs);
            var firstOutput   = results.FirstOrDefault()?.AsTensor<float>();
            if (firstOutput is null)
            {
                return null;
            }

            return PoolAndNormaliseTensor(firstOutput, attentionMask);
        }
        catch
        {
            return null;
        }
    }

    private static float[] PoolAndNormaliseTensor(Tensor<float> outputTensor, long[] attentionMask)
    {
        // ── 1. Declarations ──
        int hiddenSize;
        int seqLen;
        float[] pooled;
        float validTokens;

        // ── 2. Intent-First Narrative ──
        hiddenSize  = outputTensor.Dimensions[^1];
        pooled      = new float[hiddenSize];
        validTokens = 0f;

        if (outputTensor.Dimensions.Length == 2)
        {
            for (var d = 0; d < hiddenSize; d++)
            {
                pooled[d] = outputTensor[0, d];
            }
        }
        else if (outputTensor.Dimensions.Length == 3)
        {
            seqLen = outputTensor.Dimensions[1];
            for (var t = 0; t < seqLen; t++)
            {
                if (t < attentionMask.Length && attentionMask[t] == 0)
                {
                    continue;
                }

                validTokens += 1f;
                for (var d = 0; d < hiddenSize; d++)
                {
                    pooled[d] += outputTensor[0, t, d];
                }
            }

            if (validTokens > 0f)
            {
                for (var d = 0; d < hiddenSize; d++)
                {
                    pooled[d] /= validTokens;
                }
            }
        }

        NormaliseVectorInPlace(pooled);

        // ── 3. Output / Return ──
        return pooled;
    }

    private static float[] ComputeFallbackCpuEmbedding(string text, ThesaurusEngine? thesaurus)
    {
        // ── 1. Declarations ──
        float[] vec;
        IReadOnlyList<string> tokens;

        // ── 2. Intent-First Narrative ──
        vec    = new float[FallbackDimensions];
        tokens = text.ExtractCodeWordTokens();

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            AccumulateHashedFeature(vec, token, weight: 1.0f);

            if (thesaurus is not null && thesaurus.IsKnownConceptTerm(token, out var canonical))
            {
                // Project synonym cluster terms into the exact same canonical concept dimensions!
                AccumulateHashedFeature(vec, $"concept::{canonical}", weight: 2.5f);
            }

            if (i + 1 < tokens.Count)
            {
                AccumulateHashedFeature(vec, $"{token}_{tokens[i + 1]}", weight: 0.75f);
            }
        }

        NormaliseVectorInPlace(vec);

        // ── 3. Output / Return ──
        return vec;
    }

    private static void AccumulateHashedFeature(float[] vec, string feature, float weight)
    {
        // FNV-1a 32-bit hash for deterministic cross-platform bucket & sign projection
        var hash = 2166136261u;
        for (var i = 0; i < feature.Length; i++)
        {
            hash ^= feature[i];
            hash *= 16777619u;
        }

        var bucket = (int)(hash % (uint)vec.Length);
        var sign   = ((hash >> 24) & 1u) == 0u ? 1.0f : -1.0f;
        vec[bucket] += sign * weight;
    }

    private static void NormaliseVectorInPlace(float[] vec)
    {
        var sumSq = 0.0;
        for (var i = 0; i < vec.Length; i++)
        {
            sumSq += vec[i] * vec[i];
        }

        if (sumSq <= 1e-12)
        {
            return;
        }

        var invNorm = (float)(1.0 / Math.Sqrt(sumSq));
        for (var i = 0; i < vec.Length; i++)
        {
            vec[i] *= invNorm;
        }
    }
}
