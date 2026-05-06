# Lash Usage Guide

This guide walks through the currently implemented language in practical chunks.

## 1. Hello World

```lash
#!/usr/bin/env -S lash run

echo "Hello, Lash!"
```

Run it:

```bash
lash run hello.lash
```

## 2. Variables, Constants, And Globals

```lash
var name = "Lash"
let version = "0.14"
readonly channel = "stable"
global var runs = 0

runs += 1
echo $"{name} {version} {channel} / runs={runs}"
```

- `var` is mutable.
- `let` is compile-time immutable.
- `readonly` lowers to runtime shell immutability.
- `global` forces declaration or assignment in global scope.

## 3. Arrays, Maps, And Indexing

```lash
let items = ["alpha", "beta", "gamma"]
let meta = {"name": "lash", "shell": "bash"}

echo $"{items[0]} / {meta["name"]}"
echo $"items={#items} meta={#meta}"
```

String-keyed containers lower to Bash associative arrays.

## 4. Arithmetic And Updates

```lash
var n = 10
n += 2
n -= 1
n *= 3
n /= 2
n %= 4
n++
n--

echo "$n"
```

## 5. Conditionals, Tests, And Regex Matches

```lash
let branch = $(git rev-parse --abbrev-ref HEAD)
let has_branch = $(test "-n \"${branch}\"")

if branch =~ "^main$"
    echo "protected"
elif has_branch == "1"
    echo $"other branch: {branch}"
else
    echo "detached"
end
```

`=~` lowers to Bash regex matching and is currently supported in condition positions.

## 6. Loops And Loop Control

```lash
let items = ["a", "b", "c"]
var i = 0

while i < #items
    echo $"{i}: {items[i]}"
    i++
end

until i == 0
    i--
end

for item in items
    echo $"for: {item}"
end

select choice in ["yes", "no"]
    echo $"selected: {choice}"
    break
end
```

Nested loop control supports optional depths:

```lash
var outer = 0
var inner = 0

while outer < 10
    outer++
    inner = 0

    while inner < 10
        inner++
        if inner == 3
            continue 2
        end
    end
end
```

## 7. Functions, Enums, And Switch

```lash
fn greet(name, prefix = "hello")
    return $"{prefix}, {name}"
end

enum Mode
    Dev
    Release
end

let mode = Mode::Dev

switch mode
    case Mode::Dev, "debug":
        echo "debug settings"
    case Mode::Release:
        echo "optimized settings"
    case _:
        echo "fallback settings"
end
```

## 8. Shell Integration

Run shell commands directly as statements:

```lash
pwd
ls -1
set -euo pipefail
shopt -s nullglob
export RELEASE_CHANNEL=stable
source ./env.sh
```

Capture shell output into Lash values:

```lash
let branch = $(git rev-parse --abbrev-ref HEAD)
echo $"branch: {branch}"
```

Use process substitution and stdin-string lowering:

```lash
fn feed()
    cat
end

feed() << "payload"
feed() << [[line1
line2]]
feed() <<- [[	line1
	line2]]

diff <(sort left.txt) <(sort right.txt)
```

## 9. Process Control, Background Work, And Traps

```lash
fn cleanup()
    echo "done"
end

trap EXIT into cleanup()
trap INT "echo interrupted"

var pid = 0
var status = 0
var copid = 0

subshell into pid
    sh "sleep 1"
end &

coproc into copid
    sh "sleep 1"
end

wait pid into status
wait jobs
untrap INT
echo $"subshell={pid} coproc={copid} exit={status}"
```

## 10. Preprocessor Directives

```lash
@define SHOW_MESSAGE true

@if SHOW_MESSAGE == true
echo "compiled with message enabled"
@end

@import "notes.txt" into notes
echo $"{notes}"
```

## 11. Running, Checking, And Compiling

Run a Lash file:

```bash
lash run script.lash
```

Check semantics only:

```bash
lash check script.lash
```

Compile to Bash:

```bash
lash compile script.lash -o script.sh
bash script.sh
```

Watch Lash files and recompile changed files:

```bash
lash watch script.lash
lash watch src/
```
