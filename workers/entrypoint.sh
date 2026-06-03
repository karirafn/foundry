#!/bin/bash
set -euo pipefail

# Required environment variables
if [[ -z "${ANTHROPIC_API_KEY:-}" ]] && [[ -z "${CLAUDE_CODE_OAUTH_TOKEN:-}" ]]; then
    echo "Either ANTHROPIC_API_KEY or CLAUDE_CODE_OAUTH_TOKEN is required" >&2
    exit 1
fi
: "${CLONE_URL:?CLONE_URL is required}"
: "${GIT_PAT:?GIT_PAT is required}"
: "${WORKER_PROMPT:?WORKER_PROMPT is required}"
: "${SYSTEM_PROMPT:?SYSTEM_PROMPT is required}"
: "${ISSUE_NUMBER:?ISSUE_NUMBER is required}"

if [[ -n "${CLAUDE_SETTINGS_JSON:-}" ]]; then
    if [[ ! -w ~/.claude ]]; then
        echo "ERROR: ~/.claude is not writable by $(whoami). Rebuild the worker image: docker build -t foundry-worker:local workers/" >&2
        exit 1
    fi
    printf '%s\n' "$CLAUDE_SETTINGS_JSON" > ~/.claude/settings.json
    chmod 444 ~/.claude/settings.json
fi

# Transform https://github.com/owner/repo -> https://<PAT>@github.com/owner/repo.git
AUTHENTICATED_URL="${CLONE_URL/#https:\/\//https:\/\/${GIT_PAT}@}"
if [[ "$AUTHENTICATED_URL" != *.git ]]; then
    AUTHENTICATED_URL="${AUTHENTICATED_URL}.git"
fi

git clone "$AUTHENTICATED_URL" /workspace
git -C /workspace remote set-url origin "$CLONE_URL"

cd /workspace

if [[ -n "${BRANCH_NAME:-}" ]]; then
    git switch "$BRANCH_NAME"
fi

claude -p "$WORKER_PROMPT" \
    --append-system-prompt "$SYSTEM_PROMPT" \
    --dangerously-skip-permissions \
    --max-turns 200
