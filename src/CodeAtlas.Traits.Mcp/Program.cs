using CodeAtlas.Traits.Mcp;

string? repoPath = null;
string? dbDir    = null;

for (var i = 0; i < args.Length; i++)
{
    if ((args[i] == "--repo" || args[i] == "-r") && i + 1 < args.Length)
    {
        repoPath = args[++i];
    }
    else if (args[i] == "--db-dir" && i + 1 < args.Length)
    {
        dbDir = args[++i];
    }
    else if (!args[i].StartsWith('-') && repoPath is null && args[i] != "serve")
    {
        repoPath = args[i];
    }
}

await McpServer.RunAsync(repoPath, dbDir);
