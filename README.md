# CodeAtlas.Traits 🧭

> **Repository pattern memory, convention discovery, and consolidation for [CodeAtlas](https://github.com/ScriptedFluff/CodeAtlas).**
> **`CodeAtlas` gives the AI a map of the repository. `CodeAtlas.Traits` gives it examples of local building styles.**

> [!WARNING]
> **Alpha Stage — Volatile Codebase**: `CodeAtlas.Traits` is currently in active **Alpha** development. Its database schema (`traits.db`), CLI commands, MCP tool contracts, and internal APIs are still evolving rapidly and may change without notice.

- **Installation & MCP Setup**: See [`SETUP.md`](SETUP.md)
- **Full CLI & MCP Usage Guide**: See [`USAGE.md`](USAGE.md)

---

## 🎯 Why CodeAtlas.Traits? (The Map vs. Local Building Styles)

When an AI agent or a developer builds a feature in a large repository, symbol-name search alone isn't enough. A map tells you where existing classes are; local building styles tell you how new code is supposed to look so it feels like it belongs in the repository.

```text
CodeAtlas        (Repository Structural Memory) ──► Where is X? Who calls X? What depends on X?
CodeAtlas.Traits (Repository Pattern Memory)    ──► What smells similar? How do we normally structure this?
AI Assistant     (Reasoning & Intent)           ──► Reuse existing, extend it, or write new following local style
```

---

## 🔄 The Two AI Development Workflows

### Workflow 1 — Pre-Generation Pattern & Reuse Discovery (*"Before You Write"*)

Before implementing a new service or helper, query `search_traits` with the concepts involved. Even when none of the returned candidates is a drop-in match, the AI learns the repository's **local building style** in milliseconds instead of grepping the entire codebase:

```text
Need: Redis + SQL + Stored Procedure
                │
                ▼
          search_traits
                │
                ▼
  CustomerCacheRefresher (3/3)
  StoredProcRepository   (2/3)
  UserLookupCache        (1/3)
                │
                ▼
  Learn local building styles
(Folders, wrapping, naming, traits)
                │
                ▼
  Reuse  /  Extend  /  Write New
```

From a single call, the AI immediately sees:
1. **Whether a suitable implementation already exists** to reuse or extend.
2. **Where similar code lives** (e.g. `Infrastructure/Caching`, `Data/Procedures`).
3. **How the team structures it** (how Redis/SQL clients are wrapped, naming conventions, and companion patterns like cancellation tokens or retry policies).

### Workflow 2 — Post-Generation "Compiler Pass" (*Generate Fast → Prove It Works → Consolidate*)

Because AI lowers the cost of generating code, it is often faster to let the AI build a working feature first, verify that tests pass, and then run `CodeAtlas.Traits` as an **optimisation and consolidation pass** for repository health:

```text
Generate feature quickly ──► Verify functionality & tests ──► Run Traits audit ──► Consolidate overlaps
```

- **Semantic Overlap**: Surfaces methods with different names and implementations that solve the same underlying problem (e.g. `GetCustomerCached` vs. `LoadCustomerFromCache` vs. `RetrieveCustomerUsingRedis`).
- **Renamed & Structural Clones**: Catches copy-pasted methods with renamed variables and duplicated DTO/stub hierarchies across files.

---

## 🛠️ MCP Tools (`CodeAtlas.Traits.Mcp`)

| Tool | Workflow | Description |
| :--- | :--- | :--- |
| **`search_traits`** | **Workflow 1 (Discovery)** | Multi-concept pattern search ranked by concept overlap (`3/3` → `2/3` → `1/3`), returning candidate symbols and a **Local Building Styles** convention summary. |
| **`find_similar_to_location`** | **Workflows 1 & 2** | Finds symbols similar in size, semantic meaning, and capability traits to a target symbol (`MyNamespace.MyClass:MyMethod`) or location (`MyClass.cs:L35`). |
| **`audit_diff_traits`** | **Workflow 2 (Consolidate)** | Audits modified files from `git diff` for untagged symbols and capability overlaps with existing utilities and services. |
| **`find_similar_traits`** | **Workflow 2 (Consolidate)** | Detects semantic near-duplicates across the repository (distinct implementations sharing multiple capability/technology traits). |
| **`find_duplicate_bodies`** | **Workflow 2 (Consolidate)** | Detects exact body duplicates, Type-2 renamed clones, and file-level **Subsystem Duplication Clusters**. |
| **`get_top_tags`** | **Discovery & Health** | Lists the most frequently used capability/technology tags and recurring multi-trait combinations (`>= 3` shared traits). |
| **`get_untagged_batch`** | **Enrichment** | Retrieves untagged (or baseline auto-indexed) symbols—prioritising **High-Value Utility Islands** (`*Extensions*`, `*Utilities*`, `*Helpers*`) first—for AI trait enrichment. |
| **`get_symbol_for_analysis`** | **Enrichment** | Retrieves the source body, signature, `body_hash`, and current traits of a specific symbol (`MyNamespace.MyClass:MyMethod` or `MyClass.cs:L35`). |
| **`tag_symbol`** | **Enrichment** | Directly inserts or upgrades verified `'ai'` traits for a method or class by symbol name or `file:line` in one step. |
| **`save_traits`** | **Enrichment** | Persists a batch of verified `'ai'` traits, 1-sentence summaries, and architectural verdicts into `traits.db` and `.codeatlas/traits.ai.jsonl`. |
| **`get_traits_status`** | **Diagnostics** | Reports scan progress, coverage statistics, active CPU semantic model status, and loaded thesaurus dictionaries. |

---

## 💻 CLI Usage (`CodeAtlas.Traits.Cli`)

```bash
# Scan a repository (auto-triggers CodeAtlas if atlas.db is missing)
CodeAtlas.Traits scan <repo-path>

# Discover existing implementations & Local Building Styles by multi-concept query
CodeAtlas.Traits query -r <repo-path> --tags "redis, sql, stored procedure"
CodeAtlas.Traits query -r <repo-path> --tags "retry, polly, c#" --utility true

# Find symbols similar to a method/class by qualified name (or file:line)
CodeAtlas.Traits similar MyNamespace.MyClass:MyMethod
CodeAtlas.Traits similar MyClass.cs:L35

# List most used tags or recurring multi-trait combinations
CodeAtlas.Traits tags -r <repo-path> --combos --min-tags 3

# List untagged non-trivial symbols (prioritises High-Value Utility Islands first)
CodeAtlas.Traits untagged --lang csharp --limit 25
CodeAtlas.Traits untagged --lang csharp --utility --path "**/Extensions/**"

# Inspect or tag a method/class directly
CodeAtlas.Traits inspect MyNamespace.MyClass:MyMethod --json
CodeAtlas.Traits tag MyNamespace.MyClass:MyMethod --tags "retry,backoff,jitter,polly" --summary "Retries async operations with exponential backoff and jitter." --utility

# Find exact duplicate method bodies, renamed clones, and Subsystem Duplication Clusters
CodeAtlas.Traits duplicates --lang csharp

# Manage the local CPU semantic model & start the MCP stdio server
CodeAtlas.Traits model status
CodeAtlas.Traits model download
CodeAtlas.Traits serve --repo <repo-path>
```

---

## 🏗️ Under the Hood: Two-Tier Intelligence & Storage Boundary

- **Clean Storage Boundary**:
  - **`CodeAtlas` owns `.codeatlas/atlas.db`**: Deterministic AST structural ground truth.
  - **`CodeAtlas.Traits` owns `.codeatlas/traits.db`**: Behavioural traits, semantic vectors, and duplication clusters.
  - **Git-Trackable Team Memory (`.codeatlas/traits.ai.jsonl`)**: Verified `'ai'` traits are automatically exported to and re-imported from a deterministic `.codeatlas/traits.ai.jsonl` file so team conventions survive database rebuilds and branch switches.
- **Tier 1 — Zero-Cost Local CPU Recall**:
  - Computes content-addressable `body_hash` digests (hashing methods on executable bodies while hashing classes strictly on their public/protected contract skeleton) and Type-2 `structural_hash` digests (normalising variable/class names while preserving service calls).
  - Seeds deterministic structural traits (`accesses_redis`, `stored_procedure`, `database_transaction`, `has_retry_logic`, `uses_applock`, `cancellable_async`, `extension_method`) and computes local CPU ONNX semantic embeddings (`all-MiniLM-L6-v2` Int8) at zero API token cost.
- **Tier 2 — High-Level AI Refinement (MCP)**:
  - Connected AI companions (Antigravity / Gemini, GitHub Copilot, Claude) evaluate candidate clusters, distinguish intentional architectural contracts from actionable debt, and persist enriched `'ai'` traits back into the graph.
- **Timeout Guard (`completeOnlyResponse`)**:
  - Defaults to `completeOnlyResponse = false` so cold scans taking longer than 1.5s return `"status": "scanning_in_progress"` immediately while continuing in the background, ensuring AI agents never assume the server timed out.
