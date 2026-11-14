#!/usr/bin/env bash
# PreToolUse hook (Bash): blocks a pgrep/pkill probe that matches full command
# lines (-f, a short-flag bundle holding f, or --full and its prefixes).
#
# The shell running the probe holds the pattern in its own command line, so the
# probe always finds itself: a wait loop on it never ends and pkill -f can kill
# its caller. Exit status 2 with a message on stderr makes Claude Code refuse
# the call and hand the message to the agent.
#
# Only a probe that starts a command is blocked: at the start of the input,
# after ; && || | & ( $( or a backtick, after a shell keyword (until, while,
# if, do, ...) or behind sudo, watch, env, nohup or timeout. Quoting is not
# tracked, and heredoc bodies are ignored.

set -uo pipefail

command_text=$(jq -r 'if .tool_name == "Bash" then .tool_input.command // "" else "" end') || exit 0

# strip_heredoc_bodies <text>: prints <text> without the bodies of its heredocs.
strip_heredoc_bodies() {
    local line rest delimiter
    local -a pending=() strip_tabs=()
    while IFS= read -r line || [[ -n "$line" ]]; do
        if (( ${#pending[@]} > 0 )); then
            local candidate="$line"
            if [[ "${strip_tabs[0]}" == "1" ]]; then
                candidate="${candidate#"${candidate%%[!$'\t']*}"}"
            fi
            if [[ "$candidate" == "${pending[0]}" ]]; then
                pending=("${pending[@]:1}")
                strip_tabs=("${strip_tabs[@]:1}")
            fi
            continue
        fi
        printf '%s\n' "$line"
        # A here-string (<<<) has no body.
        rest="${line//<<</ }"
        while [[ "$rest" =~ \<\<(-?)[[:space:]]*[\'\"]?([A-Za-z0-9_]+)[\'\"]? ]]; do
            delimiter="${BASH_REMATCH[2]}"
            pending+=("$delimiter")
            if [[ -n "${BASH_REMATCH[1]}" ]]; then strip_tabs+=("1"); else strip_tabs+=("0"); fi
            rest="${rest#*"${BASH_REMATCH[0]}"}"
        done
    done <<< "$1"
}

# is_full_match_flag <word>: succeeds for -f, a short-flag bundle holding f, or a prefix of --full.
is_full_match_flag() {
    [[ "$1" =~ ^-[A-Za-z]*f[A-Za-z]*$ || "$1" =~ ^--f(u(l(l)?)?)?$ ]]
}

# probe_uses_full_match <words...>: succeeds when the probe's arguments ask for full-command-line matching.
probe_uses_full_match() {
    local word
    for word in "$@"; do
        [[ "$word" == "--" ]] && return 1
        is_full_match_flag "$word" && return 0
    done
    return 1
}

# segment_starts_full_probe <words...>: succeeds when the command these words start is a full-match probe.
segment_starts_full_probe() {
    local -a words=("$@")
    local i=0 count=${#words[@]} word
    while (( i < count )); do
        word="${words[i]}"
        case "$word" in
            if|then|else|elif|do|while|until|time|'!')
                (( i++ )) ;;
            sudo|watch|env|nohup|timeout)
                (( i++ ))
                # Skip the wrapper's options (and their arguments), assignments and durations.
                while (( i < count )); do
                    word="${words[i]}"
                    case "$word" in
                        -u|-g|-n|-s|-k|-C|-D|-h|-p|-r|-t|-U|--user|--group|--signal|--kill-after|--interval|--unset|--chdir)
                            (( i += 2 )) ;;
                        -*) (( i++ )) ;;
                        *)
                            if [[ "$word" =~ ^[A-Za-z_][A-Za-z0-9_]*= || "$word" =~ ^[0-9.]+[smhd]?$ ]]; then
                                (( i++ ))
                            else
                                break
                            fi ;;
                    esac
                done ;;
            *)
                if [[ "$word" =~ ^[A-Za-z_][A-Za-z0-9_]*= ]]; then
                    (( i++ ))
                    continue
                fi
                case "${word##*/}" in
                    pgrep|pkill) probe_uses_full_match "${words[@]:i+1}" ;;
                    *) return 1 ;;
                esac
                return ;;
        esac
    done
    return 1
}

script=$(strip_heredoc_bodies "$command_text")
script="${script//[\"\'\\]/}"
script="${script//&&/$'\n'}"
script="${script//||/$'\n'}"
script="${script//[;|&()\`\{\}]/$'\n'}"

blocked=0
while IFS= read -r segment; do
    read -ra words <<< "$segment"
    (( ${#words[@]} == 0 )) && continue
    if segment_starts_full_probe "${words[@]}"; then
        blocked=1
        break
    fi
done <<< "$script"

(( blocked == 0 )) && exit 0

cat >&2 <<'MESSAGE'
Blocked: pgrep -f matches its own shell's command line, so a waiter never sees the run end, and pkill -f can kill its caller.
Track the background run another way:
  (1) record its PID: cmd > run.log 2>&1 & echo $! > run.pid
      and later test it with: kill -0 "$(cat run.pid)"
  (2) append a completion marker: cmd > run.log 2>&1; echo "EXIT=$?" >> run.log
      and later wait for: grep -q '^EXIT=' run.log
MESSAGE
exit 2
