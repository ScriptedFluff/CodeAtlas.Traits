using System.Net.Http;

namespace CodeAtlas.Traits.Core.Onnx;

/// <summary>
/// Resolves CPU ONNX model files (<c>model.onnx</c> / <c>model_quantized.onnx</c> + <c>vocab.txt</c>)
/// across Workspace, Environment, User, and Application tiers, and supports downloading the compact
/// quantised <c>all-MiniLM-L6-v2</c> CPU model (~23 MB) on demand.
/// </summary>
public static class OnnxModelDirectoryResolver
{
    public const string EnvModelDirVar = "CODEATLAS_TRAITS_MODEL_DIR";

    private const string HuggingFaceMiniLmOnnxUrl =
        "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model_quantized.onnx";

    private const string HuggingFaceMiniLmVocabUrl =
        "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/vocab.txt";

    /// <summary>
    /// Returns the default user-level directory where downloaded CPU ONNX models are stored.
    /// </summary>
    public static string GetUserModelsDirectory()
    {
        // ── 1. Declarations ──
        string localAppData;

        // ── 2. Intent-First Narrative ──
        localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // ── 3. Output / Return ──
        return Path.Combine(localAppData, "CodeAtlas.Traits", "Models");
    }

    /// <summary>
    /// Searches all discovery tiers for a valid <c>model.onnx</c> (or <c>model_quantized.onnx</c>) and <c>vocab.txt</c> pair.
    /// </summary>
    public static bool TryResolveModelFiles(
        string? workspaceRoot,
        out string modelPath,
        out string vocabPath)
    {
        // ── 1. Declarations ──
        IReadOnlyList<string> candidateDirs;

        // ── 2. Preconditions & Guard Clauses ──
        modelPath     = string.Empty;
        vocabPath     = string.Empty;
        candidateDirs = GetCandidateDirectories(workspaceRoot);

        // ── 3. Intent-First Narrative ──
        foreach (var dir in candidateDirs)
        {
            if (TryFindModelPairInDirectory(dir, out modelPath, out vocabPath))
            {
                return true;
            }
        }

        // ── 4. Output / Return ──
        return false;
    }

    /// <summary>
    /// Downloads the compact Int8 quantised <c>all-MiniLM-L6-v2</c> CPU ONNX model (~23 MB) and <c>vocab.txt</c>
    /// into the user model directory (or <paramref name="customTargetDir"/>).
    /// </summary>
    public static async Task<(string ModelPath, string VocabPath)> DownloadDefaultMiniLmModelAsync(
        string? customTargetDir = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        string targetDir;
        string targetModelPath;
        string targetVocabPath;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // ── 2. Preconditions & Guard Clauses ──
        targetDir = !string.IsNullOrWhiteSpace(customTargetDir)
            ? Path.GetFullPath(customTargetDir)
            : GetUserModelsDirectory();

        Directory.CreateDirectory(targetDir);
        targetModelPath = Path.Combine(targetDir, "model_quantized.onnx");
        targetVocabPath = Path.Combine(targetDir, "vocab.txt");

        // ── 3. Intent-First Narrative ──
        await DownloadFileAsync(http, HuggingFaceMiniLmVocabUrl, targetVocabPath, ct);
        await DownloadFileAsync(http, HuggingFaceMiniLmOnnxUrl, targetModelPath, ct);

        // ── 4. Output / Return ──
        return (targetModelPath, targetVocabPath);
    }

    private static IReadOnlyList<string> GetCandidateDirectories(string? workspaceRoot)
    {
        // ── 1. Declarations ──
        List<string> dirs;
        string? envDir;

        // ── 2. Intent-First Narrative ──
        dirs = [];

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            dirs.Add(Path.Combine(workspaceRoot, ".codeatlas", "models"));
        }

        envDir = Environment.GetEnvironmentVariable(EnvModelDirVar);
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            dirs.Add(envDir);
        }

        dirs.Add(GetUserModelsDirectory());
        dirs.Add(Path.Combine(AppContext.BaseDirectory, "Models"));

        // ── 3. Output / Return ──
        return dirs;
    }

    private static bool TryFindModelPairInDirectory(
        string directory,
        out string modelPath,
        out string vocabPath)
    {
        // ── 1. Declarations ──
        string candidateVocab;
        string quantisedModel;
        string standardModel;

        // ── 2. Preconditions & Guard Clauses ──
        modelPath = string.Empty;
        vocabPath = string.Empty;

        if (!Directory.Exists(directory))
        {
            return false;
        }

        // ── 3. Intent-First Narrative ──
        candidateVocab = Path.Combine(directory, "vocab.txt");
        if (!File.Exists(candidateVocab))
        {
            return false;
        }

        quantisedModel = Path.Combine(directory, "model_quantized.onnx");
        if (File.Exists(quantisedModel))
        {
            modelPath = quantisedModel;
            vocabPath = candidateVocab;
            return true;
        }

        standardModel = Path.Combine(directory, "model.onnx");
        if (File.Exists(standardModel))
        {
            modelPath = standardModel;
            vocabPath = candidateVocab;
            return true;
        }

        // ── 4. Output / Return ──
        return false;
    }

    private static async Task DownloadFileAsync(
        HttpClient http,
        string url,
        string destinationPath,
        CancellationToken ct)
    {
        // ── 1. Declarations ──
        string tempPath;

        // ── 2. Intent-First Narrative ──
        tempPath = destinationPath + ".tmp";
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var netStream  = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = File.Create(tempPath);
            await netStream.CopyToAsync(fileStream, ct);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
    }
}
