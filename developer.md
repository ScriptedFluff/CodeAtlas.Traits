# CodeAtlas.Traits — Developer & Architecture Guide

`CodeAtlas.Traits` is the asynchronous, AI-agnostic capability and technology indexing companion for [CodeAtlas](https://github.com/ScriptedFluff/CodeAtlas).

---

## 1. Coding Style Mandate: Cognitive Code Structure ("Save the Reader's Neurons")

Whenever writing or refactoring code (C#, T-SQL, TypeScript, PowerShell), structure every file and routine to minimise human cognitive load. A human reading any stored procedure, function, or method must be able to see what state exists, what the preconditions are, and what the routine's intent is before reaching the bottom of each chunk.

### 1.1 C-Style Top-of-Scope Variable Declarations
Declare all local working variables at the very top of their scope (stored procedure `BEGIN`, method body, or major block), grouped and commented by purpose—never scattered mid-stream right before first use.
- **T-SQL**: Place a single `DECLARE` block at the very top of `BEGIN`, grouped into visual subsections (e.g. *Outcome & Receipt State*, *Lock & Coordination State*, *Evaluation State*) with aligned columns for variable names, types, and default values.
- **C# / TypeScript / PowerShell**: Declare routine-level working variables at the top of the method body before the precondition and execution sections.
- **Only permitted inline declarations**: loop induction variables (`for (var i = 0; ...)`, `foreach (var item in items)`), pattern-matched guard variables (`if (x is Foo foo)`), and `using var` resource scopes that immediately wrap the remaining block.

### 1.2 Four-Part Routine Anatomy
Every non-trivial stored procedure, function, or method MUST be structured into four visually distinct, top-to-bottom sections:
1. **Declarations (Top of Scope)**: What state exists in this routine?
2. **Preconditions & Guard Clauses**: What must be true before we start? Group all parameter validation, null/empty checks, and guard clauses at the entry gate—never interleave them between business steps.
3. **Intent-First Narrative (Single Level of Abstraction)**: What are the 3–6 high-level steps? Top-level orchestrators (`Run`, `Handle`, `QueueAsync`, `ExecuteAsync`, `usp_*`) must read like a linear table of contents (20–45 lines) stating *what* happens, delegating *how* it happens to focused private helpers below.
4. **Output / Return**: Clean assembly and return of the result.

### 1.3 Zero Interleaved Plumbing
- **No Nested `try/catch` Inside Loops or Orchestrators**: Extract retry loops, fallback parsing, best-effort logging, or transaction rollback suppression into small, single-purpose private helper methods (`ExecuteOnceAsync`, `TryRollbackQuietly`).
- **No Loose Boolean Flag Soup**: If a workflow tracks 3+ mutable coordination values across steps, encapsulate them in a small private sealed class or record (`BatchExecutionState`) instead of mutating loose `bool` flags.
- **No Wide Anonymous Tuples**: Never return an anonymous tuple with more than 2–3 elements; define a named private sealed record instead.
- **Zero `GOTO` Statements**: Never use `GOTO` in T-SQL or C#. Use structured `IF ... ELSE BEGIN ... END` phases or helper subroutines so control flow always moves top-to-bottom.

### 1.4 No Domain-Disguised Generic Utilities or Re-Invented Infrastructure
- Never bloat domain services, controllers, or functions with private helpers bearing domain-flavoured names (`HashesMatch`, `EscapeCsv`, `SanitiseLogFragment`, `DelayWithJitterAsync`) or manual retry loops when their body is purely a generic string, collection, primitive, or retry operation.
- Search for and reuse existing shared services (e.g. `IRetryService.RetryAsync` / `RetryWithBackoffAsync`) and shared extension classes (`*.Extensions.StringExtensions`) first.
- If a genuinely new general-purpose primitive/string/collection helper is needed, place it in a shared `*Extensions.cs` class with a general name (e.g. `StringExtensions.Match`) rather than hiding it as a private method inside a feature class.

### 1.5 Language & Naming Conventions
- Use **British English** across all identifiers, comments, XML docs, and user-facing messages (`Sanitise`, `Authorise`, `Cancelled`, `Normalise`, `Synchronise`, `Behaviour`, `Catalogue`).

---

## 2. Architectural Principles

1. **Strict Separation of Databases**:
   - `CodeAtlas` owns `<repo>/.codeatlas/atlas.db`.
   - `CodeAtlas.Traits` owns `<repo>/.codeatlas/traits.db`.
   - Neither project reads or writes the other's SQLite file directly; `CodeAtlas.Traits` invokes the `CodeAtlas` CLI via [`CodeAtlasCliClient`](src/CodeAtlas.Traits.Core/AtlasBridge/CodeAtlasCliClient.cs).

2. **Cross-Platform Discovery (`install.json`)**:
   - Whenever `CodeAtlas` runs, it writes its CLI binary path to `<LocalApplicationData>/CodeAtlas/install.json`.
   - [`CodeAtlasLocator`](src/CodeAtlas.Traits.Core/AtlasBridge/CodeAtlasLocator.cs) resolves `CodeAtlas` via:
     1. `CODEATLAS_PATH` environment variable
     2. `<LocalApplicationData>/CodeAtlas/install.json`
     3. System `PATH`
     4. Sibling dev folder (`../CodeAtlas`)

3. **Content-Addressable `body_hash`**:
   - **Methods / Functions**: Hashed on their whitespace-normalised AST byte slice (`StartByte..EndByte`).
   - **Classes**: Hashed strictly on their **contract skeleton** (inheritance/signature, constructors, and non-private method signatures) so editing private methods never invalidates class traits.

4. **Drop-In Dictionary / Thesaurus Packs (`Dictionaries/*.json`)**:
   - Zero hardcoded language rules in C#.
   - [`ThesaurusEngine`](src/CodeAtlas.Traits.Core/Thesaurus/ThesaurusEngine.cs) merges `*.json` packs across App (`Dictionaries/en.json`), User (`<LocalApplicationData>/CodeAtlas.Traits/Dictionaries`), Environment (`CODEATLAS_TRAITS_DICTIONARIES_DIR`), and Workspace (`.codeatlas/dictionaries`).
