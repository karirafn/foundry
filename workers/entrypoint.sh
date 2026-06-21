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

if [[ -n "${GH_TOKEN:-}" ]]; then
    GH_HOST="${CLONE_URL#https://}"
    GH_HOST="${GH_HOST%%/*}"
    if [[ -z "$GH_HOST" ]]; then
        echo "WARNING: could not derive hostname from CLONE_URL; skipping gh auth setup-git" >&2
    elif command -v gh > /dev/null 2>&1; then
        gh auth setup-git --hostname "$GH_HOST" --force
    else
        echo "WARNING: GH_TOKEN is set but the gh CLI is not present in this image." >&2
        echo "  git push and gh pr create will not authenticate." >&2
        echo "  Rebuild the worker image with INSTALL_GH=true: docker build --build-arg INSTALL_GH=true -t foundry-worker:local workers/" >&2
    fi
fi

if [[ -n "${GITLAB_TOKEN:-}" ]]; then
    GL_HOST="${CLONE_URL#https://}"
    GL_HOST="${GL_HOST%%/*}"
    if [[ -z "$GL_HOST" ]]; then
        echo "WARNING: could not derive hostname from CLONE_URL; skipping glab credential helper setup" >&2
    elif command -v glab > /dev/null 2>&1; then
        export GITLAB_HOST="https://${GL_HOST}"
        git config credential."https://${GL_HOST}".helper "!glab auth git-credential"
    else
        echo "WARNING: GITLAB_TOKEN is set but the glab CLI is not present in this image." >&2
        echo "  git push and glab mr create will not authenticate." >&2
        echo "  Rebuild the worker image with INSTALL_GLAB=true: docker build --build-arg INSTALL_GLAB=true -t foundry-worker:local workers/" >&2
    fi
fi

if [[ -n "${BRANCH_NAME:-}" ]]; then
    if [[ ! "$BRANCH_NAME" =~ ^[a-zA-Z0-9_/.-]+$ ]]; then
        echo "ERROR: BRANCH_NAME contains invalid characters: $BRANCH_NAME" >&2
        exit 1
    fi
    git switch -- "$BRANCH_NAME" || git switch -c "$BRANCH_NAME"
fi

claude -p "$WORKER_PROMPT" \
    --append-system-prompt "$SYSTEM_PROMPT" \
    --dangerously-skip-permissions \
    --output-format json \
    --max-turns 200
