#!/usr/bin/env bats
# Tests for toolchain.sh. Every tool is a stub in a per-test directory, and PATH holds only that
# directory, which holds the few utilities the script itself needs, so the host's real tools never leak in.

bats_require_minimum_version 1.5.0

SCRIPT="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/toolchain.sh"

setup() {
    STUB_DIR="$BATS_TEST_TMPDIR/bin"
    mkdir -p "$STUB_DIR"
    local tool
    for tool in jq timeout; do
        ln -s "$(command -v "$tool")" "$STUB_DIR/$tool"
    done
    SLEEP_BIN="$(command -v sleep)"

    CLAUDE_CODE_DIR="$BATS_TEST_TMPDIR/claude-code"
    NPM_ROOT="$BATS_TEST_TMPDIR/npm-global"

    export FOUNDRY_TOOLCHAIN_CLAUDE_CODE_DIR="$CLAUDE_CODE_DIR"
    export FOUNDRY_TOOLCHAIN_TIMEOUT=1
    unset CHROME_BIN
}

# run_script [run flags]: runs the script with PATH limited to the stub directory.
run_script() {
    run "$@" env PATH="$STUB_DIR" "$SCRIPT"
}

# stub <name> <stdout>: an executable printing <stdout> and exiting 0.
stub() {
    printf '#!/bin/bash\nprintf "%%s\\n" %q\n' "$2" >"$STUB_DIR/$1"
    chmod +x "$STUB_DIR/$1"
}

# package <path> <version>: a package.json declaring <version>.
package() {
    mkdir -p "$1"
    printf '{"name":"pkg","version":"%s"}\n' "$2" >"$1/package.json"
}

stub_every_tool() {
    stub node "v22.22.3"
    package "$CLAUDE_CODE_DIR/node_modules/@anthropic-ai/claude-code" "2.1.319"
    stub dotnet "10.0.100"
    stub csharp-ls "csharp-ls, 0.28.0 (Release)+0123abcd"
    stub npm "$NPM_ROOT"
    package "$NPM_ROOT/@angular/cli" "21.2.30"
    stub chromium "Chromium 156.0.8078.79"
    export CHROME_BIN="$STUB_DIR/chromium"
    stub docker "Docker version 29.9.3, build 1a2b3c4"
    package "$CLAUDE_CODE_DIR/node_modules/playwright-core" "1.65.1"
    printf '#!/bin/bash\nprintf "gh version 2.63.0 (2024-11-27)\\nhttps://github.com/cli/cli/releases/tag/v2.63.0\\n"\n' >"$STUB_DIR/gh"
    chmod +x "$STUB_DIR/gh"
    stub glab "glab 1.46.1 (abcdef0)"
    stub git "git version 2.39.5"
}

# json_of <output>: the JSON after the FOUNDRY_TOOLCHAIN marker.
json_of() {
    printf '%s' "${1#FOUNDRY_TOOLCHAIN }"
}

@test "every present tool is reported with its version, in order" {
    stub_every_tool

    run_script --separate-stderr

    [ "$status" -eq 0 ]
    expected='[{"name":"node","version":"22.22.3"},{"name":"claude-code","version":"2.1.319"},{"name":"dotnet-sdk","version":"10.0.100"},{"name":"csharp-ls","version":"0.28.0"},{"name":"angular-cli","version":"21.2.30"},{"name":"chromium","version":"156.0.8078.79"},{"name":"docker","version":"29.9.3"},{"name":"playwright-core","version":"1.65.1"},{"name":"gh","version":"2.63.0"},{"name":"glab","version":"1.46.1"},{"name":"git","version":"2.39.5"}]'
    [ "$output" = "FOUNDRY_TOOLCHAIN $expected" ]
}

@test "an absent tool is omitted, not an error" {
    stub_every_tool
    rm "$STUB_DIR/docker"
    rm -r "$CLAUDE_CODE_DIR/node_modules/playwright-core"

    run_script --separate-stderr

    [ "$status" -eq 0 ]
    names="$(json_of "$output" | jq -r '[.[].name] | join(",")')"
    [ "$names" = "node,claude-code,dotnet-sdk,csharp-ls,angular-cli,chromium,gh,glab,git" ]
}

@test "no tools at all yields an empty list" {
    run_script --separate-stderr

    [ "$status" -eq 0 ]
    [ "$output" = "FOUNDRY_TOOLCHAIN []" ]
}

@test "a tool whose version command fails is omitted" {
    stub_every_tool
    printf '#!/bin/bash\necho "dotnet: broken" >&2\nexit 3\n' >"$STUB_DIR/dotnet"

    run_script --separate-stderr

    [ "$status" -eq 0 ]
    json_of "$output" | jq -e 'map(.name) | index("dotnet-sdk") == null'
    json_of "$output" | jq -e 'map(.name) | index("csharp-ls") != null'
}

@test "a tool whose version command hangs is omitted after the timeout" {
    stub_every_tool
    printf '#!/bin/bash\nexec %q 30\n' "$SLEEP_BIN" >"$STUB_DIR/csharp-ls"

    SECONDS=0
    run_script --separate-stderr
    elapsed=$SECONDS

    [ "$status" -eq 0 ]
    [ "$elapsed" -lt 10 ]
    json_of "$output" | jq -e 'map(.name) | index("csharp-ls") == null'
    json_of "$output" | jq -e 'map(.name) | index("git") != null'
}

@test "the output is exactly one line of valid JSON, even when tools write to stderr" {
    stub_every_tool
    printf '#!/bin/bash\necho noise >&2\necho "git version 2.39.5"\n' >"$STUB_DIR/git"

    run_script

    [ "$status" -eq 0 ]
    [ "${#lines[@]}" -eq 1 ]
    [[ "${lines[0]}" == "FOUNDRY_TOOLCHAIN "* ]]
    json_of "${lines[0]}" | jq -e 'type == "array" and length == 11'
}

@test "an unset CHROME_BIN omits chromium" {
    stub_every_tool
    unset CHROME_BIN

    run_script --separate-stderr

    [ "$status" -eq 0 ]
    json_of "$output" | jq -e 'map(.name) | index("chromium") == null'
}
