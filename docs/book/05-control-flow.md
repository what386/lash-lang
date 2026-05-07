# 5. Control Flow

Lash uses `end` to close structured blocks. That is different from Bash, where `if` closes with `fi`, loops close with `done`, and `case` closes with `esac`.

## if / elif / else

```lash
if mode == "release"
    echo "release"
elif mode == "debug"
    echo "debug"
else
    echo "default"
end
```

Conditions can use comparisons, regex matches, logical operators, or numeric truthiness.

## while and until

```lash
var i = 0

while i < 3
    echo $"{i}"
    i++
end

until i == 0
    i--
end
```

The compiler warns on constant conditions and unreachable loop bodies when it can prove them.

## for over ranges

```lash
for i in 1..5
    echo $"{i}"
end

for i in 10..2 step -2
    echo $"{i}"
end
```

Ranges lower to `seq`. The compiler warns when a constant `step` does not move toward the range end.

## for over arrays, argv, and globs

```lash
let names = ["a", "b", "c"]

for name in names
    echo $"{name}"
end

for arg in argv
    echo $"{arg}"
end

for file in ./dist/*.tar.gz
    echo $"{file}"
end
```

Glob loops lower directly to Bash word/glob iteration and follow the shell's active glob options.

## select

```lash
select choice in ["yes", "no"]
    echo $"{choice}"
    break
end
```

`select` lowers to Bash `select`. It can iterate over arrays, `argv`, ranges, or globs.

## switch

```lash
enum Mode
    Dev
    Release
end

switch mode
    case Mode::Dev, "debug":
        echo "debug"
    case Mode::Release:
        echo "release"
    case _:
        echo "fallback"
end
```

`switch` lowers to Bash `case`. `case _:` is the wildcard and cannot be combined with other patterns. Duplicate constant case patterns are diagnosed.

## break and continue depths

```lash
while outer < 10
    while inner < 10
        continue 2
    end
end
```

`break` and `continue` are only valid inside loops. Optional depths must be positive integer literals.
