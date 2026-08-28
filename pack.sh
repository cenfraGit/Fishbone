#!/usr/bin/env bash
#
# pack.sh
#
# packs every Fishbone NuGet package into artifacts/ and prints what came out.
#
#   ./pack.sh        the libraries and plugins
#   ./pack.sh -i     also the SpineIDE packages, one per platform (slow, publishes first)
#   ./pack.sh -p     purge the packed versions from the NuGet cache afterwards
#
# versions come from Directory.Build.props, and for SpineIDE from its own packaging
# csproj. there is deliberately no flag to override them here: a version that exists
# only in your shell history is one you cannot reproduce. to release a new version,
# edit the file and run this again.
#
set -euo pipefail

# resolve to the script's own directory, so it works from anywhere
cd -- "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

PROJECTS=(
    "src/Fishbone/Fishbone.csproj"
    "src/Fishbone.DebugAdapter/Fishbone.DebugAdapter.csproj"
    "plugins/Fishbone.Plugins.Math/Fishbone.Plugins.Math.csproj"
    "plugins/Fishbone.Plugins.OpenCV/Fishbone.Plugins.OpenCV.csproj"
    "plugins/Fishbone.Plugins.Halcon24111/Fishbone.Plugins.Halcon24111.csproj"
)

SPINE_RIDS=("win-x64" "linux-x64")

OUTPUT="artifacts"
CONFIGURATION="Release"

BUILD_IDE=0
PURGE_CACHE=0

while [ $# -gt 0 ]; do
    case "$1" in
        -i|--ide)   BUILD_IDE=1; shift ;;
        -p|--purge) PURGE_CACHE=1; shift ;;
        -h|--help)  awk 'NR>1 && /^#/ {sub(/^# ?/, ""); print; next} NR>1 {exit}' "$0"; exit 0 ;;
        *) echo "unknown option: $1 (try -h)" >&2; exit 1 ;;
    esac
done

mkdir -p "$OUTPUT"

# clear only what this run rebuilds. without -i the SpineIDE packages are left alone,
# since they are slow to build and versioned separately
if [ "$BUILD_IDE" -eq 1 ]; then
    rm -f "$OUTPUT"/*.nupkg
else
    find "$OUTPUT" -maxdepth 1 -name "*.nupkg" ! -name "Fishbone.SpineIDE.*" -delete 2>/dev/null || true
fi

run_step() {
    local label="$1"; shift
    local log="$OUTPUT/.step.log"
    printf '  %-30s' "$label"
    if "$@" > "$log" 2>&1; then
        echo "ok"
    else
        echo "FAILED"
        echo
        tail -25 "$log" >&2
        exit 1
    fi
}

for project in "${PROJECTS[@]}"; do
    run_step "$(basename "$project" .csproj)" dotnet pack "$project" --configuration "$CONFIGURATION" --output "$OUTPUT"
done

if [ "$BUILD_IDE" -eq 1 ]; then
    for rid in "${SPINE_RIDS[@]}"; do
        publish_dir="ide/Fishbone.SpineIDE/bin/publish/$rid"
        rm -rf "$publish_dir"
        run_step "SpineIDE $rid publish" dotnet publish ide/Fishbone.SpineIDE/Fishbone.SpineIDE.csproj --configuration "$CONFIGURATION" --runtime "$rid" --self-contained false --output "$publish_dir"
        run_step "SpineIDE $rid pack" dotnet pack ide/Fishbone.SpineIDE.Package/Fishbone.SpineIDE.Package.csproj --configuration "$CONFIGURATION" --output "$OUTPUT" -p:SpineRid="$rid" -p:SpinePublishDir="$(pwd)/$publish_dir"
    done
fi

rm -f "$OUTPUT"/.step.log

# --------------------------------------------------------------------------------
# look inside. a package that builds but contains the wrong thing is the normal
# failure here, so never just trust the exit code
# --------------------------------------------------------------------------------

echo
if ! command -v unzip > /dev/null 2>&1; then
    ls -1 "$OUTPUT"/*.nupkg
    echo "(install unzip to see package contents)"
    exit 0
fi

# "Fishbone.Plugins.Math.0.1.0-alpha.1.nupkg" -> id and version
package_id() { basename "$1" .nupkg | sed -E 's/[.][0-9]+[.][0-9]+[.][0-9]+(-[0-9A-Za-z.]+)?$//'; }
package_version() {
    local base id
    base="$(basename "$1" .nupkg)"
    id="$(package_id "$1")"
    echo "${base#"$id".}"
}

ENGINE_PARTS="Fishbone.Core.dll Fishbone.Parser.dll Fishbone.Interpreter.dll Fishbone.Debugging.dll"

for package in "$OUTPUT"/*.nupkg; do
    id="$(package_id "$package")"
    extracted="$OUTPUT/.inspect"
    rm -rf "$extracted"
    unzip -o -q "$package" -d "$extracted"

    echo "$id  $(package_version "$package")"

    # content-only packages (SpineIDE) have no lib folder at all, and a failing find
    # would take the whole script down under pipefail
    if [ -d "$extracted/lib" ]; then
        find "$extracted/lib" -name "*.dll" | sort | while read -r dll; do
            echo "    lib  $(basename "$dll")"
        done
    fi

    grep -oE 'id="[^"]*" version="[^"]*"' "$extracted"/*.nuspec 2>/dev/null | sed -E 's/id="([^"]*)" version="([^"]*)"/    dep  \1 \2/' || true

    # a plugin carrying its own copy of the engine is what produces
    # "assembly manifest definition does not match" when it loads
    if [ "$id" != "Fishbone" ] && [ -d "$extracted/lib" ]; then
        for part in $ENGINE_PARTS; do
            if find "$extracted/lib" -name "$part" | grep -q .; then
                echo "    WARNING: ships $part, it should depend on Fishbone instead"
            fi
        done
    fi
    echo
done

rm -rf "$OUTPUT/.inspect"

# --------------------------------------------------------------------------------
# the cache. installing a version copies it under ~/.nuget/packages, and repacking
# the same version is not picked up by a consumer until that copy is gone
# --------------------------------------------------------------------------------

CACHE="${NUGET_PACKAGES:-$HOME/.nuget/packages}"

if [ "$PURGE_CACHE" -eq 1 ]; then
    for package in "$OUTPUT"/*.nupkg; do
        lower="$(package_id "$package" | tr '[:upper:]' '[:lower:]')"
        rm -rf "$CACHE/$lower/$(package_version "$package")"
    done
    echo "purged from $CACHE"
    echo "run 'dotnet restore --force' in the consuming project"
else
    echo "note: consumers that already installed these versions have them cached in"
    echo "      $CACHE and will not see this build. rerun with -p to clear them"
fi
