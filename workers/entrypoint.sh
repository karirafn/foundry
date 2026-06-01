#!/bin/bash
set -euo pipefail

# Required environment variables
: "${ANTHROPIC_API_KEY:?ANTHROPIC_API_KEY is required}"
: "${CLONE_URL:?CLONE_URL is required}"
: "${GIT_PAT:?GIT_PAT is required}"
: "${WORKER_PROMPT:?WORKER_PROMPT is required}"
: "${SYSTEM_PROMPT:?SYSTEM_PROMPT is required}"
: "${ISSUE_NUMBER:?ISSUE_NUMBER is required}"

# Transform https://github.com/owner/repo -> https://<PAT>@github.com/owner/repo.git
AUTHENTICATED_URL="${CLONE_URL/#https:\/\//https:\/\/${GIT_PAT}@}"
if [[ "$AUTHENTICATED_URL" != *.git ]]; then
    AUTHENTICATED_URL="${AUTHENTICATED_URL}.git"
fi

git clone "$AUTHENTICATED_URL" /workspace
git -C /workspace remote set-url origin "$CLONE_URL"

cd /workspace

if [[ -n "${BRANCH_NAME:-}" ]]; then
    git checkout -- "$BRANCH_NAME"
fi

claude -p "$WORKER_PROMPT" \
    --append-system-prompt "$SYSTEM_PROMPT" \
    --dangerously-skip-permissions \
    --max-turns 200
