using System.Text;
using CodeAtlas.Traits.Core.Scanner;
using CodeAtlas.Traits.Models;

namespace CodeAtlas.Traits.Tests;

public sealed class BodyHasherTests : IDisposable
{
    private readonly string _tempDir;

    public BodyHasherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TraitsHasherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in temp directory.
        }
    }

    [Fact]
    public void ClassSkeletonHash_IgnoresPrivateMethodsAndMethodBodyImplementationChanges()
    {
        // ── 1. Declarations ──
        string sourceV1;
        string sourceV2;
        byte[] bytesV1;
        byte[] bytesV2;
        AtlasSymbolDto classSym;
        IReadOnlyList<AtlasSymbolDto> membersV1;
        IReadOnlyList<AtlasSymbolDto> membersV2;
        string skeletonV1;
        string skeletonV2;

        // ── 2. Intent-First Narrative ──
        sourceV1 = """
            public class RetryService : IRetryService
            {
                public RetryService(ILogger logger) { _logger = logger; }
                public Task ExecuteAsync(Func<Task> action) { return action(); }
                private void SecretHelper() { var x = 1; }
            }
            """;

        // V2 changes the method body of ExecuteAsync and adds another private method:
        sourceV2 = """
            public class RetryService : IRetryService
            {
                public RetryService(ILogger logger) { _logger = logger ?? throw new ArgumentNullException(); }
                public Task ExecuteAsync(Func<Task> action) { Console.WriteLine("Changed implementation!"); return action(); }
                private void SecretHelper() { var x = 999; }
                private static bool HashesMatch(string a, string b) => a == b;
            }
            """;

        bytesV1  = Encoding.UTF8.GetBytes(sourceV1);
        bytesV2  = Encoding.UTF8.GetBytes(sourceV2);
        classSym = new AtlasSymbolDto(1, "RetryService", "Class", null, "csharp", "RetryService.cs", 1, 6, 0, bytesV1.Length, "IRetryService", null, false);

        membersV1 =
        [
            new AtlasSymbolDto(2, "RetryService", "Method", "RetryService", "csharp", "RetryService.cs", 3, 3, 0, 0, "ILogger logger", null, false),
            new AtlasSymbolDto(3, "ExecuteAsync", "Method", "RetryService", "csharp", "RetryService.cs", 4, 4, 0, 0, "public Task ExecuteAsync(Func<Task> action)", null, false),
            new AtlasSymbolDto(4, "SecretHelper", "Method", "RetryService", "csharp", "RetryService.cs", 5, 5, 0, 0, "private void SecretHelper()", null, false)
        ];

        membersV2 =
        [
            new AtlasSymbolDto(2, "RetryService", "Method", "RetryService", "csharp", "RetryService.cs", 3, 3, 0, 0, "ILogger logger", null, false),
            new AtlasSymbolDto(3, "ExecuteAsync", "Method", "RetryService", "csharp", "RetryService.cs", 4, 4, 0, 0, "public Task ExecuteAsync(Func<Task> action)", null, false),
            new AtlasSymbolDto(4, "SecretHelper", "Method", "RetryService", "csharp", "RetryService.cs", 5, 5, 0, 0, "private void SecretHelper()", null, false),
            new AtlasSymbolDto(5, "HashesMatch", "Method", "RetryService", "csharp", "RetryService.cs", 6, 6, 0, 0, "private static bool HashesMatch(string a, string b)", null, false)
        ];

        skeletonV1 = BodyHasher.BuildClassContractSkeleton(classSym, membersV1, bytesV1);
        skeletonV2 = BodyHasher.BuildClassContractSkeleton(classSym, membersV2, bytesV2);

        // ── 3. Output / Assertions ──
        Assert.Equal(skeletonV1, skeletonV2);
        Assert.DoesNotContain("SecretHelper", skeletonV1);
        Assert.DoesNotContain("HashesMatch", skeletonV2);
        Assert.Contains("IRetryService", skeletonV1);
        Assert.Contains("ExecuteAsync", skeletonV1);
    }

    [Fact]
    public void ComputeSnapshotsForFile_NormalisesWhitespaceAndDetectsTrivialMethods()
    {
        // ── 1. Declarations ──
        string filePath;
        string content;
        int fullMethodStart;
        int fullMethodEnd;
        int privateHelperStart;
        int privateHelperEnd;
        int trivialStart;
        int trivialEnd;
        IReadOnlyList<AtlasSymbolDto> symbols;
        IReadOnlyList<SymbolSnapshot> snapshots;

        // ── 2. Intent-First Narrative ──
        content = """
            public class StringHelper
            {
                public string Name { get; set; }
                public static bool StdEquals(string left, string right)
                {
                    if (left is null || right is null) return false;
                    return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
                }
                private static bool HashesMatch(string a, string b) => a == b;
            }
            """;

        filePath = Path.Combine(_tempDir, "StringHelper.cs");
        File.WriteAllText(filePath, content);

        trivialStart       = content.IndexOf("public string Name", StringComparison.Ordinal);
        trivialEnd         = content.IndexOf("}", trivialStart, StringComparison.Ordinal) + 1;
        fullMethodStart    = content.IndexOf("public static bool StdEquals", StringComparison.Ordinal);
        fullMethodEnd      = content.IndexOf("}", fullMethodStart, StringComparison.Ordinal) + 1;
        privateHelperStart = content.IndexOf("private static bool HashesMatch", StringComparison.Ordinal);
        privateHelperEnd   = content.IndexOf(";", privateHelperStart, StringComparison.Ordinal) + 1;

        symbols =
        [
            new AtlasSymbolDto(1, "Name", "Method", "StringHelper", "csharp", "StringHelper.cs", 3, 3, trivialStart, trivialEnd, null, null, false),
            new AtlasSymbolDto(2, "StdEquals", "Method", "StringHelper", "csharp", "StringHelper.cs", 4, 8, fullMethodStart, fullMethodEnd, "string left, string right", null, false),
            new AtlasSymbolDto(3, "HashesMatch", "Method", "StringHelper", "csharp", "StringHelper.cs", 9, 9, privateHelperStart, privateHelperEnd, "string a, string b", null, false)
        ];

        snapshots = BodyHasher.ComputeSnapshotsForFile(_tempDir, "StringHelper.cs", symbols);

        // ── 3. Output / Assertions ──
        Assert.Equal(3, snapshots.Count);
        Assert.True(snapshots[0].IsTrivial);
        Assert.False(snapshots[1].IsTrivial);
        Assert.False(snapshots[2].IsTrivial); // Even 1-line private helper methods are tracked to catch hidden bloat!
        Assert.Equal(16, snapshots[1].BodyHash.Length);
        Assert.Equal(16, snapshots[2].BodyHash.Length);
    }
}
