# 1. Getting Started

Lash is a Lua-like scripting language that compiles to Bash.

The point is not to hide Bash. The point is to keep shell programs readable while adding structure, checks, and a few higher-level conveniences.

## Your first script

```lash
#!/usr/bin/env -S lash run

set -euo pipefail

let name = "Lash"
echo $"Hello from {name}"
```

Run it:

```bash
lash run script.lash
```

## The edit loop

Use `check` before you run a script. It catches parser, name, type, flow, and code generation problems before Bash sees the output.

```bash
lash check script.lash
lash run script.lash
lash compile script.lash -o script.sh
bash script.sh
```

Use `format` when editing larger files:

```bash
lash format script.lash
lash format . --check
```

## File shape

- Lash files usually end in `.lash`.
- A leading shebang is allowed and stripped before parsing.
- Line comments use `//`.
- Block comments use `/* ... */`.

## Mental model

- Lash parses your file and builds an AST.
- Semantic passes validate declarations, functions, loops, constants, and codegen feasibility.
- Code generation emits Bash.
- Shell commands still run as shell commands at runtime.

That last point matters: Lash gives you structure and earlier errors, but the target language is still Bash.
