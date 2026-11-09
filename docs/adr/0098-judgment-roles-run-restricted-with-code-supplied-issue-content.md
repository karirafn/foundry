# Judgment Roles Run Restricted, with Issue Content Supplied by Code

> **2026-11-09 — Amended:** the Decision's clause that a `Restricted` run uses `GIT_PAT` for a shallow clone and then unsets it is replaced by [ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md): every role clones from a base bundle prepared by the Publisher, and no worker container spec contains a provider credential.

## Context

[ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md) requires that a step which only classifies runs without command-execution tools (`--restricted`). Every worker container is currently set up for implementation work: the account PAT is injected as `GH_TOKEN`/`GITLAB_TOKEN`, a branch is created on the provider before the run, the CLI runs with `--dangerously-skip-permissions`, and the prompt tells the agent to read the issue itself from `ISSUE_API_URL`. On CLI 2.1.319, `--restricted` removes Bash, the other code-running tools and WebFetch, confines the file tools to the working directories, and refuses `bypassPermissions`. A restricted agent can therefore neither skip permissions nor fetch the issue it is asked to judge.

## Decision

Each Worker Role declares a capability profile as a closed type on its role definition, `WorkerCapability = Restricted | Full`. The entrypoint and the container spec branch on the profile, never on the role's name. `implement` is `Full`; `triage` is `Restricted`.

A `Restricted` run:

- starts `claude` with `--restricted --strict-mcp-config` and without `--dangerously-skip-permissions`. In `-p` mode the read-only file tools need no approval, and anything that would need approval is denied;
- receives no provider token. `GIT_PAT` is used for a shallow clone of the default branch and then unset, and `GH_TOKEN`/`GITLAB_TOKEN` are not set;
- gets no branch created on the provider;
- gets the issue body and comments from Foundry's code at dispatch, through an `IIssueProvider` read, rendered into the prompt as XML-encoded untrusted data;
- uses its role's own system-prompt sections, with no push, pull-request or checks instructions.

The output of a restricted role is only its schema-validated verdict, so a prompt injection in issue content can at worst produce a wrong verdict. For triage, a wrong verdict is a false "ready", which is the same as dispatching without triage.

## Considered Options

- **`--restricted` alone, in the same container as implement**: rejected because the container would still hold a write-capable PAT and a branch created on the provider. Restricted tools alone do not remove the most valuable thing in the container.
- **No restriction, with limits stated in the prompt**: rejected as the agent-driven option ADR 0094 already rejects.
- **A direct Messages API call from Foundry instead of a container**: rejected because triage needs the repository to check that the files and concepts an issue names exist, because prompts would leave the image-pinned role folders of [ADR 0095](0095-worker-roles-ship-in-the-worker-image.md), and because it is unverified that Messages API inference works with the workers' OAuth session.
- **A role-name switch in the entrypoint**: rejected because each new role would add another branch, and a role could end up with half a profile.

## Consequences

- Foundry reads issue bodies and comments from the provider, which it has not done since the `DropIssueBody` migration.
- Whether `--restricted` combined with `--dangerously-skip-permissions` fails at startup or is silently downgraded is unverified. The restricted profile never passes the skip flag, so the design does not depend on the answer.
- Run listings show the role of each run, because more than one role can now appear in an issue's history.
