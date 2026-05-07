# Lash

Lash is a Lua-like scripting language that lowers directly to Bash.

This repository contains:

- `lash`: CLI entrypoint
- `lashc`: compiler (`.lash` -> `.sh`)
- `lashfmt`: formatter
- `lash-lsp`: language server

## Status

Lash is under active development. The current implemented surface is documented in:

- `docs/web/site/index.html` (interactive HTML docs)
- `docs/book/README.md` (markdown source for the user guide)
- `docs/language-spec.md` (reference)

Implemented language features include:

- mutable, immutable, readonly, and global declarations
- arrays and string-keyed map literals
- functions, enums, `switch`, and Bash-oriented control flow
- `for`, `select`, `while`, `until`, `break N`, and `continue N`
- raw shell command statements, `$(...)`, process substitution, redirections, traps, background jobs, and `coproc`

## Requirements

- .NET SDK (`net10.0`)
- Bash

## Quick Start

Build interactive HTML docs:

```bash
just docs-web-build
```

Open the web docs:

```bash
xdg-open docs/web/site/index.html
```

Read the markdown book source:

```bash
cat docs/book/README.md
```

Preview interactive docs locally:

```bash
just docs-web-preview
```

Run a Lash script:

```bash
lash run script.lash arg1 arg2
```

Check a script without emitting Bash:

```bash
lash check script.lash
```

Compile a script to Bash:

```bash
lash compile script.lash -o script.sh
bash script.sh
```

Format Lash files:

```bash
lash format .
lash format . --check
```

Watch Lash files and recompile on changes:

```bash
lash watch script.lash
lash watch src/
```

## CLI Commands

From `lash --help`:

- `compile <file>`: compile `.lash` to Bash (`-o/--output` optional)
- `check <file>`: validate a `.lash` file without emission
- `format <paths>...`: format files/directories (`--check` supported)
- `run <file> [args...]`: compile to temp Bash and execute
- `watch <paths>...`: watch `.lash` files/directories and recompile changed files

Use `--verbose` on commands for extra phase and progress logs.
