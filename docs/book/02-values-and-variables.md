# 2. Values, Variables, and Scope

Lash has simple value categories: numbers, strings, booleans, arrays, maps, and unknown shell-derived values.

## Declarations

```lash
var count = 0
let channel = "stable"
readonly project = "lash"
global var runs = 0
```

- `var` declares a mutable variable.
- `let` declares a Lash-level immutable variable. Reassignment is rejected by `lash check`.
- `readonly` declares a Bash runtime readonly variable and requires an initializer.
- `global` declares or assigns in global scope from inside a function.

Declarations may omit an initializer for `var` and `let`; the compiler emits an empty value.

```lash
var output
let empty
```

## Scope

Functions introduce local scope. Variables declared inside a function lower to Bash `local` unless they are marked `global`.

```lash
var mode = "dev"

fn set_mode()
    global mode = "release"
    var local_note = "only visible here"
end
```

Block bodies such as `if`, `for`, and `switch` are checked with scoped symbol rules, so declarations inside a branch do not become a reliable declaration for later code unless declared earlier.

## The discard binding

`_` can be used where a binding name is required but the value is intentionally ignored.

```lash
for _ in 1..3
    echo "tick"
end
```

The ignored binding can still execute side effects if its initializer is a capture. Lash warns for some suspicious ignored captures.

## Booleans

Boolean literals are `true` and `false`. They lower to Bash numeric truth values:

- `true` becomes `1`
- `false` becomes `0`

This means booleans work naturally in Lash arithmetic-style conditions.

## Strings

```lash
let plain = "hello"
let interpolated = $"hello {name}"
let block = [[line one
line two]]
let interpolated_block = $[[hello {name}
line two]]
```

Interpolation placeholders use Lash/Bash identifier paths. For example, `{name}` and `{items[0]}` become Bash expansions.

Use `$"..."` or `$[[...]]` when you want interpolation. Plain strings that look like `{name}` are treated as plain text and may trigger a warning.
