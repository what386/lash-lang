# 6. Functions and Value Flow

Lash functions lower to Bash functions, but parameter binding and return values are more structured.

## Declaring and calling functions

```lash
fn greet(name, prefix = "hello")
    return $"{prefix}, {name}"
end

let message = greet("world")
echo $"{message}"
```

Function arity is checked. Parameters with default values are optional. Required parameters must be passed by the caller.

## Return values

`return expr` does not lower to Bash `return <number>`. It emits the expression with `echo`, then returns status `0`.

```lash
fn archive_name(name)
    return name + ".tar.gz"
end
```

This makes Lash functions usable in value expressions because Bash captures command output from functions.

Use bare `return` when you only want to stop the function successfully.

## Function arguments

Arguments are rendered as shell arguments. Strings are quoted for the generated Bash.

```lash
fn show(value)
    echo $"{value}"
end

show("hello world")
```

Passing `argv` forwards all script arguments:

```lash
fn forward()
    printf '%s\n' "$@"
end

forward(argv)
```

## Array-like parameters

When the compiler sees an array argument for a function parameter, it can lower that parameter as an array in the generated function body.

```lash
fn print_all(items)
    for item in items
        echo $"{item}"
    end
end

let names = ["a", "b"]
print_all(names)
```

## Value pipes

```lash
fn suffix(value, ext)
    return value + ext
end

let archive = "Lash-linux-x64" | suffix(".tar.gz")
```

In a value pipe, the left value becomes the first argument to the function call on the right.

## Unknown shell-derived values

Captures such as `$(...)` often produce `unknown` values because Bash decides the runtime output. Use explicit checks and conversions where the meaning matters.
