# glab Credential Helper via GITLAB_TOKEN

## Context

GitLab-provider workers install glab but it was never authenticated — `GIT_PAT` was set but no `GITLAB_TOKEN`, and the entrypoint resets `origin` to the PAT-stripped clone URL so `git push` and `glab mr create` could not authenticate.
This mirrors the GitHub auth wiring introduced in ADR 0016 and issues #163 / #185.

## Decision

Authenticate glab non-interactively by (a) passing `GITLAB_TOKEN` (the account PAT) in the worker environment for GitLab-provider dispatches, and (b) registering glab as git's per-host credential helper in the entrypoint:

```bash
git config credential."https://<host>".helper "!glab auth git-credential"
export GITLAB_HOST="https://<host>"
```

The host is derived from `CLONE_URL` at entrypoint runtime.
glab reads the token from `GITLAB_TOKEN` and the host from `GITLAB_HOST` automatically — no interactive login or on-disk credential state is required.

**Note:** glab has no equivalent of `gh auth setup-git`, which is why this mechanism differs from the GitHub side.
On the GitHub side, `gh auth setup-git` installs the credential helper globally; on the GitLab side the git config entry must be written explicitly per host.

## Considered Options

- **`glab auth login --token`** — writes interactive-style credential state to `~/.config/glab-cli`.
  Adds redundant on-disk state given env-var auth already works and is less transparent than the git-credential-helper approach.

- **Re-embed PAT in `origin` URL** — e.g. `https://oauth2:<token>@gitlab.com/owner/repo.git`.
  Keeps secrets in git config; rejected consistently with the principle established in ADR 0016.
