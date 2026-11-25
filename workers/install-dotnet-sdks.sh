#!/bin/bash
# Installs dotnet-sdk-<channel> for every .NET channel Microsoft still supports (ADR 0109).
#   install-dotnet-sdks.sh <releases-index.json>         install the selected SDKs
#   install-dotnet-sdks.sh --list <releases-index.json>  print the selected channels, install nothing
# A channel is selected when its support-phase is active or maintenance; preview and go-live builds are
# not reliably on the prod apt feed, and eol channels are unsupported. A selected channel with no apt
# package (a release-day feed lag, or a version the Debian release never got) is skipped with a warning
# so it costs only the repositories on that version; the script fails when nothing is left to install.
set -euo pipefail

usage() {
    echo "usage: install-dotnet-sdks.sh [--list] <releases-index.json>" >&2
    exit 2
}

list_only=false
if [ "${1:-}" = "--list" ]; then
    list_only=true
    shift
fi
[ "$#" -eq 1 ] || usage
index="$1"

[ -r "$index" ] || { echo "install-dotnet-sdks: cannot read $index" >&2; exit 1; }

# A channel version becomes part of a package name, so anything but major.minor is rejected.
if ! channels="$(jq -r '
        .["releases-index"]
        | if type == "array" then . else error("no releases-index array") end
        | map(select(.["support-phase"] == "active" or .["support-phase"] == "maintenance")
              | .["channel-version"]
              | if type == "string" and test("^[0-9]+\\.[0-9]+$") then . else error("bad channel-version \(.)") end)
        | .[]' "$index" 2>&1)"; then
    echo "install-dotnet-sdks: cannot parse $index: $channels" >&2
    exit 1
fi
[ -n "$channels" ] || { echo "install-dotnet-sdks: $index lists no active or maintenance channel" >&2; exit 1; }

if $list_only; then
    printf '%s\n' "$channels"
    exit 0
fi

packages=()
while IFS= read -r channel; do
    package="dotnet-sdk-$channel"
    if apt-cache show "$package" >/dev/null 2>&1; then
        packages+=("$package")
    else
        echo "install-dotnet-sdks: warning: no apt package $package, skipping .NET $channel" >&2
    fi
done <<<"$channels"

[ "${#packages[@]}" -gt 0 ] || { echo "install-dotnet-sdks: no selected .NET SDK has an apt package" >&2; exit 1; }

apt-get install -y --no-install-recommends "${packages[@]}"
