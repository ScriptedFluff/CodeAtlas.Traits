# CodeAtlas.Traits — AI Companion & MCP Server Setup Guide (`SETUP.md`)

> **Audience**: This document is written for **AI Coding Companions** (Antigravity / Gemini, GitHub Copilot, Claude Code / Claude Desktop, Cursor, Windsurf, Roo Code, Cline) and developers setting up the `CodeAtlas.Traits` Model Context Protocol (MCP) server.

`CodeAtlas.Traits` is the capability, behaviour, and technology indexing companion for [`CodeAtlas`](https://github.com/ScriptedFluff/CodeAtlas). It runs as a local **stdio MCP server** (`CodeAtlas.Traits.Mcp.exe`) with 100% CPU-based execution (zero GPU or external daemon required).

---

## Step 1: Verify Prerequisites

Before installing `CodeAtlas.Traits`, verify that:
1. **`.NET 10.0 SDK`** is installed (`dotnet --version`).
2. **`CodeAtlas`** is installed or published in its sibling directory (`../CodeAtlas/publish/cli/CodeAtlas.exe`) or registered on `PATH` / `CODEATLAS_PATH`.
   - If `CodeAtlas` has not been published yet, run:
     ```powershell
     powershell -ExecutionPolicy Bypass -File "D:\Projects\CodeAtlas\scripts\publish.ps1"
     ```

---

## Step 2: Publish `CodeAtlas.Traits` Release Binaries

Always publish `CodeAtlas.Traits` to its dedicated `publish/` directory rather than pointing MCP clients at `bin/Debug/`. This prevents file-locking collisions when compiling or testing the repository.

Run the publish script from the repository root (`D:\Projects\CodeAtlas.Traits`):

```powershell
powershell -ExecutionPolicy Bypass -File "D:\Projects\CodeAtlas.Traits\scripts\publish.ps1"
```

This produces:
- **MCP Server Executable**: `D:\Projects\CodeAtlas.Traits\publish\server\CodeAtlas.Traits.Mcp.exe`
- **CLI Executable**: `D:\Projects\CodeAtlas.Traits\publish\cli\CodeAtlas.Traits.exe` (and `D:\Projects\CodeAtlas.Traits\publish\CodeAtlas.Traits.exe`)

---

## Step 3: Download the CPU-Only ONNX Semantic Model (Optional but Recommended)

`CodeAtlas.Traits` includes an instant zero-download 128-dim CPU feature-hashing vectoriser out of the box, and supports downloading the ~23 MB Int8 quantised **`all-MiniLM-L6-v2`** CPU ONNX model (`Microsoft.ML.OnnxRuntime`) into `%LOCALAPPDATA%\CodeAtlas.Traits\Models`:

```powershell
D:\Projects\CodeAtlas.Traits\publish\cli\CodeAtlas.Traits.exe model download
D:\Projects\CodeAtlas.Traits\publish\cli\CodeAtlas.Traits.exe model status
```

---

## Step 4: Register the MCP Server in Your AI Client

`CodeAtlas.Traits.Mcp.exe` runs in **Dynamic / Unbound Mode** by default when `args` is empty (`[]`), resolving the target repository dynamically from each tool call's `targetDirectory` argument or the active workspace directory.

### A. Antigravity / Gemini (`~/.gemini/config/mcp_config.json`)

Edit `C:\Users\<Username>\.gemini\config\mcp_config.json` and add `"codeatlas-traits"` alongside `"codeatlas"` under `"mcpServers"`:

```json
{
  "mcpServers": {
    "codeatlas": {
      "command": "D:\\Projects\\CodeAtlas\\publish\\server\\CodeAtlas.Mcp.exe",
      "args": []
    },
    "codeatlas-traits": {
      "command": "D:\\Projects\\CodeAtlas.Traits\\publish\\server\\CodeAtlas.Traits.Mcp.exe",
      "args": []
    }
  }
}
```

### B. GitHub Copilot / VS Code (`.vscode/mcp.json` or User Settings)

```json
{
  "servers": {
    "codeatlas-traits": {
      "type": "stdio",
      "command": "D:\\Projects\\CodeAtlas.Traits\\publish\\server\\CodeAtlas.Traits.Mcp.exe",
      "args": []
    }
  }
}
```

### C. Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "codeatlas-traits": {
      "command": "D:\\Projects\\CodeAtlas.Traits\\publish\\server\\CodeAtlas.Traits.Mcp.exe",
      "args": []
    }
  }
}
```

### D. Cursor (`.cursor/mcp.json`)

```json
{
  "mcpServers": {
    "codeatlas-traits": {
      "command": "D:\\Projects\\CodeAtlas.Traits\\publish\\server\\CodeAtlas.Traits.Mcp.exe",
      "args": []
    }
  }
}
```

### E. Bound Repository Mode (Optional)

If you want to lock an MCP server instance to a single repository by default, pass `-r <path>` in `args`:

```json
{
  "mcpServers": {
    "codeatlas-traits": {
      "command": "D:\\Projects\\CodeAtlas.Traits\\publish\\server\\CodeAtlas.Traits.Mcp.exe",
      "args": [
        "-r",
        "D:\\Work\\MyProject"
      ]
    }
  }
}
```

---

## Step 5: Verify the MCP Server Installation

AI companions can verify the MCP server directly from the terminal using a single-shot JSON-RPC `initialize` + `tools/list` + `tools/call` check over `stdin`/`stdout`, or by invoking `get_traits_status` via MCP:

### Option A: Verify via MCP Tool Call (When Connected)
Call `get_traits_status` with `targetDirectory` set to your repository root:
- Confirm `Status` is `"ok"` (or `"scanning_in_progress"` on a cold repository).
- Confirm `SemanticEngine` reports the active CPU model and `LoadedDictionaries` includes `en-core-dev`.

### Option B: Verify via CLI / Stdio Handshake Test
```powershell
# 1. Verify CLI status on a repository
D:\Projects\CodeAtlas.Traits\publish\cli\CodeAtlas.Traits.exe status -r "D:\Projects\CodeAtlas.Traits"

