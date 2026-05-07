# 7. Shell Integration

Lash is built for shell scripts. It supports many Bash features directly, but it still parses known Lash syntax first.

## Bare shell commands

Most shell-like lines that are not recognized as Lash statements become raw command statements:

```lash
set -euo pipefail
shopt -s nullglob
export RELEASE_CHANNEL=stable
source ./env.sh
echo "building..."
```

The compiler recognizes common shell builtins such as `set`, `export`, `shopt`, `alias`, `source`, `.`, `unset`, `declare`, and `local` for validation and warnings.

Known Lash statement prefixes such as `if`, `for`, `var`, `let`, `return`, `trap`, and `wait` are parsed as Lash syntax.

## `sh` statements

Use `sh "..."` when you want to force a string literal to be emitted as shell code.

```lash
sh "printf '%s\n' \"$HOME\""
```

The payload must be a string literal. This restriction keeps Bash code generation predictable.

## Command substitution

```lash
let branch = $(git rev-parse --abbrev-ref HEAD)
echo $"branch={branch}"
```

Lash preserves Bash command substitution syntax by rewriting it internally before parsing.

Interpolated command segments are supported:

```lash
let dist = "./dist"
let archives = $(find $"\"{dist}\" -maxdepth 1 -name '*.tar.gz'")
```

## `test` captures

```lash
let path = ".git"
let has_git = $(test $"-d \"{path}\"")

if has_git == 1
    echo "repo"
end
```

`$(test "...")` lowers to a Bash `[[ ... ]]` check that echoes `1` or `0`. The test payload must be a string literal.

## Array spread in shell payloads

Inside shell payload strings, `$name...` expands a Lash array variable as Bash `"${name[@]}"`.

```lash
let files = ["a.txt", "b.txt"]
sh "printf '%s\n' $files..."
```

## Process substitution

```lash
diff <(sort left.txt) <(sort right.txt)
```

Input process substitution `<(...)` and output process substitution `>(...)` are supported and lower back to Bash process substitution.

## Redirects

Redirect expressions are supported as standalone expression statements.

```lash
echo "log" > "build.log"
echo "more" >> "build.log"
echo "error" 2> "error.log"
echo "all" &> "combined.log"
cat < "input.txt"
exec <> "state.db"
```

Supported redirect operators:

- `>` stdout truncate
- `>>` stdout append
- `2>` stderr truncate
- `2>>` stderr append
- `&>` stdout and stderr truncate
- `&>>` stdout and stderr append
- `<` stdin from file
- `<>` read/write file
- `n>&m` duplicate file descriptor
- `n>&-` close file descriptor

## Here input

```lash
cat << "one line"

cat << [[line one
line two]]

cat <<- [[	line with leading tab
	next line]]
```

Single-line `<<` string payloads lower to Bash here-strings. Multiline literals lower to heredocs. `<<-` requires a multiline string literal and lowers to a tab-stripping heredoc.

The deprecated Bash spelling `<<<` is not the Lash syntax. Use `<<`.
