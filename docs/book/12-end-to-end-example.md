# 12. End-to-End Example

This script packages files from `dist`, writes checksums, and demonstrates common Lash patterns.

```lash
#!/usr/bin/env -S lash run

set -euo pipefail
shopt -s nullglob

let dist = "./dist"
let checksum_file = dist + "/SHA256SUMS"

if $(test $"-d \"{dist}\"") != 1
    echo $"missing directory: {dist}"
    exit 1
end

fn checksum(path)
    return $(sha256sum "$path" | cut -d' ' -f1)
end

var count = 0
echo "" > $"{checksum_file}"

for file in ./dist/*.tar.gz
    let sum = checksum(file)
    echo $"{sum}  {file}" >> $"{checksum_file}"
    count += 1
end

if count == 0
    echo "no archives found"
    exit 1
end

echo $"wrote {count} checksums to {checksum_file}"
```

## What this uses

- `set` and `shopt` as bare Bash command statements
- immutable `let` bindings for configuration
- `$(test "...")` to turn a Bash test into `1` or `0`
- a function that returns a value through output
- a glob `for` loop
- arithmetic `+=`
- string interpolation
- redirection with `>` and `>>`

## Check it before running

```bash
lash check checksums.lash
lash run checksums.lash
```

When behavior is unclear, compile the script and inspect the Bash:

```bash
lash compile checksums.lash -o checksums.sh
sed -n '1,200p' checksums.sh
```