# 2. Verify MCP stdio server responds to JSON-RPC initialize & tools/list
$psi = [System.Diagnostics.ProcessStartInfo]::new("D:\Projects\CodeAtlas.Traits\publish\server\CodeAtlas.Traits.Mcp.exe")
$psi.RedirectStandardInput  = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.UseShellExecute        = $false
$proc = [System.Diagnostics.Process]::Start($psi)

$proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"setup-verify","version":"1.0.0"}}}')
$proc.StandardInput.Flush()
Write-Host $proc.StandardOutput.ReadLine()

$proc.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
$proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
$proc.StandardInput.Flush()
Write-Host $proc.StandardOutput.ReadLine()

$proc.StandardInput.Close()
$proc.WaitForExit(3000) | Out-Null
```

---

## Step 6: Available MCP Tools Summary

Once registered, the `codeatlas-traits` MCP server exposes **11 tools**:

| Tool Name | Purpose |
| :--- | :--- |
| **`get_traits_status`** | View scan progress, coverage stats, active CPU ONNX model, and loaded dictionaries. |
| **`get_top_tags`** | List the most frequently used single tags and recurring multi-trait combinations (`>= 3` traits). |
| **`search_traits`** | Search classes, methods, and functions by capability, behaviour, and technology tags + thesaurus synonyms. |
| **`find_similar_to_location`** | Rank methods/classes similar to `<Namespace.Class:Method>` or `<File:Line>` by `body_hash`, shared traits, CPU ONNX embeddings, and line count. |
| **`get_symbol_for_analysis`** | Fetch a symbol's source body, signature, `body_hash`, and existing traits for AI inspection. |
| **`tag_symbol`** | Directly insert or upgrade detailed `'ai'` behavioural/capability traits for a symbol (`Namespace.Class:Method` or `File:Line`). |
| **`get_untagged_batch`** | Retrieve a deduplicated batch of untagged (or baseline `'auto-cpu'`) symbols to enrich with detailed `'ai'` traits. |
| **`save_traits`** | Bulk-save detailed `'ai'` traits and summaries keyed by `body_hash`. |
| **`find_duplicate_bodies`** | Find exact structural duplicates sharing the same whitespace-normalised `body_hash`. |
| **`find_similar_traits`** | Find distinct implementations sharing `>= N` capability tags across the repository. |
| **`audit_diff_traits`** | Audit modified symbols in `git diff` for untagged methods and duplicate capabilities. |

For detailed CLI and MCP workflow examples, see [`USAGE.md`](USAGE.md).
