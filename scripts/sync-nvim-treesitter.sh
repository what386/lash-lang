#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="/home/bmorin/dev/programming/lash-lang"

SOURCE_DIR="${REPO_ROOT}/tree-sitter"
TARGET_DIR="${HOME}/.config/nvim/treesitter/lash"
QUERY_SOURCE_DIR="${SOURCE_DIR}/queries"
QUERY_TARGET_DIR="${HOME}/.config/nvim/queries/lash"

DRY_RUN=false
DELETE=true

show_help() {
    cat <<EOF
Usage: $0 [--dry-run] [--no-delete] [--target <path>]

Sync Lash tree-sitter parser files into Neovim local parser directory.

Options:
  --dry-run          Show what would change without writing files
  --no-delete        Do not delete files in target that no longer exist in source
  --target <path>    Override target directory (default: ${TARGET_DIR})
  -h, --help         Show this help text
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
    --dry-run)
        DRY_RUN=true
        shift
        ;;
    --no-delete)
        DELETE=false
        shift
        ;;
    --target)
        if [[ $# -lt 2 ]]; then
            echo "error: --target requires a path" >&2
            exit 1
        fi
        TARGET_DIR="$2"
        shift 2
        ;;
    -h | --help)
        show_help
        exit 0
        ;;
    *)
        echo "error: unknown option '$1'" >&2
        show_help
        exit 1
        ;;
    esac
done

if [[ ! -d "${SOURCE_DIR}" ]]; then
    echo "error: source parser directory not found: ${SOURCE_DIR}" >&2
    exit 1
fi
if [[ ! -d "${QUERY_SOURCE_DIR}" ]]; then
    echo "error: query directory not found: ${QUERY_SOURCE_DIR}" >&2
    exit 1
fi

mkdir -p "${TARGET_DIR}"

RSYNC_FLAGS=(-a)
if [[ "${DELETE}" == true ]]; then
    RSYNC_FLAGS+=(--delete)
fi
if [[ "${DRY_RUN}" == true ]]; then
    RSYNC_FLAGS+=(--dry-run --itemize-changes)
fi

echo "Syncing tree-sitter parser:"
echo "  source: ${SOURCE_DIR}"
echo "  target: ${TARGET_DIR}"
echo "  query-source: ${QUERY_SOURCE_DIR}"
echo "  query-target: ${QUERY_TARGET_DIR}"
echo "  delete: ${DELETE}"
echo "  dryrun: ${DRY_RUN}"

rsync "${RSYNC_FLAGS[@]}" "${SOURCE_DIR}/" "${TARGET_DIR}/"

mkdir -p "${QUERY_TARGET_DIR}"
rsync "${RSYNC_FLAGS[@]}" "${QUERY_SOURCE_DIR}/" "${QUERY_TARGET_DIR}/"
echo "Synced query files: ${QUERY_TARGET_DIR}"

if [[ "${DRY_RUN}" == true ]]; then
    echo "Dry run complete. No files were modified."
else
    echo "Sync complete."
fi
