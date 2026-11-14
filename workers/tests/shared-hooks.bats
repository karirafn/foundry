#!/usr/bin/env bats
# Tests for the shared role plugin's hooks (workers/roles/shared/hooks/).
# Each test feeds a hook the PreToolUse JSON Claude Code sends on stdin.

bats_require_minimum_version 1.5.0

SHARED_PLUGIN="$(cd "$(dirname "$BATS_TEST_FILENAME")/../roles/shared" && pwd)"
PROBE_HOOK="$SHARED_PLUGIN/hooks/block-self-matching-probe.sh"

# run_probe_hook <command>: runs the probe hook on a Bash PreToolUse event for <command>.
run_probe_hook() {
    local payload
    payload=$(jq -cn --arg command "$1" '{tool_name: "Bash", tool_input: {command: $command}}')
    run --separate-stderr bash "$PROBE_HOOK" <<< "$payload"
}

assert_blocked() {
    run_probe_hook "$1"
    [ "$status" -eq 2 ]
    [[ "$stderr" == *"pgrep -f matches its own shell's command line"* ]]
}

assert_passes() {
    run_probe_hook "$1"
    [ "$status" -eq 0 ]
    [ -z "$stderr" ]
}

# --- wiring ---------------------------------------------------------------------

@test "hooks.json declares a PreToolUse Bash hook running a plugin script with bash" {
    local hooks_json="$SHARED_PLUGIN/hooks/hooks.json"
    run jq -er '.hooks.PreToolUse[] | select(.matcher == "Bash") | .hooks[] | select(.type == "command") | .command' "$hooks_json"
    [ "$status" -eq 0 ]
    [[ "$output" =~ ^bash\ \"\$\{CLAUDE_PLUGIN_ROOT\}/(hooks/[A-Za-z0-9._-]+\.sh)\"$ ]]
    local script="$SHARED_PLUGIN/${BASH_REMATCH[1]}"
    [ -f "$script" ]
    bash -n "$script"
}

# --- blocked shapes ---------------------------------------------------------------

@test "blocks pgrep -f at the start of the command" {
    assert_blocked 'pgrep -f "dotnet test"'
}

@test "blocks pkill -f at the start of the command" {
    assert_blocked 'pkill -f "dotnet test"'
}

@test "blocks a short-flag bundle holding f (-af)" {
    assert_blocked 'pgrep -af dotnet'
}

@test "blocks a short-flag bundle holding f (-fl)" {
    assert_blocked 'pkill -fl dotnet'
}

@test "blocks --full" {
    assert_blocked 'pgrep --full dotnet'
}

@test "blocks a prefix of --full" {
    assert_blocked 'pgrep --fu dotnet'
}

@test "blocks the full-match flag after the pattern" {
    assert_blocked 'pgrep dotnet -f'
}

@test "blocks pgrep invoked by path" {
    assert_blocked '/usr/bin/pgrep -f dotnet'
}

@test "blocks a probe after ;" {
    assert_blocked 'sleep 5; pgrep -f dotnet'
}

@test "blocks a probe after &&" {
    assert_blocked 'sleep 5 && pkill -f dotnet'
}

@test "blocks a probe after ||" {
    assert_blocked 'false || pgrep -f dotnet'
}

@test "blocks a probe after |" {
    assert_blocked 'echo x | pgrep -f dotnet'
}

@test "blocks a probe inside a subshell" {
    assert_blocked '(pgrep -f dotnet)'
}

@test "blocks a probe inside a command substitution" {
    assert_blocked 'pids=$(pgrep -f "dotnet test")'
}

@test "blocks a probe on a later line" {
    assert_blocked $'cd /workspace\npgrep -f dotnet'
}

@test "blocks a probe in an until condition" {
    assert_blocked 'until ! pgrep -f "dotnet test"; do sleep 15; done'
}

@test "blocks a probe in a while condition" {
    assert_blocked 'while pgrep -f "dotnet test" > /dev/null; do sleep 15; done'
}

@test "blocks a probe behind sudo" {
    assert_blocked 'sudo -u root pkill -f dotnet'
}

@test "blocks a probe behind watch" {
    assert_blocked 'watch -n 5 "pgrep -f dotnet"'
}

@test "blocks a probe behind env" {
    assert_blocked 'env LC_ALL=C pgrep -f dotnet'
}

@test "blocks a probe behind nohup" {
    assert_blocked 'nohup pkill -f dotnet &'
}

@test "blocks a probe behind timeout" {
    assert_blocked 'timeout -s KILL 30 pgrep -f dotnet'
}

@test "blocks a probe after a heredoc ends" {
    assert_blocked $'cat > notes.txt <<EOF\nhello\nEOF\npgrep -f dotnet'
}

@test "the block message names the PID and the completion-marker alternatives" {
    run_probe_hook 'pkill -f dotnet'
    [ "$status" -eq 2 ]
    [[ "$stderr" == *"pkill -f can kill its caller"* ]]
    [[ "$stderr" == *'cmd > run.log 2>&1 & echo $! > run.pid'* ]]
    [[ "$stderr" == *'kill -0 "$(cat run.pid)"'* ]]
    [[ "$stderr" == *'cmd > run.log 2>&1; echo "EXIT=$?" >> run.log'* ]]
    [[ "$stderr" == *"grep -q '^EXIT=' run.log"* ]]
}

# --- passing shapes ---------------------------------------------------------------

@test "passes pgrep without full matching" {
    assert_passes 'pgrep dotnet'
}

@test "passes pgrep -x" {
    assert_passes 'pgrep -x dotnet'
}

@test "passes pkill with a signal and no full matching" {
    assert_passes 'pkill -TERM -x dotnet'
}

@test "passes pidof" {
    assert_passes 'pidof dotnet'
}

@test "passes pgrep -f inside a heredoc body" {
    assert_passes $'cat > wait.sh <<\'EOF\'\nuntil ! pgrep -f "dotnet test"; do sleep 15; done\nEOF\nbash wait.sh'
}

@test "passes pgrep -f inside a tab-stripped heredoc body" {
    assert_passes $'cat > wait.sh <<-EOF\n\tpkill -f dotnet\n\tEOF\necho done'
}

@test "passes text naming pgrep -f in a command another word starts" {
    assert_passes 'echo "pgrep -f" && grep -rn "pkill -f" docs/'
}

@test "passes a pgrep pattern after --" {
    assert_passes 'pgrep -- -f'
}

@test "passes ordinary commands" {
    assert_passes 'dotnet test > run.log 2>&1; echo "EXIT=$?" >> run.log'
}

@test "passes a tool other than Bash" {
    run --separate-stderr bash "$PROBE_HOOK" <<< '{"tool_name":"Read","tool_input":{"file_path":"pgrep -f"}}'
    [ "$status" -eq 0 ]
}
