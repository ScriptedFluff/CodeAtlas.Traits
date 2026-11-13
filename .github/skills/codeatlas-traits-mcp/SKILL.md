---
name: codeatlas-traits-mcp
description: Capability, behaviour, and technology search & deduplication engine for CodeAtlas. Use BEFORE writing new utility/helper methods to discover existing reusable code by capability tags (e.g. 'retry', 'polly', 'sanitise', 'csv', 'compare'), and AFTER writing code to tag newly added symbols and audit git diffs for duplicate or overlapping helpers.
---

# CodeAtlas.Traits Skill Guide

`CodeAtlas.Traits` indexes classes, methods, and functions by **what they do** and **what technologies they use** rather than just their symbol names.

---

## 1. Mandatory Agent Workflow

### A. Before Writing Any Helper, Extension, or Infrastructure Logic
Before inventing a private helper method (such as `HashesMatch`, `EscapeCsv`, `SanitiseLogFragment`, or a custom retry loop):
1. Call **`search_traits`** with capability and language tags (e.g. `tags: "compare,string,c#"` or `tags: "retry,backoff,c#"`).
2. `search_traits` automatically expands synonyms via its built-in thesaurus (so searching `'sanitise'` matches `'escape'`, `'clean'`, `'scrub'`, etc.) and filters by language.
3. Reuse the returned shared extension or service instead of duplicating logic.

### B. After Finishing Code Changes (Post-Implementation Review & Cleanup)
1. Call **`audit_diff_traits`** to inspect all files modified in the current `git diff`:
   - If `UntaggedSymbolsInDiff` is non-empty, analyse those candidates and call **`save_traits`** with their `bodyHash`, `summary`, `isUtility`, and normalised `tags`.
   - Review `OverlappingSymbolsInDiff` (or call **`find_duplicate_bodies`** and **`find_similar_traits`**) to spot newly added methods that duplicate existing shared utilities, and consolidate them.

---

## 2. Handling `scanning_in_progress` (`completeOnlyResponse`)

By default, `CodeAtlas.Traits` tools run with `completeOnlyResponse: false`.
- If a repository has never been indexed before and the initial `CodeAtlas` scan takes longer than 1.5 seconds, the tool returns `"status": "scanning_in_progress"` immediately so you know the server is actively scanning and has not timed out.
- You may either continue with other work and check back, or immediately re-invoke the tool with **`completeOnlyResponse: true`** to wait until the scan finishes.
