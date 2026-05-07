# 11. What Lash Does Differently From Bash

Lash compiles to Bash, but it is not Bash syntax with a different file extension. This chapter lists the differences that matter most when reading or porting scripts.

## Blocks close with `end`

```lash
if ready
    echo "ready"
end

for file in ./*.lash
    echo $"{file}"
end
```

Generated Bash uses `fi`, `done`, and `esac`; Lash source uses `end`.

## Declarations are checked

Bash lets you assign almost anywhere. Lash distinguishes declaration from assignment.

```lash
let name = "lash"
name = "other" // error
```

Variables must be declared before use. `let` cannot be reassigned. `readonly` also lowers to Bash readonly behavior.

## Conditions are expressions

```lash
if count > 0
    echo "non-zero"
end
```

Lash generates Bash tests for you. Numeric comparisons use arithmetic tests. String equality uses `[[ ... ]]`. Regex `=~` is supported in conditions only.

## Booleans are numeric at runtime

`true` lowers to `1`; `false` lowers to `0`.

## Functions return values by output

```lash
fn name()
    return "lash"
end
```

This emits `echo "lash"` followed by `return 0`. It is designed for command-substitution style value flow, not Bash numeric return status.

## argv is zero-based

```lash
let arg_count = #argv
echo $"{argv[0]}"
echo $"{arg_count}"
```

Bash positional parameters are one-based after `$0`. Lash `argv[0]` means the first script argument.

## Maps are Bash associative arrays

```lash
let meta = {"name": "lash"}
echo $"{meta["name"]}"
```

The compiler lowers string-keyed maps to Bash associative arrays and enforces string keys for map literals.

## Ranges are Lash syntax

```lash
for i in 1..5 step 2
    echo $"{i}"
end
```

Ranges lower through `seq` and are only supported as `for` iterables.

## wait jobs is Lash-tracked

`wait jobs` waits for background `subshell` and `coproc` jobs the compiler tracks. It does not scan arbitrary Bash jobs started by raw command lines.

## Bare Bash is still supported

These shell forms are intentionally accepted:

```lash
set -euo pipefail
export NAME=value
source ./env.sh
shopt -s nullglob
alias ll='ls -l'
unset NAME
declare -A table
local value=1
```

Command substitution, process substitution, globs, redirects, heredocs, traps, background jobs, and `coproc` are also supported. Lash adds structure around them; it does not remove Bash from the language.
