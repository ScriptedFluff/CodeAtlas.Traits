# CodeAtlas.Traits 🧭

> **Repository pattern memory, convention discovery, and consolidation for [CodeAtlas](https://github.com/ScriptedFluff/CodeAtlas).**
> **`CodeAtlas` gives the AI a map of the repository. `CodeAtlas.Traits` gives it examples of local building styles.**

> [!WARNING]
> **Alpha Stage — Volatile Codebase**: `CodeAtlas.Traits` is currently in active **Alpha** development. Its database schema (`traits.db`), CLI commands, MCP tool contracts, and internal APIs are still evolving rapidly and may change without notice.

- **Installation & MCP Setup**: See [`SETUP.md`](SETUP.md)
- **Full CLI & MCP Usage Guide**: See [`USAGE.md`](USAGE.md)

---

## 🎯 Why CodeAtlas.Traits? (The Map vs. Local Building Styles)

When an AI agent or a new developer needs to build a feature in an unfamiliar repository, symbol-name search alone isn't enough:
- **`CodeAtlas` (Structural Knowledge — The Map)**: Answers *"Where is X? Who calls X? What depends on X?"*
- **`CodeAtlas.Traits` (Pattern & Convention Knowledge — Local Building Styles)**: Answers *"What existing code smells similar enough that you should look at it before writing something new? Where does code like this live, and how does this team normally structure it?"*

### The Two AI Development Workflows

1. **Workflow 1 — Pre-Generation Pattern & Reuse Discovery (*"Before You Write"*)**:
   - Ask `search_traits` for a combination of concepts (e.g. `"redis, sql, stored procedure"`).
   - `CodeAtlas.Traits` returns ranked candidates (`CustomerCacheRefresher`, `StoredProcRepository`, `UserLookupCache`) along with a **Local Building Styles** summary (common directories, class naming conventions, and companion traits).
   - The AI can then:
     1. **Reuse** an existing implementation directly,
     2. **Extend** an existing service or helper, or
     3. **Write a new implementation** that mirrors the repository's local folder structure, dependency wrapping, and naming conventions—so new code feels like it belongs in the repository.
2. **Workflow 2 — Post-Generation "Compiler Pass" (*Generate Fast → Prove It Works → Consolidate*)**:
   - Because AI makes code generation cheap, let the AI build the feature rapidly and verify that tests pass first.
   - Then run `audit_diff_traits`, `find_similar_to_location`, and `find_duplicate_bodies` as an **optimisation and consolidation pass**—catching semantic overlap (`GetCustomerCached` vs. `LoadCustomerFromCache` vs. `RetrieveCustomerUsingRedis`), renamed Type-2 clones, and duplicated subsystem stubs before merging.

---

## 🏗️ Architecture: Two-Tier Intelligence Model

- **Tier 1 — Fast Local CPU Recall**: Pulls AST symbols from `CodeAtlas`, computes content-addressable `body_hash` and Type-2 `structural_hash` values, seeds zero-cost structural traits (`accesses_redis`, `stored_procedure`, `database_transaction`, `has_retry_logic`, `uses_applock`, `cancellable_async`, `extension_method`), and indexes CPU-only ONNX semantic embeddings (`all-MiniLM-L6-v2` Int8) at zero API token cost.
- **Tier 2 — High-Level AI Refinement (MCP)**: Connected AI companions (Antigravity / Gemini, GitHub Copilot, Claude) evaluate candidate clusters, enrich methods with behavioural traits (`tag_source = 'ai'`), and gatekeep newly authored code against existing utilities and subsystem patterns.
- **Strict Separation & Git-Trackable Memory**: `CodeAtlas` owns `.codeatlas/atlas.db` (deterministic AST ground truth); `CodeAtlas.Traits` owns `.codeatlas/traits.db` and automatically mirrors verified `'ai'` traits to a Git-trackable `.codeatlas/traits.ai.jsonl` file.
- **Timeout Guard (`completeOnlyResponse`)**: Defaults to `completeOnlyResponse = false` so cold scans taking longer than 1.5s return `"status": "scanning_in_progress"` immediately while continuing in the background, ensuring AI agents never assume the server timed out.

---

## 🛠️ MCP Tools (`CodeAtlas.Traits.Mcp`)

| Tool | Description |
| :--- | :--- |
| **`search_traits`** | **Workflow 1 (Pattern Discovery)**: Search classes, methods, and functions by multi-concept traits (`"redis, sql, stored procedure"`), ranked by concept overlap (`3/3` -> `2/3` -> `1/3`), returning candidate symbols and a **Local Building Styles** convention summary. |
| **`get_top_tags`** | Lists the most frequently used capability/technology tags and recurring multi-trait combinations (`>= 3` shared traits) across the repository. |
| **`get_untagged_batch`** | Retrieves a batch of untagged (or `'auto-cpu'` baseline-tagged) non-trivial symbols—prioritising **High-Value Utility Islands** (`*Extensions*`, `*Utilities*`, `*Helpers*`) first—for Gemini, Copilot, or Claude to enrich with `'ai'` traits. |
| **`get_symbol_for_analysis`** | Retrieves the source body, signature, `body_hash`, and current tags of a specific symbol (`MyNamespace.MyClass:MyMethod` or `MyClass.cs:L35`) so Gemini or Copilot can analyse it. |
| **`save_traits`** | Persists a batch of AI-generated detailed tags, 1-sentence behavioural summaries, `isUtility` flags, and semantic verdicts into `traits.db` and `.codeatlas/traits.ai.jsonl` (`tag_source = 'ai'`). |
| **`tag_symbol`** | Directly inserts or upgrades detailed `'ai'` traits for a method/class by symbol name (`MyNamespace.MyClass:MyMethod`) or location (`MyClass.cs:L35`) in one step. |
| **`find_duplicate_bodies`** | **Workflow 2 (Consolidation)**: Detects exact body duplicates, Type-2 renamed clones (`structural_hash`), and file-level **Subsystem Duplication Clusters**. |
| **`find_similar_traits`** | **Workflow 2 (Consolidation)**: Detects semantic near-duplicates (different `body_hash` solving a similar problem with overlapping capability/technology tags). |
| **`find_similar_to_location`** | **Workflows 1 & 2**: Finds symbols similar in size, CPU ONNX semantic embedding, and capability/synonym traits to a target symbol (`MyNamespace.MyClass:MyMethod`) or location (`MyClass.cs:L35`). |
| **`audit_diff_traits`** | **Workflow 2 (Consolidation)**: Audits modified files from `git diff` for untagged symbols and capability overlaps with existing utilities. |
| **`get_traits_status`** | Reports current scan progress, coverage statistics, active CPU ONNX semantic model status, and loaded thesaurus dictionaries. |

---

## 💻 CLI Usage (`CodeAtlas.Traits.Cli`)

```bash
# Scan a repository (auto-triggers CodeAtlas if atlas.db is missing)
CodeAtlas.Traits scan <repo-path>

# List most used tags or recurring multi-trait combinations
CodeAtlas.Traits tags -r <repo-path>
CodeAtlas.Traits tags -r <repo-path> --combos --min-tags 3

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

# List untagged non-trivial symbols (prioritises High-Value Utility Islands first)
CodeAtlas.Traits untagged --lang csharp --limit 25
CodeAtlas.Traits untagged --lang csharp --utility --path "**/Extensions/**"

# Find exact duplicate method bodies and Subsystem Duplication Clusters
CodeAtlas.Traits duplicates --lang csharp

# Start the MCP stdio server
CodeAtlas.Traits serve --repo <repo-path>
```
