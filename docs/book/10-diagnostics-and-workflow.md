# 10. Diagnostics and Workflow

Lash tries to fail before Bash runs when a problem can be found statically.

## Check first

```bash
lash check script.lash
```

`check` runs preprocessing, parsing, semantic analysis, warnings, and codegen feasibility checks without writing or running Bash output.

## Run and compile

```bash
lash run script.lash arg1 arg2
lash compile script.lash -o script.sh
bash script.sh
```

`run` compiles to temporary Bash and executes it. `compile` writes the Bash output so you can inspect or run it yourself.

## Format and watch

```bash
lash format .
lash format . --check
lash watch script.lash
lash watch src/
```

Use `format --check` in CI and `watch` when iterating on scripts.

## Diagnostic groups

Diagnostic codes are grouped by phase:

- `E000-E001`: lexing and parsing
- `E010-E015`: preprocessor errors
- `E110-E125`: names, declarations, scope, traps, enums, and `into`
- `E200-E203`: type and container compatibility
- `E300-E303`: flow and constant-safety checks
- `E400-E401`: code generation feasibility
- `W010`, `W500-W522`: warnings

## Common warnings

Warnings include:

- unreachable statements
- unused variables, parameters, and functions
- shadowed variables
- constant conditions
- suspicious missing interpolation
- malformed shell expansions
- suspicious heredoc payloads
- `wait jobs` with no tracked jobs
- non-positive constant `wait` targets
- missing or malformed shebangs

Warnings are there to make shell scripts less surprising. Treat them as design feedback, especially in automation code.

## A useful debugging loop

1. Run `lash check`.
2. Fix the first real error.
3. Re-run `lash check`.
4. If generated Bash behavior is surprising, run `lash compile` and inspect the output.
5. Use `--verbose` on CLI commands when you need phase and tool resolution details.
