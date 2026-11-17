# A Restricted Review Run Reads a Code-Prepared Packet at a Read-Only Commit

## Context

[ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md) runs a judgment step without command-execution tools, and [ADR 0098](0098-judgment-roles-run-restricted-with-code-supplied-issue-content.md) gives a `Restricted` run only the default branch.
A reviewer must see the pull request's code and what changed, yet `--restricted` removes Bash, so it cannot run `git diff`.
In the entrypoint, `BRANCH_NAME` means the branch a run writes to, and its presence is what produces a Run bundle for the Publisher to push ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)).
On Claude Code 2.1.325, the version `workers/package-lock.json:9` pins, `--help` says `--restricted` "confines the file tools to the working directories (--add-dir included)".

## Decision

The `review` role stays `Restricted`.
Code prepares everything it can compute, and the reviewer reads it from a read-only directory.

- **A separate read-only input** — the review dispatch context carries the reviewed commit, and the entrypoint checks it out detached from a new `REVIEW_COMMIT` variable.
  `BRANCH_NAME` stays unset, so no Run bundle is written and nothing can be published.
  The Publisher puts that commit in the base bundle because of the dispatch context, never because of the role's name.
- **A review packet on the Hand-off volume** — the Publisher's prepare step writes `in/review/diff.patch` (merge base to reviewed commit) and reports each changed path with its status (added, modified or deleted) and the diff's zero-context patch-id on stdout, as it reports `FOUNDRY_BASE`.
- **Scoped `--add-dir`** — the entrypoint passes `--add-dir /handoff/in/review` only when `REVIEW_COMMIT` is set, so `in/base.bundle` stays outside the reviewer's reach.
- **Facts are computed in C#** — one changed-file classifier (a value object with built-in path conventions, no per-repository setting) classes each reported path as production, test or other.
  It sets the flag "production added or modified, no test added or modified", renders the classified paths into the prompt, and validates `coveringTests` after the run.
  An acceptance-criteria extractor numbers the issue's criteria `AC1…ACn` from a heading matching `acceptance criteria`.

## Considered Options

- **The diff in the system prompt, workspace on the default branch**: rejected because the reviewer could not open a changed file for context, and a large diff would fail bootstrap at the `MAX_ARG_STRLEN` limit the entrypoint enforces.
- **Reuse `BRANCH_NAME` for the checkout**: rejected because it would make a review run one profile bug away from publishing.
- **Copy the packet into the checkout as an untracked file**: rejected because it puts the input inside the tree being judged.
- **A third, read-only profile with Bash**: rejected by ADR 0094's "A step that only classifies runs without command-execution tools", and because the checks gate already ran the pull request's CI.
- **Classify paths in the Publisher's shell**: rejected because the post-run validation in C# would need a second copy of the rules.

## Consequences

- Spikes on 2.1.323 and 2.1.325 in `--restricted --strict-mcp-config` mode read a file inside an `--add-dir` directory, and were denied a file in that directory's parent, recorded in `permission_denials`.
- The same spikes found the Skill tool present and Bash absent.
  A plugin skill ran when called by its namespaced name (`<plugin>:<skill>`), while the bare name was denied on 2.1.323, so the role prompt names its skills with the namespace.
- A restricted run cannot Read a skill's supporting files, because the plugin directory under `/opt/foundry/roles/` lies outside the working directories.
  Each review skill keeps its whole instruction in `SKILL.md`, with no `references/` files.
