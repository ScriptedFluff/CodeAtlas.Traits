# CodeAtlas.Traits — Usage Guide (`USAGE.md`)

`CodeAtlas.Traits` indexes classes, methods (including `private` helpers), and functions by **what they do** (capability, behaviour, and technology tags + CPU semantic embeddings) rather than just their identifier names.

It works alongside [`CodeAtlas`](https://github.com/ScriptedFluff/CodeAtlas):
- `CodeAtlas` owns `<repo>/.codeatlas/atlas.db` (AST symbol graph & call references).
- `CodeAtlas.Traits` owns `<repo>/.codeatlas/traits.db` (content-addressable `body_hash` traits, tags, and CPU ONNX embeddings).

---

## 1. Target Reference Syntax

Wherever a command or MCP tool accepts a `<target>`, you can reference a method or class in any of the following formats:

| Format | Example | Description |
| :--- | :--- | :--- |
| **`Namespace.Class:Method`** | `MyNamespace.MyClass:MyMethod` | Fully qualified namespace, class, and method name (recommended). |
| **`Class:Method`** | `MyClass:MyMethod` | Short class and method name (or partial namespace suffix). |
| **`Namespace.Class.Method`** | `MyNamespace.MyClass.MyMethod` | Dot-qualified symbol reference. |
| **`File:Line`** | `MyClass.cs:L35` or `src/MyClass.cs:35` | Resolves the innermost symbol spanning line `35` of `MyClass.cs`. |

---

## 2. CLI Commands (`CodeAtlas.Traits`)

### 2.1 Scan a Repository
Synchronises symbols from `CodeAtlas` (automatically running `codeatlas index` if `atlas.db` does not exist yet) and computes content-addressable `body_hash` values into `.codeatlas/traits.db`:
```powershell
CodeAtlas.Traits scan D:\Projects\MyRepo
CodeAtlas.Traits scan D:\Projects\MyRepo --json
```

### 2.2 List Most Used Tags & Multi-Trait Combinations (`tags`)
Lists the most frequently used single tags (default) or recurring **multi-trait combinations** (`--combos` / `-c` and `--min-tags 3`) across non-trivial symbols in the repository:
```powershell
# Top 50 most used single tags in the repository
CodeAtlas.Traits tags -r D:\Projects\MyRepo

# Top 25 recurring multi-trait combinations (symbols sharing >= 4 traits) in C#
CodeAtlas.Traits tags -r D:\Projects\MyRepo --combos --min-tags 4 --lang csharp --limit 25

# Output JSON
CodeAtlas.Traits tags -r D:\Projects\MyRepo --combos --min-tags 3 --json
```

### 2.3 Search by Capability, Technology & Language Tags (`query`)
Searches tagged symbols using the built-in synonym thesaurus (e.g. querying `sanitise` also matches `escape`, `clean`, `scrub`, and US/UK spelling variants). Language tags (`c#`, `typescript`, `powershell`) automatically filter by file language:
```powershell
# Find reusable retry utilities in C#
CodeAtlas.Traits query --tags "retry,backoff,c#" --utility true

# Output JSON for scripts or AI agents
CodeAtlas.Traits query --tags "csv,sanitise" --lang csharp --json
```

### 2.3 Find Similar Methods or Classes (`similar`)
Checks whether the target symbol has been scanned and tagged (automatically scanning and baseline-tagging via the CPU engine if needed), then ranks similar symbols across the codebase by:
1. **Exact `body_hash` match** (`100%` — copy-paste duplicates with different names).
2. **Shared capability & synonym concepts** (`55%` weight).
3. **CPU ONNX semantic embedding cosine similarity** (`25%` weight).
4. **Line-count size similarity** (`20%` weight).

```powershell
# By Namespace.Class:Method (recommended)
CodeAtlas.Traits similar MyNamespace.MyClass:MyMethod

# By Class:Method
CodeAtlas.Traits similar OrderService:HashesMatch

# By File:Line
CodeAtlas.Traits similar MyClass.cs:L35

# JSON output with custom limit
CodeAtlas.Traits similar MyNamespace.MyClass:MyMethod --limit 10 --json
```

### 2.4 Inspect & Tag Symbols with Detailed AI Traits (`inspect` & `tag`)
While the local CPU engine automatically generates baseline tags (`tag_source = 'auto-cpu'`), **Gemini**, **GitHub Copilot**, or **Claude** can inspect a method/class and insert rich, detailed traits (`tag_source = 'ai'`). Detailed `'ai'` traits are protected and never overwritten by `'auto-cpu'` scans.

```powershell
# 1. Inspect a method's source code, signature, body_hash, and current traits
CodeAtlas.Traits inspect MyNamespace.MyClass:MyMethod
CodeAtlas.Traits inspect MyNamespace.MyClass:MyMethod --json

# 2. Insert or upgrade detailed AI traits directly for a symbol
CodeAtlas.Traits tag MyNamespace.MyClass:MyMethod `
  --tags "retry,exponential-backoff,jitter,polly,resilience" `
  --summary "Executes an asynchronous operation with Polly exponential backoff and random jitter." `
  --utility
```

### 2.5 List Untagged / Baseline-Tagged Symbols (`untagged`)
Lists non-trivial symbols awaiting detailed AI tagging (deduplicated by `body_hash`). By default, **High-Value Utility Islands** (`*Extensions*`, `*Utilities*`, `*Helpers*`, `*Common*`, and non-test `static` methods) are prioritised first and ordered by cognitive statement weight:
```powershell
CodeAtlas.Traits untagged --lang csharp --limit 25
CodeAtlas.Traits untagged --lang csharp --utility
CodeAtlas.Traits untagged --lang csharp --path "**/Extensions/**"
CodeAtlas.Traits untagged --lang csharp --json
```

### 2.6 Find Exact Structural Duplicates & Subsystem Clusters (`duplicates`)
Finds non-trivial methods, functions, or class skeletons across the repository that share the exact same whitespace-normalised `body_hash` (including `private` helper methods), and automatically rolls up sibling file-pair collisions into **Subsystem Duplication Clusters**:
- By default, interface/abstract contract declarations without executable bodies (`has_executable_body = 0`) are excluded (pass `--include-contracts` to inspect contract signature duplicates).
- Standalone 1-statement / 1-property short boilerplate (`<= 5` lines) is suppressed unless it belongs to a multi-symbol Subsystem Cluster (customisable via `--min-statements`).
- Respects `.codeatlas/settings.json` `ignorePatterns` (e.g. `"**/*Tests/**/*"`).
```powershell
CodeAtlas.Traits duplicates --lang csharp -r D:\Projects\MyRepo
CodeAtlas.Traits duplicates --lang csharp --include-contracts --min-statements 1
CodeAtlas.Traits duplicates --json
```

### 2.7 Manage the CPU-Only ONNX Semantic Model (`model`)
`CodeAtlas.Traits` runs 100% on CPU with zero GPU or external daemon required:
- Out of the box, it includes a deterministic **128-dim CPU Feature-Hashing + Thesaurus Subspace Vectoriser**.
- You can optionally download the ~23 MB Int8 quantised **`all-MiniLM-L6-v2`** CPU ONNX model (`Microsoft.ML.OnnxRuntime`) into `<LocalApplicationData>/CodeAtlas.Traits/Models`:

```powershell
# Check active CPU semantic engine status
CodeAtlas.Traits model status

# Download the ~23 MB quantised all-MiniLM-L6-v2 CPU ONNX model + vocab.txt
CodeAtlas.Traits model download
```

### 2.8 Check Coverage & Status (`status`)
```powershell
CodeAtlas.Traits status
CodeAtlas.Traits status --json
```

---

## 3. MCP Server Usage (Gemini, GitHub Copilot, Claude, Cursor)

### 3.1 Registering the MCP Server
See [`SETUP.md`](SETUP.md) for full installation instructions. After publishing (`scripts/publish.ps1`), point your MCP client configuration to `publish\server\CodeAtlas.Traits.Mcp.exe`:

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

### 3.2 Available MCP Tools

| MCP Tool | Purpose | Key Parameters |
| :--- | :--- | :--- |
| **`get_top_tags`** | List the most frequently used capability and technology tags in the repository. | `language`, `symbolType`, `onlyUtilities`, `limit` (default `50`) |
| **`search_traits`** | Search existing utilities/methods before writing new helpers. | `tags` (`"retry,polly,c#"`), `onlyUtilities`, `matchAllTags`, `useThesaurus` |
| **`find_similar_to_location`** | Find duplicates/similar methods to a target symbol or `file:line`. | `location` (`"MyNamespace.MyClass:MyMethod"` or `"MyClass.cs:L35"`), `limit` |
| **`get_symbol_for_analysis`** | Fetch a symbol's source body, `body_hash`, and current tags for AI analysis. | `target` (`"MyNamespace.MyClass:MyMethod"` or `"MyClass.cs:L35"`) |
| **`tag_symbol`** | Directly insert/upgrade detailed `'ai'` traits for a target symbol in one step. | `target`, `tags`, `summary`, `isUtility` |
| **`get_untagged_batch`** | Pull a batch of untagged or `'auto-cpu'` symbols to enrich with detailed `'ai'` traits. | `language`, `batchSize`, `includeAutoTagged` (default `true`) |
| **`save_traits`** | Save a batch of detailed `'ai'` traits keyed by `body_hash`. | `entries` (`[{ bodyHash, summary, isUtility, tags }]`) |
| **`find_duplicate_bodies`** | Detect exact `body_hash` duplicates across the codebase. | `language`, `limit` |
| **`find_similar_traits`** | Detect different implementations sharing `>= N` capability tags. | `language`, `minSharedTags` (default `3`), `limit` |
| **`audit_diff_traits`** | Audit modified files in `git diff` for untagged symbols and duplicate capabilities. | `baseRef`, `stagedOnly` |
| **`get_traits_status`** | View scan progress, coverage stats, CPU ONNX model status, and loaded dictionaries. | `language`, `forceRescan` |

> **Timeout Guard (`completeOnlyResponse`)**: All scan-backed MCP tools default to `completeOnlyResponse: false`. If a cold repository scan takes longer than `1.5s`, the tool immediately returns `"status": "scanning_in_progress"` while continuing in the background. Pass `completeOnlyResponse: true` to wait synchronously for completion.

---

## 4. Recommended AI Workflows

### Workflow A: Preventing Duplicate Helpers Before Writing Code
1. Before creating a private helper or utility (e.g. string comparison, CSV escaping, retry with jitter), call **`search_traits`** (`tags: "string,compare,c#"`, `onlyUtilities: true`).
2. Reuse the returned shared extension/service method instead of writing a duplicate private helper.

### Workflow B: Checking a Specific Method for Duplicates
1. Run **`find_similar_to_location`** with `location: "MyNamespace.MyClass:MyMethod"` (or CLI `CodeAtlas.Traits similar MyNamespace.MyClass:MyMethod`).
2. Review any exact `body_hash` duplicates or high-scoring capability/embedding matches.

### Workflow C: Enriching Traits with Gemini or GitHub Copilot
1. **Single symbol**: Call **`get_symbol_for_analysis`** (`target: "MyNamespace.MyClass:MyMethod"`), analyse the method's `ContentToAnalyse`, and call **`tag_symbol`** with detailed capability/technology tags and a 1-sentence behavioural summary.
2. **Batch enrichment**: Call **`get_untagged_batch`** (`includeAutoTagged: true`, `batchSize: 25`), analyse the returned candidates, and submit them in a single **`save_traits`** call.

---

## 5. Custom & Multi-Language Thesaurus Packs (`Dictionaries/*.json`)

`CodeAtlas.Traits` ships with a built-in English developer thesaurus (`Dictionaries/en.json`) and automatically merges any `*.json` dictionary files dropped into:
1. `<AppBaseDirectory>/Dictionaries/`
2. `<LocalApplicationData>/CodeAtlas.Traits/Dictionaries/`
3. `CODEATLAS_TRAITS_DICTIONARIES_DIR` (environment variable)
4. `<repo>/.codeatlas/dictionaries/` (workspace-specific terminology)

Example custom dictionary pack (`.codeatlas/dictionaries/custom.json`):
```json
{
  "id": "team-domain",
  "language": "en",
  "name": "Team Domain Synonyms",
  "version": "1.0.0",
  "spellingNormalisations": [
    { "find": "ize", "replace": "ise" }
  ],
  "synonymGroups": [
    ["sanitise", "escape", "scrub", "clean", " redact"],
    ["retry", "backoff", "jitter", "polly", "resilience"]
  ]
}
```

---

## 6. Workspace Filtering Configuration (`.codeatlas/settings.json`)

`CodeAtlas.Traits` shares `.codeatlas/settings.json` (and `appsettings.json`) with `CodeAtlas`. You can exclude test projects, generated files, or specific folders from scanning and duplicate/trait queries using `ignorePatterns`, `ignoreDirectories`, or `includePatterns`:

```json
{
  "ignorePatterns": [
    "**/*Tests/**/*",
    "**/*.Generated.cs"
  ],
  "ignoreDirectories": [
    "Migrations",
    "wwwroot/lib"
  ]
}
```

---

## 7. Git-Trackable AI Traits Sync (`.codeatlas/traits.ai.jsonl`)

While `.codeatlas/traits.db` is a local SQLite database (typically ignored by Git), `CodeAtlas.Traits` automatically mirrors all verified `tag_source = 'ai'` traits to a compact, deterministic, sorted `.codeatlas/traits.ai.jsonl` file keyed by `body_hash`.
- **Automatic Export**: Whenever `save_traits` or `tag_symbol` saves a verified `'ai'` trait, `.codeatlas/traits.ai.jsonl` is updated automatically.
- **Automatic Re-Import**: Whenever `traits.db` is opened or migrated on a new machine (or after deleting `traits.db`), any `'ai'` entries in `.codeatlas/traits.ai.jsonl` are automatically imported into `traits.db`.
- Commit `.codeatlas/traits.ai.jsonl` to Git so your team and CI pipelines share verified semantic traits and architectural verdicts without re-spending LLM tokens.
