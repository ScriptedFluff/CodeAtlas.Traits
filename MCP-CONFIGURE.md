# CodeAtlas.Traits — MCP Configuration Guide

This guide details how to configure `CodeAtlas.Traits.Mcp` as a Model Context Protocol (MCP) server alongside `CodeAtlas` in **Antigravity / Gemini**, **GitHub Copilot (VS Code / Visual Studio)**, **Cursor**, and **Claude Desktop**.

---

## 1. Build the Server & CLI

```powershell
dotnet build D:\Projects\CodeAtlas.Traits\CodeAtlas.Traits.slnx -c Release
```

The compiled executables will be located at:
- **MCP Server**: `D:\Projects\CodeAtlas.Traits\src\CodeAtlas.Traits.Mcp\bin\Release\net10.0\CodeAtlas.Traits.Mcp.exe`
- **CLI Tool**: `D:\Projects\CodeAtlas.Traits\src\CodeAtlas.Traits.Cli\bin\Release\net10.0\CodeAtlas.Traits.exe`
- **Built-in Dictionary Pack**: `D:\Projects\CodeAtlas.Traits\src\CodeAtlas.Traits.Mcp\bin\Release\net10.0\Dictionaries\en.json`

---

## 2. Client Configuration Examples

### A. Antigravity / Gemini AI (`mcp_config.json`)

```json
{
  "mcpServers": {
    "codeatlas-traits": {
      "command": "D:\\Projects\\CodeAtlas.Traits\\src\\CodeAtlas.Traits.Mcp\\bin\\Release\\net10.0\\CodeAtlas.Traits.Mcp.exe",
      "args": []
    }
  }
}
```

### B. GitHub Copilot in VS Code (`.vscode/mcp.json`)

```json
{
  "servers": {
    "codeatlas-traits": {
      "type": "stdio",
      "command": "D:\\Projects\\CodeAtlas.Traits\\src\\CodeAtlas.Traits.Mcp\\bin\\Release\\net10.0\\CodeAtlas.Traits.Mcp.exe",
      "args": []
    }
  }
}
```

### C. Cursor (`.cursor/mcp.json`)

```json
{
  "mcpServers": {
    "codeatlas-traits": {
      "command": "D:\\Projects\\CodeAtlas.Traits\\src\\CodeAtlas.Traits.Mcp\\bin\\Release\\net10.0\\CodeAtlas.Traits.Mcp.exe",
      "args": []
    }
  }
}
```

---

## 3. Drop-In Thesaurus / Dictionary Packs

To add another human language (e.g. `fr.json`, `de.json`, `es.json`) or domain-specific vocabulary (e.g. `clever-adviser.json`), drop a `.json` file into any of the following folders:
1. **Project-Local**: `<repo>/.codeatlas/dictionaries/*.json`
2. **User-Global**: `%LOCALAPPDATA%\CodeAtlas.Traits\Dictionaries\*.json` (Windows) or `~/.local/share/CodeAtlas.Traits/Dictionaries/*.json` (Linux/macOS)
3. **Application Directory**: `<AppBaseDirectory>/Dictionaries/*.json`
