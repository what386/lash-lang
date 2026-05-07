# 9. Compile-Time Directives

Directives run before Lash parsing. They are useful for file inclusion, conditional compilation, raw shell passthrough, and build-time diagnostics.

Directive lines start with `@`.

## Conditional directives

```lash
@define ENABLE_LOGS true

@if ENABLE_LOGS == true
echo "logs enabled"
@elif DEBUG
echo "debug enabled"
@else
echo "quiet"
@end
```

Supported directive expression features:

- symbol presence checks
- literals
- `==`
- `!=`
- `!`
- `&&`
- `||`
- parentheses

Use `@end`, not `@endif`.

## Defining and undefining symbols

```lash
@define MODE release
@define FEATURE_X=true
@undef FEATURE_X
```

Definitions may use either whitespace or `=`:

```lash
@define NAME value
@define NAME=value
```

## Importing Lash source

```lash
@import "./shared.lash"
```

Plain `@import` includes and preprocesses another file. Relative paths are resolved relative to the file containing the directive.

`@import` is only allowed at file/preprocessor scope, not inside runtime blocks.

## Importing file text into a variable

```lash
@import "./template.txt" into template
echo $"{template}"
```

`@import "path" into name` reads file text into a multiline string. If `name` is already known at top level, the directive assigns it. Otherwise it creates a new `let`.

Imported text cannot contain `]]` when using `into`, because the preprocessor represents it as a Lash multiline string literal.

## Raw blocks

```lash
@raw
case "$1" in
  --help) echo help ;;
esac
@end
```

Raw block content is passed through as command text while the current directive branch is active.

## Build-time diagnostics

```lash
@warning This script assumes GNU sed.

@if PLATFORM == windows
@error Windows is not supported by this script.
@end
```

`@warning` emits a non-fatal diagnostic. `@error` stops compilation when the active branch is reached.
