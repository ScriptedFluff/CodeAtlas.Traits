# CodeAtlas.Traits 🧭

> **The capability and behaviour layer for [CodeAtlas](https://github.com/ScriptedFluff/CodeAtlas). Index methods and classes by what they do, not just what they're named.**

---

## 🎯 Overview

AI agents and developers frequently invent duplicate private utility methods because AST symbol-name search fails when a capability has a different name in the existing codebase:
- Searching for `"Match"` misses `StdEquals`.
- Searching for `"Jitter"` misses `RetryWithBackoffAsync`.
- Grepping candidate files burns tens of thousands of LLM context tokens.

`CodeAtlas.Traits` works alongside `CodeAtlas` as an AI-agnostic MCP server and CLI tool:
- **Strict Separation**: `CodeAtlas` owns `.codeatlas/atlas.db`; `CodeAtlas.Traits` owns `.codeatlas/traits.db`. Neither touches the other's database.
- **Auto-Discovery & Auto-Indexing**: Locates `CodeAtlas` via its cross-platform `<LocalApplicationData>/CodeAtlas/install.json` beacon (or `CODEATLAS_PATH` / `PATH`) and automatically triggers `codeatlas index` if `atlas.db` does not exist yet.
- **Content-Addressable `body_hash`**:
  - **Methods & Functions**: Hashed on their whitespace-normalised AST byte slice (`StartByte..EndByte`). Survives full re-indexes, formatting edits, and file moves.
  - **Classes**: Hashed strictly on their contract skeleton (inheritance/interfaces, constructors, and non-private method signatures)—editing private implementation details never invalidates class traits.
- **Timeout Guard (`completeOnlyResponse`)**: Defaults to `completeOnlyResponse = false` so cold scans taking longer than 1.5s return `"status": "scanning_in_progress"` immediately while continuing in the background, ensuring AI agents never assume the server timed out.

---

## 🛠️ MCP Tools (`CodeAtlas.Traits.Mcp`)

| Tool | Description |
| :--- | :--- |
| **`search_traits`** | Search classes, methods, and functions by capability/technology tags (`"retry,polly,c#"`). Automatically normalises language tags (`"c#"`, `"typescript"`, `"powershell"`) to filter out non-matching language files. |
| **`get_untagged_batch`** | Retrieves a batch of untagged (or `'auto-cpu'` baseline-tagged when `includeAutoTagged = true`) non-trivial symbols for Gemini, GitHub Copilot, or Claude to enrich with detailed `'ai'` traits. |
| **`get_symbol_for_analysis`** | Retrieves the source body, signature, `body_hash`, and current tags of a specific symbol (`MyNamespace.MyClass:MyMethod` or `MyClass.cs:L35`) so Gemini or Copilot can analyse it. |
| **`save_traits`** | Persists a batch of AI-generated detailed tags, 1-sentence behavioural summaries, and `isUtility` classifications into `traits.db` keyed by `body_hash` (`tag_source = 'ai'`). |
| **`tag_symbol`** | Directly inserts or upgrades detailed `'ai'` traits for a method/class by symbol name (`MyNamespace.MyClass:MyMethod`) or location (`MyClass.cs:L35`) in one step. |
| **`find_duplicate_bodies`** | Detects exact structural/body duplicates (`GROUP BY body_hash HAVING COUNT(*) > 1`) across the repository for post-implementation cleanup. |
| **`find_similar_traits`** | Detects semantic near-duplicates (different `body_hash` sharing multiple capability/technology tags). |
| **`find_similar_to_location`** | Finds symbols similar in size, CPU ONNX semantic embedding, and capability/synonym traits to a target symbol (`MyNamespace.MyClass:MyMethod`, `MyClass:MyMethod`) or location (`MyClass.cs:L35`), auto-scanning and auto-tagging if needed. |
| **`audit_diff_traits`** | Audits modified files from `git diff` for untagged symbols and capability overlaps with existing utilities. |
| **`get_traits_status`** | Reports current scan progress, coverage statistics, active CPU ONNX semantic model status, and loaded thesaurus dictionaries. |

---

## 💻 CLI Usage (`CodeAtlas.Traits.Cli`)

```bash
# Scan a repository (auto-triggers CodeAtlas if atlas.db is missing)
CodeAtlas.Traits scan <repo-path>

# Query by capability, technology, and language tags
CodeAtlas.Traits query --tags "retry,polly,c#" --utility true

# Find symbols similar to a method/class by qualified name (or file:line)
CodeAtlas.Traits similar MyNamespace.MyClass:MyMethod
CodeAtlas.Traits similar MyClass:MyMethod
CodeAtlas.Traits similar MyClass.cs:L35

# Inspect a method/class so Gemini or Copilot can analyse its body and current traits
CodeAtlas.Traits inspect MyNamespace.MyClass:MyMethod --json

# Insert/upgrade detailed AI traits directly for a method or class
CodeAtlas.Traits tag MyNamespace.MyClass:MyMethod --tags "retry,backoff,jitter,polly" --summary "Retries async operations with exponential backoff and jitter." --utility

# Manage the CPU-only ONNX semantic model (all-MiniLM-L6-v2 Int8 ~23 MB)
CodeAtlas.Traits model status
CodeAtlas.Traits model download

# List untagged non-trivial symbols
CodeAtlas.Traits untagged --lang csharp --limit 25

# Find exact duplicate method bodies across the codebase
CodeAtlas.Traits duplicates --lang csharp

# Start the MCP stdio server
CodeAtlas.Traits serve --repo <repo-path>
```
