# 8. Process Control and Jobs

Lash exposes Bash process control with structured syntax for the common cases.

## shift

```lash
shift
shift 2
```

`shift` defaults to `1`. Lash guards the generated Bash so shifting past the end clears the argument list instead of failing halfway through.

## subshell

```lash
subshell
    cd build
    make
end
```

`subshell ... end` lowers to a Bash subshell block:

```bash
(
  ...
)
```

## Capturing subshell status or pid

```lash
var status = 0
subshell into status
    false
end

var pid = 0
subshell into pid
    sleep 1
end &
```

For a foreground subshell, `into` captures `$?`. For a background subshell, `into` captures `$!`.

## coproc

```lash
var copid = 0

coproc into copid
    sh "cat"
end
```

`coproc ... end` lowers to Bash `coproc { ... }`. `into` captures `${COPROC_PID}`.

## wait

```lash
wait
wait pid
wait pid into status
```

`wait` lowers to Bash `wait`. `into` captures the resulting exit status.

## wait jobs

```lash
subshell
    sleep 1
end &

coproc
    sleep 1
end

wait jobs
```

`wait jobs` waits for jobs that Lash tracks from background `subshell` and `coproc` statements in the current analysis scope. It does not discover arbitrary jobs started by raw shell commands.

The compiler warns when `wait jobs` has no tracked jobs.

## trap and untrap

```lash
fn cleanup()
    echo "cleanup"
end

trap EXIT into cleanup()
trap INT "echo interrupted"
untrap INT
```

`trap SIGNAL into function()` registers a Lash function as the handler. Handler calls cannot include arguments. `trap SIGNAL "command"` emits a Bash command handler. `untrap SIGNAL` clears the trap.

Valid trap signals are checked by the compiler.
