using System.Text.Json;
using CodeAtlas.Traits.Core.AtlasBridge;
using CodeAtlas.Traits.Core.Scanner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CodeAtlas.Traits.Mcp;

/// <summary>
/// Carries the bound repository root path and optional database override directory into DI.
/// </summary>
public sealed record RepoContext(string? RepoPath, string? CustomDbDir);

/// <summary>
/// Configures and hosts the stdio MCP server for <c>CodeAtlas.Traits</c>.
/// </summary>
public static class McpServer
{
    public static async Task RunAsync(
        string? repoPath = null,
        string? customDbDir = null,
        CancellationToken ct = default)
    {
        // ── 1. Declarations ──
        HostApplicationBuilder builder;
        JsonSerializerOptions jsonOptions;
        IHost host;

        // ── 2. Preconditions & Guard Clauses ──
        // Redirect Console.Out to Console.Error so stray Console.WriteLine calls never corrupt JSON-RPC stdio frames.
        Console.SetOut(Console.Error);

        // ── 3. Intent-First Narrative ──
        builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.TypeInfoResolverChain.Add(McpJsonContext.Default);

        builder.Services
            .AddSingleton(_ => new RepoContext(repoPath, customDbDir))
            .AddSingleton<ICodeAtlasCliClient>(_ => new CodeAtlasCliClient())
            .AddSingleton<ScanOrchestrator>()
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<Tools.SearchTraitsTool>(jsonOptions)
            .WithTools<Tools.GetUntaggedBatchTool>(jsonOptions)
            .WithTools<Tools.SaveTraitsTool>(jsonOptions)
            .WithTools<Tools.FindDuplicatesTool>(jsonOptions)
            .WithTools<Tools.AuditDiffTraitsTool>(jsonOptions)
            .WithTools<Tools.GetTraitsStatusTool>(jsonOptions);

        host = builder.Build();

        // ── 4. Output / Return ──
        await host.RunAsync(ct);
    }
}
