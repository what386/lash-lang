#!/usr/bin/env bash
set -euo pipefail
readonly RED="\\033[0;31m"
readonly GREEN="\\033[0;32m"
readonly YELLOW="\\033[1;33m"
readonly BLUE="\\033[0;34m"
readonly NC="\\033[0m"
readonly PROJECT_NAME="Lash"
readonly CONFIGURATION="Release"
readonly OUTPUT_DIR="./build"
readonly PROJECTS=("lash:./src/Lash.Cli/Lash.Cli.csproj" "lashc:./src/Lash.Compiler/Lash.Compiler.csproj" "lashfmt:./src/Lash.Formatter/Lash.Formatter.csproj" "lashlsp:./src/Lash.Lsp/Lash.Lsp.csproj")
ENABLE_SELF_CONTAINED=1
show_help() {
    echo $'\n    Usage: build.lash [OPTIONS] <platform1> [platform2] ...\n\n    Options:\n    --no-self-contained  Build as framework-dependent (requires installed runtime)\n    -h, --help         Show this help message\n\n    Arguments:\n    <platforms>  Space-separated list of platforms to build\n\n    Available platforms:\n    win-x64, win-x86, win-arm64\n    linux-x64, linux-arm64, linux-arm\n    osx-x64, osx-arm64\n\n    Examples:\n    build.lash win-x64                        # Build only Windows 64-bit\n    build.lash --no-self-contained linux-x64  # Framework-dependent Linux build\n    build.lash win-x64 linux-x64              # Build Windows and Linux 64-bit\n    build.lash osx-x64 osx-arm64              # Build both macOS versions\n    '
}
is_windows_rid() {
    local rid="$1"

    case ${rid} in
        win-*)
            echo 1
            return 0
            ;;
    esac
    echo 0
    return 0
}
file_exists() {
    local path="$1"

    echo $(( $(if [[ -f "${path}" ]]; then echo 1; else echo 0; fi) == 1 ))
    return 0
}
dir_exists() {
    local path="$1"

    echo $(( $(if [[ -d "${path}" ]]; then echo 1; else echo 0; fi) == 1 ))
    return 0
}
get_output_dirname() {
    local rid="$1"

    local platform=""
    local arch=""
    local bits=""
    case ${rid} in
        win-x64)
            platform="win"
            arch="x86"
            bits="64"
            ;;
        win-x86)
            platform="win"
            arch="x86"
            bits="32"
            ;;
        win-arm64)
            platform="win"
            arch="arm"
            bits="64"
            ;;
        linux-x64)
            platform="linux"
            arch="x86"
            bits="64"
            ;;
        linux-arm64)
            platform="linux"
            arch="arm"
            bits="64"
            ;;
        linux-arm)
            platform="linux"
            arch="arm"
            bits="32"
            ;;
        osx-x64)
            platform="osx"
            arch="x86"
            bits="64"
            ;;
        osx-arm64)
            platform="osx"
            arch="arm"
            bits="64"
            ;;
    esac
    echo "${PROJECT_NAME}-${platform}_${arch}-${bits}"
    return 0
}
find_published_executable() {
    local temp_dir="$1"
    local rid="$2"
    local project_stem="$3"

    local src_file=""
    if [ $(is_windows_rid "${rid}") -ne 0 ]; then
        local expected="${temp_dir}/${project_stem}.exe"
        if [ $(file_exists "${expected}") -ne 0 ]; then
            src_file=${expected}
        else
            src_file=$(find "$temp_dir" -maxdepth 1 -type f -name '*.exe' ! -name 'createdump.exe' -print -quit)
        fi
        echo ${src_file}
        return 0
    fi
    local expected="${temp_dir}/${project_stem}"
    if [ $(file_exists "${expected}") -ne 0 ]; then
        src_file=${expected}
    else
        src_file=$(find "$temp_dir" -maxdepth 1 -type f -perm -u+x -print -quit)
    fi
    echo ${src_file}
    return 0
}
build_tool_for_platform() {
    local tool_name="$1"
    local project_path="$2"
    local rid="$3"
    local bundle_dir="$4"

    local temp_dir="${OUTPUT_DIR}/temp_${tool_name}_${rid}"
    local publish_args=(${project_path} "-c" ${CONFIGURATION} "-r" ${rid} "-o" ${temp_dir})
    if (( ENABLE_SELF_CONTAINED != 0 )); then
        publish_args+=("--self-contained")
    fi
    dotnet publish "${publish_args[@]}"
    local project_stem=$(basename "$project_path" .csproj)
    local src_file=$(find_published_executable "${temp_dir}" "${rid}" "${project_stem}")
    if [[ ${src_file} == "" ]] || ! [ $(file_exists "${src_file}") -ne 0 ]; then
        echo -e "${RED}✗ Build completed but executable not found for ${tool_name}${NC}"
        rm -rf "$temp_dir"
        exit 1
    fi
    local extension=""
    if [ $(is_windows_rid "${rid}") -ne 0 ]; then
        extension=".exe"
    fi
    local dest_file="${bundle_dir}/${tool_name}${extension}"
    mv "$src_file" "$dest_file"
    rm -rf "$temp_dir"
    if ! [ $(is_windows_rid "${rid}") -ne 0 ]; then
        chmod +x "$dest_file"
    fi
    local size=$(du -h "$dest_file" | cut -f1)
    echo -e "${GREEN}✓ Built ${tool_name} (${size}) -> ${dest_file}${NC}"
}
build_platform() {
    local rid="$1"
    local description="$2"

    local bundle_dir="${OUTPUT_DIR}/$(get_output_dirname "${rid}")"
    echo ""
    echo -e "${YELLOW}Building for $description ($rid)...${NC}"
    rm -rf "$bundle_dir"
    mkdir -p "$bundle_dir"
    for entry in "${PROJECTS[@]}"; do
        local tool_name=$(printf '%s' $entry | cut -d: -f1)
        local project_path=$(printf '%s' $entry | cut -d: -f2-)
        (
            build_tool_for_platform "${tool_name}" "${project_path}" "${rid}" "${bundle_dir}"
        )
        local publish_status=$?
        if [[ ${publish_status} != 0 ]]; then
            echo -e "${RED}✗ Build failed for ${tool_name} (${rid})${NC}"
            exit $publish_status
        fi
    done
    local bundle_size=$(du -sh "$bundle_dir" | cut -f1)
    echo -e "${GREEN}✓ Bundle ready (${bundle_size}) -> ${bundle_dir}${NC}"
    ls -lah "$bundle_dir"
}
get_platform_description() {
    local rid="$1"

    case ${rid} in
        win-x64)
            echo "Windows (64-bit)"
            return 0
            ;;
        win-x86)
            echo "Windows (32-bit)"
            return 0
            ;;
        win-arm64)
            echo "Windows ARM64"
            return 0
            ;;
        linux-x64)
            echo "Linux (64-bit)"
            return 0
            ;;
        linux-arm64)
            echo "Linux ARM64"
            return 0
            ;;
        linux-arm)
            echo "Linux ARM"
            return 0
            ;;
        osx-x64)
            echo "macOS Intel"
            return 0
            ;;
        osx-arm64)
            echo "macOS Apple Silicon"
            return 0
            ;;
    esac
    echo ${rid}
    return 0
}
BUILD_PLATFORMS=()
while (( $# > 0 )); do
    arg=${@:1:1}
    if [[ ${arg} == "-h" ]] || [[ ${arg} == "--help" ]]; then
        show_help
        exit 0
    elif [[ ${arg} == "--no-self-contained" ]]; then
        ENABLE_SELF_CONTAINED=0
        __lash_shift_n=$(( 1 ))
        if (( __lash_shift_n > 0 )); then
        if (( __lash_shift_n >= $# )); then set --; else shift "${__lash_shift_n}"; fi
        fi
    else
        BUILD_PLATFORMS+=(${arg})
        __lash_shift_n=$(( 1 ))
        if (( __lash_shift_n > 0 )); then
        if (( __lash_shift_n >= $# )); then set --; else shift "${__lash_shift_n}"; fi
        fi
    fi
done
if [[ ${#BUILD_PLATFORMS[@]} == 0 ]]; then
    echo -e "${RED}Error: No platforms specified${NC}"
    echo ""
    show_help
    exit 1
fi
echo -e "${GREEN}  Building ${PROJECT_NAME}${NC}"
echo -e "${BLUE}Configuration:${NC}"
if (( ENABLE_SELF_CONTAINED != 0 )); then
    echo -e "  Self-contained: ${GREEN}Enabled${NC}"
else
    echo -e "  Self-contained: ${YELLOW}Disabled${NC}"
fi
if [ $(dir_exists "${OUTPUT_DIR}") -ne 0 ]; then
    echo -e "${YELLOW}Cleaning output directory...${NC}"
    rm -rf "$OUTPUT_DIR"
fi
mkdir -p "$OUTPUT_DIR"
echo -e "${BLUE}Building platforms: ${BUILD_PLATFORMS[*]}${NC}"
for rid in "${BUILD_PLATFORMS[@]}"; do
    description=$(get_platform_description "${rid}")
    build_platform "${rid}" "${description}"
done
echo ""
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}  Build Complete!${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""
echo "Bundles are located in:"
echo ""
find "$OUTPUT_DIR" -maxdepth 1 -mindepth 1 -type d -name "${PROJECT_NAME}-*" | sort
