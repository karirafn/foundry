#!/bin/bash
# Prints the worker image's toolchain as exactly one stdout line:
#   FOUNDRY_TOOLCHAIN [{"name":"node","version":"22.22.3"}, ...]
# Foundry runs it once against every new image (ADR 0109) and gates toolchain-dependent prompt
# sections on the list it prints. A tool that is absent, or whose version command fails or hangs,
# is omitted rather than reported as an error, so one broken tool never hides the others.
set -uo pipefail

# Overridable only so the bats suite can point at fixtures; the image never sets them.
CLAUDE_CODE_DIR="${FOUNDRY_TOOLCHAIN_CLAUDE_CODE_DIR:-/opt/claude-code}"
VERSION_TIMEOUT="${FOUNDRY_TOOLCHAIN_TIMEOUT:-20}"

entries=()

# run_version <command...>
# Runs a version command under the timeout, printing its first stdout line; fails when the
# command is missing, fails, hangs or prints nothing.
run_version() {
    local output
    output="$(timeout "$VERSION_TIMEOUT" "$@" 2>/dev/null)" || return 1
    output="${output%%$'\n'*}"
    [ -n "$output" ] || return 1
    printf '%s\n' "$output"
}

# extract_version <text>
# Prints the first dotted version number in <text> (keeping a -preview/+build suffix), so
# "Docker version 29.9.3, build abc" yields 29.9.3 and "v22.22.3" yields 22.22.3.
extract_version() {
    if [[ "$1" =~ ([0-9]+(\.[0-9]+)+([-+][0-9A-Za-z.-]+)?) ]]; then
        printf '%s\n' "${BASH_REMATCH[1]}"
        return 0
    fi
    return 1
}

# report <name> <command...>
# Adds <name> with the version its command prints, or nothing when the command yields none.
report() {
    local name="$1"
    shift
    command -v "$1" >/dev/null 2>&1 || return 0
    local raw version
    raw="$(run_version "$@")" || return 0
    version="$(extract_version "$raw")" || return 0
    entries+=("$name" "$version")
}

# report_package <name> <package.json path>
report_package() {
    local name="$1" manifest="$2"
    [ -f "$manifest" ] || return 0
    report "$name" jq -er .version "$manifest"
}

# report_dotnet_sdks
# Adds one dotnet-sdk entry per line of `dotnet --list-sdks` ("10.0.404 [/usr/share/dotnet/sdk]"), so
# an SDK the image skipped shows as a missing entry rather than hiding behind the default SDK.
report_dotnet_sdks() {
    command -v dotnet >/dev/null 2>&1 || return 0
    local output line version
    output="$(timeout "$VERSION_TIMEOUT" dotnet --list-sdks 2>/dev/null)" || return 0
    while IFS= read -r line; do
        version="$(extract_version "${line%% \[*}")" || continue
        entries+=("dotnet-sdk" "$version")
    done <<<"$output"
}

report node node --version
report_package claude-code "$CLAUDE_CODE_DIR/node_modules/@anthropic-ai/claude-code/package.json"
report_dotnet_sdks
report csharp-ls csharp-ls --version
if command -v npm >/dev/null 2>&1 && npm_root="$(run_version npm root -g)"; then
    report_package angular-cli "$npm_root/@angular/cli/package.json"
fi
if [ -n "${CHROME_BIN:-}" ]; then
    report chromium "$CHROME_BIN" --version
fi
report docker docker --version
report_package playwright-core "$CLAUDE_CODE_DIR/node_modules/playwright-core/package.json"
report_package corepack "$CLAUDE_CODE_DIR/node_modules/corepack/package.json"
report gh gh --version
report glab glab --version
report git git --version

json="$(jq -cn '[range(0; $ARGS.positional | length; 2) as $i
                 | {name: $ARGS.positional[$i], version: $ARGS.positional[$i + 1]}]' \
        --args "${entries[@]}")" || exit 1
printf 'FOUNDRY_TOOLCHAIN %s\n' "$json"
