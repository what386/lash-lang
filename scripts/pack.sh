#!/usr/bin/env bash
declare -a __lash_jobs=()
set -euo pipefail
INPUT_ROOT="./build"
OUTPUT_DIR="./dist"
MAX_PARALLEL=4
wait_for_batch() {
    local -a pending=("${@:1}")

    for job in "${pending[@]}"; do
        local pid=$(printf '%s' $job | cut -d: -f1)
        local dir=$(printf '%s' $job | cut -d: -f2-)
        set +e
        wait "${pid}"
        local status=$?
        set -e
        if [[ ${status} != 0 ]]; then
            echo "Packing failed:" $dir
            exit $status
        fi
    done
}
mkdir -p "$OUTPUT_DIR"
cd "$INPUT_ROOT"
packed=0
active_jobs=()
for dir in ./*; do
    archive="../dist/${dir}.tar.gz"
    (
        echo "Packing:" $dir "->" $archive
        tar -czf "$archive" "$dir"
    )     &
    pack_pid=$!
    __lash_jobs+=("$!")
    active_jobs+=("${pack_pid}:${dir}")
    packed=$(( ${packed} + 1 ))
    if (( ${#active_jobs[@]} >= 4 )); then
        wait_for_batch "${active_jobs[@]}"
        active_jobs=()
    fi
done
if (( ${#active_jobs[@]} > 0 )); then
    wait_for_batch "${active_jobs[@]}"
fi
if [[ ${packed} == 0 ]]; then
    echo "No directories found under ./build"
else
    echo "Packed ${packed} directories."
fi
