# 4. Collections, Indexing, and argv

Lash arrays and maps lower to Bash arrays. The compiler tracks whether a container is numeric-indexed or string-keyed so accidental mixed indexing can be reported early.

## Arrays

```lash
let items = ["alpha", "beta", "gamma"]
let count = #items

echo $"{items[0]}"
echo $"count={count}"
```

Numeric arrays lower to Bash indexed arrays.

## Array append

`+=` is overloaded. With arrays, it appends array values.

```lash
var items = ["alpha"]
items += ["beta", "gamma"]
```

When the right-hand side looks numeric, `+=` is arithmetic. If the compiler cannot tell which mode you intended, it reports an ambiguity.

## Maps

```lash
let meta = {
    "name": "lash",
    "shell": "bash"
}

echo $"{meta["name"]}"
```

Maps are string-keyed containers and lower to Bash associative arrays. Map literal keys must be strings.

Do not mix numeric and string keys for the same container.

## Index assignments

```lash
var items = ["a", "b"]
items[1] = "changed"

var meta = {"name": "lash"}
meta["version"] = "0.3"
```

Index assignment targets must be named variables. Assigning into an arbitrary expression is rejected for Bash code generation.

## argv

`argv` represents script positional arguments.

```lash
let arg_count = #argv
echo $"{argv[0]}"
echo $"arg-count={arg_count}"

for arg in argv
    echo $"{arg}"
end
```

Lash uses zero-based indexing for `argv[index]`. Bash positional parameters are one-based, so the compiler lowers `argv[0]` to the first Bash argument.

Bare `argv` is allowed in contexts where the compiler can expand it safely, such as declarations, function arguments, `for` ranges, appends, and length checks.

```lash
let args = argv
items += argv
```

Assignment to `argv` is not supported.
