#!/usr/bin/env bash
#
# check.sh
#
# packs everything, builds a fresh host from the packages and runs it. on windows it also
# checks that the SpineIDE package copied the IDE next to the host.
#
set -euo pipefail
cd -- "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"

windows=0
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) windows=1 ;; esac

if [ "$windows" -eq 1 ]; then bash ./pack.sh -i; else bash ./pack.sh; fi

# a package cache of its own, so a stale copy of the same version can't be used
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
dotnet build tests/packaging/Consumer/Consumer.csproj --configuration Release \
    -p:RestorePackagesPath="$work/packages" --output "$work/out"

output="$(dotnet "$work/out/Consumer.dll")"
echo "$output"
grep -qx "result 5" <<< "$output" || { echo "FAILED: the script didn't run" >&2; exit 1; }
grep -qx "adapter Fishbone.DebugAdapter" <<< "$output" || { echo "FAILED: the debug adapter didn't load" >&2; exit 1; }

if [ "$windows" -eq 1 ]; then
    for file in spineide/spineide.exe spineide/daphost/fishbone-dap.dll; do
        [ -f "$work/out/$file" ] || { echo "FAILED: $file is missing from the host's output" >&2; exit 1; }
    done
fi

echo "packages ok"
