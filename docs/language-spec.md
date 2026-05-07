# Lash Language Spec

This document summarizes the language currently implemented by `src/Lash.Compiler/Lash.g4` and the compiler pipeline.

For examples and learning material, read [The Lash Book](./book/README.md).

## Design Goal

Lash is a Lua-like scripting language that lowers directly to Bash with minimal runtime overhead.

## File Format

- Files conventionally use `.lash`.
- A leading shebang is allowed and stripped before parsing.
- Line comments use `//`.
- Block comments use `/* ... */`.

## Preprocessor Directives

Directives run before parsing and start with `@`.

Supported directives:

- conditionals: `@if`, `@elif`, `@else`, `@end`
- symbols: `@define`, `@undef`
- imports: `@import <path>`, `@import <path> into <name>`
- raw passthrough: `@raw ... @end`
- diagnostics: `@warning`, `@error`

Directive conditions support symbol presence checks, literals, `==`, `!=`, `!`, `&&`, `||`, and parentheses.

`@import` is only valid at file/preprocessor scope. Plain `@import` includes another Lash source file. `@import ... into name` reads file text into a multiline string assignment; if `name` is not known at top level, it creates `let name = ...`.

## Statements

### Declarations

```lash
var name
var name = expr
let name
let name = expr
readonly name = expr
global var name = expr
global let name = expr
global readonly name = expr
```

`readonly` requires an initializer. `_` is accepted as a discard binding where a binding name is required.

### Assignment and Updates

```lash
name = expr
global name = expr
array[index] = expr
name += expr
name -= expr
name *= expr
name /= expr
name %= expr
name++
name--
```

Compound operators require variable targets except simple index assignment with `=`.

### Functions and Enums

```lash
fn name(param1, param2 = defaultValue)
    statements
end

enum Name
    Member
end
```

Function arity is checked. Enum member access uses `EnumName::Member`.

### Control Flow

```lash
if expr
    statements
elif expr
    statements
else
    statements
end

for name in expr
    statements
end

for name in expr step expr
    statements
end

for name in ./glob/*.txt
    statements
end

select name in expr
    statements
end

while expr
    statements
end

until expr
    statements
end

switch expr
    case expr:
        statements
    case expr1, expr2:
        statements
    case _:
        statements
end
```

Loop control:

```lash
break
break 2
continue
continue 2
```

Depth arguments must be positive integer literals.

### Process and Shell Statements

```lash
return
return expr
shift
shift expr
subshell [into name] ... end [&]
coproc [into name] ... end
wait
wait expr
wait jobs
wait expr into name
trap SIGNAL into handler()
trap SIGNAL "command"
untrap SIGNAL
sh "command"
test "condition"
```

Bare shell command lines are accepted when they do not look like Lash syntax.

## Expressions

### Literals

- integers: `0`, `123`
- booleans: `true`, `false`
- strings: `"text"`
- interpolated strings: `$"hello {name}"`
- multiline strings: `[[line1\nline2]]`
- interpolated multiline strings: `$[[hello {name}\nline2]]`

### Variables and Calls

- variable reference: `name`
- function call: `name(args...)`
- enum access: `EnumName::Member`
- argument access: `argv[index]`, `#argv`

### Collections

```lash
[expr, expr]
{ "key": expr, "other": expr }
expr[expr]
```

Map literal keys must be strings.

### Shell Expressions

```lash
$(command)
$(test "condition")
<(...)
>(...)
```

Shell payloads are represented internally as string literals and must remain string-literal-like for Bash code generation.

### Operators

Unary:

- `!`
- `-`
- `+`
- `#`

Binary:

- arithmetic: `*`, `/`, `%`, `+`, `-`
- range: `..`
- comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
- regex match: `=~`
- logical: `&&`, `||`
- value pipe: `|`
- redirects: `>`, `>>`, `2>`, `2>>`, `&>`, `&>>`, `<`, `<>`, `<<`, `<<-`, `n>&m`, `n>&-`

## Type Model

Lash uses coarse expression categories for static checks:

- `number`
- `string`
- `bool`
- `array`
- `unknown`

There is no user-facing type annotation syntax. Runtime values are Bash values.

## Semantic Rules

- Variables must be declared before use.
- Duplicate declarations in the same scope are rejected.
- `let` variables cannot be reassigned.
- `readonly` cannot be declared in repeated loop contexts.
- Function calls must match required/default parameter arity.
- `break` and `continue` are only valid inside loops.
- Enum declarations must be non-empty and member names must be unique.
- Enum access is validated.
- `case _:` is the wildcard switch case and cannot be combined with other patterns.
- Duplicate constant switch patterns are rejected.
- Map keys must be strings.
- A container cannot mix numeric and string key modes.
- Division and modulo by constant zero are rejected.
- Invalid constant shift amounts and invalid constant range steps are rejected.

## Bash Code Generation Restrictions

Some syntax parses as Lash but is rejected if the Bash generator cannot lower it safely.

- Regex `=~` is only supported in condition positions.
- Range expressions are only supported as the top-level iterable in `for ... in`.
- Redirect expressions are only supported as standalone expression statements.
- `<<-` requires a multiline string literal payload.
- Index access receivers must be named variables.
- Index assignment targets must be named variables.
- Assignment to `argv` is not supported.
- Bare `argv` is only supported in specific contexts: declarations, assignment RHS, function arguments, loop ranges, append RHS, and length checks.
- `sh`, `test`, trap command payloads, command substitution payloads, and process substitution payloads must be string-literal-like shell payloads.
- Value pipe expressions require a function call on the right.
- Pipe statements only support compiler-recognized assignment-sink forms.

## Bash Lowering Notes

- `true` lowers to `1`; `false` lowers to `0`.
- `return expr` lowers to `echo <expr>; return 0`.
- `EnumName::Member` lowers to `"EnumNameMember"`.
- Map literals lower to Bash associative arrays.
- `argv[index]` is zero-based and lowers to Bash positional parameter access.
- `#argv` lowers to `$#`.
- `for i in start..end step n` lowers through `seq`.
- Glob-style `for` and `select` loops lower to Bash word/glob iteration.
- `switch` lowers to Bash `case`; multi-pattern cases lower to `pattern1|pattern2)`.
- `=~` lowers to `[[ left =~ regex ]]` in conditions.
- `subshell ... end` lowers to `(...)`.
- Background `subshell ... end &` captures `$!` when `into` is present.
- Foreground `subshell into name` captures `$?`.
- `coproc ... end` lowers to `coproc { ... }`; `into` captures `${COPROC_PID}`.
- `wait jobs` drains compiler-tracked background subshell/coproc pids.
- Single-line `<<` payloads lower to Bash here-strings; multiline payloads lower to heredocs.
- In shell payload strings, `$name...` expands an array variable as Bash `"${name[@]}"`.

## Diagnostics

Diagnostic code ranges:

- `E000-E001`: lex/parse diagnostics
- `E010-E015`: preprocessor diagnostics
- `E110-E125`: name/declaration/scope diagnostics
- `E200-E203`: type and semantic compatibility diagnostics
- `E300-E303`: flow and constant-safety diagnostics
- `E400-E401`: codegen-feasibility diagnostics
- `W010`, `W500-W522`: warnings
