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

# a host on each framework the packages ship for, and each gets the engine built for it
for framework in net8.0:8.0 net10.0:10.0; do
    tfm="${framework%%:*}"
    out="$work/out-$tfm"
    dotnet build tests/packaging/Consumer/Consumer.csproj --configuration Release --framework "$tfm" \
        -p:RestorePackagesPath="$work/packages" --output "$out"

    output="$(dotnet "$out/Consumer.dll")"
    echo "$output"
    grep -qx "result 5" <<< "$output" || { echo "FAILED: the script didn't run on $tfm" >&2; exit 1; }
    grep -qx "adapter Fishbone.DebugAdapter" <<< "$output" || { echo "FAILED: the debug adapter didn't load on $tfm" >&2; exit 1; }
    grep -qx "engine .NETCoreApp,Version=v${framework##*:}" <<< "$output" || { echo "FAILED: $tfm didn't get the $tfm engine" >&2; exit 1; }

    if [ "$windows" -eq 1 ]; then
        for file in spineide/spineide.exe spineide/daphost/fishbone-dap.dll; do
            [ -f "$out/$file" ] || { echo "FAILED: $file is missing from the host's output" >&2; exit 1; }
        done
        # SpineIDE is built for .NET 8 and runs on a newer one when 8 isn't installed
        for config in spineide/spineide.runtimeconfig.json spineide/daphost/fishbone-dap.runtimeconfig.json; do
            grep -q '"rollForward": "Major"' "$out/$config" || { echo "FAILED: $config doesn't roll forward" >&2; exit 1; }
        done
    fi
done

echo "packages ok"
