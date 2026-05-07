# Changelog — lash-lang

*Generated on 2026-05-07*

## 0.16.0 — 2026-05-06

### High Priority

- Serialize per-RID tool publishing in build script to avoid project-output races `bug`, `build`, `scripts`

### Changes

- cli: Add 'watch' command to cli
- compiler: rename/update warnings to be clearer


## 0.15.0 — 2026-04-07

### High Priority

- Add first-class associative map literals to Lash `feature`, `language`, `compiler`
- Add condition-only regex match operator to Lash `feature`, `language`, `compiler`
- Synchronize Lash docs with implemented language surface `feature`, `language`, `docs`

### Changes

- Add loop-depth operands for break and continue `feature`, `language`, `compiler`
- Add multi-pattern switch cases to Lash `feature`, `language`, `compiler`


## 0.14.0 — 2026-04-05

### High Priority

- Simplify preprocessor @if definition checks by removing defined() `feature`, `language`, `compiler`

### Changes

- Document simplified preprocessor @define/@if syntax `feature`, `language`, `docs`


## 0.13.0 — 2026-04-01

### High Priority

- Rename mutable/immutable bindings from let/const to var/let and simplify into bindings to bare names `feature`, `language`, `compiler`
- Make expression variable references use bare identifiers and reserve $ for expansion syntax `feature`, `language`, `compiler`


## 0.12.0 — 2026-03-03

### High Priority

- Redesign here-string syntax to use << and <<- with automatic heredoc lowering for multiline input `feature`, `language`, `compiler`


## 0.11.2 — 2026-03-03

### High Priority

- Split compile-time const from runtime readonly declarations `feature`, `language`, `compiler`
- Add process substitution operators <(...) and >(...) with direct Bash lowering `feature`, `language`, `compiler`
- Add arithmetic update and assignment operators (++, --, +=, -=, *=, /=, %=) `feature`, `language`, `compiler`
- Add arithmetic update and assignment operators (++, --, +=, -=, \*=, /=, %=) `feature`, `language`, `compiler`

### Changes

- Add wildcard switch case '_' pattern support `feature`, `language`, `compiler`
- Add interpolated multiline strings with compiler and parser support `feature`, `language`, `compiler`
- Add shebang diagnostics for missing or malformed shebangs `feature`, `compiler`, `diagnostics`
- Add non-mutating let warning with const suggestion `feature`, `compiler`, `diagnostics`
- Add wildcard switch case '\_' pattern support `feature`, `language`, `compiler`


## 0.10.1 — 2026-03-03

### Changes

- Improve warning name resolution to reduce false unused-symbol diagnostics `bug`, `compiler`, `diagnostics`
- Add fuzzy diagnostic suggestions for mistyped shell command options and similar typos `feature`, `compiler`, `diagnostics`
- Add discard identifier '_' support for bindings and loop variables `feature`, `language`, `compiler`
- Add discard identifier '\_' support for bindings and loop variables `feature`, `language`, `compiler`


## 0.9.0 — 2026-03-01

### High Priority

- Add shell-command registry for set/export/shopt/alias/source with AST-backed validation diagnostics `feature`, `language`, `compiler`
- Add @allow directive for next-statement warning suppression (Wxxx only) `feature`, `language`, `compiler`

### Changes

- Rename registered-command terminology to shell-command across AST, frontend registry, and tests `feature`, `compiler`
- Emit unused @allow warning (W509) when no targeted warning is matched `feature`, `compiler`, `diagnostics`
- Skip internal @allow compiler marker commands during Bash codegen `bug`, `compiler`, `codegen`


## 0.8.0 — 2026-02-28

### High Priority

- Make into bindings explicit: 'into ' assigns existing vars, 'into let/const name' creates vars `feature`, `language`

### Changes

- Add heredoc redirection and until loops to Lash with direct Bash lowering `feature`, `language`
- Use captured test expressions for file/dir checks in scripts/build/build.lash `bug`, `tooling`
- Add Bash bootstrap build script (scripts/build/build.sh) mirroring Lash build flags and output layout `feature`, `tooling`
- Add Bash bootstrap pack script (scripts/build/pack.sh) to archive release bundles into dist/*.tar.gz `feature`, `tooling`


## 0.7.0 — 2026-02-27

### Changes

- Add Lash LSP completion with keywords, symbols, directives, and core snippets `feature`, `lsp`
- Add safe rename and prepare-rename support to Lash LSP `feature`, `lsp`
- Improve Lash LSP hover docs for language tokens and symbols `feature`, `lsp`
- Harden Lash LSP local symbol resolution and add LSP test suite `feature`, `lsp`


## 0.6.0 — 2026-02-27

### Changes

- Add Lash LSP server with diagnostics, hover, and go-to-definition `feature`, `lsp`
- Refactor compiler analysis into a reusable API for editor tooling `feature`, `compiler`, `lsp`



