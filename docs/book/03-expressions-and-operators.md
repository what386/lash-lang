# 3. Expressions and Operators

Lash expressions are checked before they are lowered to Bash. Some operators can be used anywhere a value is allowed; others are only supported in specific contexts.

## Arithmetic

```lash
var n = 10
n += 2
n -= 1
n *= 3
n /= 2
n %= 4
n++
n--
```

Arithmetic operators are numeric:

- `*`, `/`, `%`
- `+`, `-`
- unary `-`, unary `+`

Compound assignments other than `+=` require a variable target and numeric operands.

## String concatenation with `+`

`+` is numeric unless either side is string-like.

```lash
let name = "lash"
let archive = name + ".tar.gz"
```

Mixed `number + string` is rejected when the checker can prove the mismatch.

## Comparisons and truthiness

```lash
if count > 0
    echo "has items"
end

if name == "lash"
    echo "matched"
end
```

`==` and `!=` lower to Bash `[[ left == right ]]` in condition code paths. Numeric comparisons such as `<`, `>`, `<=`, and `>=` lower to arithmetic tests.

For a bare condition like `if count`, Lash treats numeric non-zero as true. `argv` is special: bare `argv` in a condition checks whether any positional arguments exist.

## Regex match

```lash
if branch =~ "^main$"
    echo "protected branch"
end
```

`=~` lowers to Bash regex matching in condition positions. It is intentionally not supported as a general value expression.

## Logical operators

```lash
if ready && !dry_run
    echo "apply"
end
```

Supported logical operators:

- `!`
- `&&`
- `||`

## Length with `#`

```lash
let items = ["a", "b", "c"]
let item_count = #items
let first_length = #items[0]
let arg_count = #argv
echo $"items={item_count}"
echo $"first-length={first_length}"
echo $"args={arg_count}"
```

`#` works for identifiers, index access, string literals, array literals, map literals, and `argv`.

## Range expressions

```lash
for i in 1..5
    echo $"{i}"
end

for i in 1..10 step 2
    echo $"{i}"
end
```

Ranges are only supported as the top-level iterable in `for ... in`. They lower through `seq`.

## Value pipes

Value pipes pass the left value as the first argument to a function call on the right.

```lash
fn wrap(value, prefix = "[")
    return prefix + value + "]"
end

let label = "ok" | wrap("status=")
```

As an expression, the right-hand side of `|` must be a function call. As a standalone statement, pipe handling is more limited and intended for assignment-sink forms supported by the compiler.
