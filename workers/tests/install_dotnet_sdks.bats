#!/usr/bin/env bats
# Tests for install-dotnet-sdks.sh. apt-cache and apt-get are stubs in a per-test directory that record
# what they were asked for; the index fixtures follow the shape of Microsoft's releases-index.json
# (captured from https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json on
# 2026-11-25, trimmed to the fields the script reads).

bats_require_minimum_version 1.5.0

SCRIPT="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/install-dotnet-sdks.sh"

setup() {
    STUB_DIR="$BATS_TEST_TMPDIR/bin"
    mkdir -p "$STUB_DIR"
    ln -s "$(command -v jq)" "$STUB_DIR/jq"
    APT_LOG="$BATS_TEST_TMPDIR/apt-get.log"
    AVAILABLE="$BATS_TEST_TMPDIR/available"
    : >"$AVAILABLE"
    INDEX="$BATS_TEST_TMPDIR/releases-index.json"

    # apt-cache show succeeds only for packages listed in $AVAILABLE.
    cat >"$STUB_DIR/apt-cache" <<EOF
#!/bin/bash
[ "\$1" = show ] || exit 100
/bin/grep -qx -- "\$2" "$AVAILABLE"
EOF
    cat >"$STUB_DIR/apt-get" <<EOF
#!/bin/bash
printf '%s\n' "\$*" >>"$APT_LOG"
EOF
    chmod +x "$STUB_DIR/apt-cache" "$STUB_DIR/apt-get"
}

# index <channel:phase>...: writes a releases index with one release per argument.
index() {
    local pairs=()
    local arg
    for arg in "$@"; do
        pairs+=("${arg%%:*}" "${arg#*:}")
    done
    jq -n '{"releases-index": [range(0; $ARGS.positional | length; 2) as $i
             | {"channel-version": $ARGS.positional[$i], "support-phase": $ARGS.positional[$i + 1],
                "product": ".NET"}]}' --args "${pairs[@]}" >"$INDEX"
}

# available <package>...: makes apt-cache know <package>.
available() {
    printf '%s\n' "$@" >>"$AVAILABLE"
}

run_script() {
    run --separate-stderr env PATH="$STUB_DIR" /bin/bash "$SCRIPT" "$@"
}

installed() {
    [ -f "$APT_LOG" ] && cat "$APT_LOG"
}

@test "--list prints the active and maintenance channels" {
    index 11.0:active 10.0:maintenance

    run_script --list "$INDEX"

    [ "$status" -eq 0 ]
    [ "$output" = $'11.0\n10.0' ]
}

@test "eol, preview and go-live channels are not selected" {
    index 12.0:preview 11.0:go-live 10.0:active 9.0:eol

    run_script --list "$INDEX"

    [ "$status" -eq 0 ]
    [ "$output" = "10.0" ]
}

@test "--list installs nothing" {
    index 10.0:active
    available dotnet-sdk-10.0

    run_script --list "$INDEX"

    [ "$status" -eq 0 ]
    [ -z "$(installed)" ]
}

@test "every active and maintenance channel is installed, and the others are not" {
    index 12.0:preview 11.0:active 10.0:maintenance 9.0:eol 8.0:go-live
    available dotnet-sdk-12.0 dotnet-sdk-11.0 dotnet-sdk-10.0 dotnet-sdk-9.0 dotnet-sdk-8.0

    run_script "$INDEX"

    [ "$status" -eq 0 ]
    [ "$(installed)" = "install -y --no-install-recommends dotnet-sdk-11.0 dotnet-sdk-10.0" ]
}

@test "an index that selects no channel fails" {
    index 9.0:eol 12.0:preview

    run_script "$INDEX"

    [ "$status" -ne 0 ]
    [[ "$stderr" == *"no active or maintenance"* ]]
    [ -z "$(installed)" ]
}

@test "a malformed index fails" {
    printf '{"releases-index": [' >"$INDEX"

    run_script "$INDEX"

    [ "$status" -ne 0 ]
    [[ "$stderr" == *"$INDEX"* ]]
    [ -z "$(installed)" ]
}

@test "an index without a releases-index array fails" {
    printf '{"releases": []}' >"$INDEX"

    run_script --list "$INDEX"

    [ "$status" -ne 0 ]
    [ -z "$output" ]
}

@test "a channel version that is not major.minor fails" {
    index '10.0 evil:active'

    run_script "$INDEX"

    [ "$status" -ne 0 ]
    [ -z "$(installed)" ]
}

@test "an unreadable index fails" {
    run_script "$BATS_TEST_TMPDIR/missing.json"

    [ "$status" -ne 0 ]
    [[ "$stderr" == *"missing.json"* ]]
}

@test "a missing index argument fails with usage" {
    run_script

    [ "$status" -ne 0 ]
    [[ "$stderr" == *"usage"* ]]
}

@test "a channel with no apt package is skipped with a warning naming it, and the others install" {
    index 11.0:active 10.0:active
    available dotnet-sdk-10.0

    run_script "$INDEX"

    [ "$status" -eq 0 ]
    [[ "$stderr" == *"dotnet-sdk-11.0"* ]]
    [ "$(installed)" = "install -y --no-install-recommends dotnet-sdk-10.0" ]
}

@test "when no selected channel has an apt package the script fails" {
    index 11.0:active 10.0:maintenance

    run_script "$INDEX"

    [ "$status" -ne 0 ]
    [[ "$stderr" == *"dotnet-sdk-11.0"* ]]
    [[ "$stderr" == *"dotnet-sdk-10.0"* ]]
    [ -z "$(installed)" ]
}

@test "a failing apt-get install fails the script" {
    index 10.0:active
    available dotnet-sdk-10.0
    printf '#!/bin/bash\nexit 100\n' >"$STUB_DIR/apt-get"

    run_script "$INDEX"

    [ "$status" -ne 0 ]
}
