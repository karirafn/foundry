---
reaches: structure
---

# Worker Roles Ship in the Worker Image

## Context

Foundry is adding kinds of worker runs (triage, implementation, review), and each needs its own prompt, skills and verdict schema. Under [ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md) a role's instructions must be pinned and reviewed, and must change together with the code that validates its verdict. [ADR 0093](0093-workers-receive-no-host-claude-code-configuration.md) removed the host-mount route for configuration. This ADR replaces the prompt-template part of [ADR 0080](0080-all-worker-settings-in-global-settings.md) (`WorkerPrompts` in `GlobalSettings`, edited in the dashboard).

One implement model made every issue run on the same model, while review approved about 95% of Opus-implemented work on its first pass (4 of 78 issues sent back at the time), so most issues do not need the most capable model. Implement is also most of worker spend. The model an issue needs is therefore chosen per Implement Demand, which triage judges, and the demand-to-model mapping stays an operator setting.

## Decision

Each Worker Role's configuration lives in the Foundry repository under `workers/roles/<role>/`: a Claude Code plugin (skills, agents) and the role's prompt. `workers/roles/shared/` is loaded for every role. The worker image bakes these folders in, and the entrypoint loads the run's role with `--plugin-dir`. Role names come from Foundry's code, so a role without a folder fails at image build or in tests, not at dispatch.

- **Versioned with the build**: the image is rebuilt from the checkout at every Foundry startup, the same checkout the API's verdict schemas are compiled from. Prompts, skills and schemas therefore always come from one commit. A merged role change takes effect at the next restart, and a run records which role and image it used.
- **Prompts leave settings**: `WorkerPrompts` is removed from `GlobalSettings`. Prompt changes go through pull-request review like any other change. The safety preamble stays in code (`SystemPromptBuilder`).
- **Model is a per-role setting, and implement's is per demand tier**: triage's and review's models are operator settings stored in the database and keyed by role name. The implement model is two operator settings, one per Implement Demand (`routine`, `demanding`), each with an optional effort level written to the managed settings `effortLevel`. Dispatch picks the setting matching the issue's effective demand, which triage's verdict sets and a review-findings or failed-checks revision escalates to `demanding`. The verdict carries a demand, never a model, so the mapping stays an operational cost and plan choice, not instruction content. A model is chosen from the documented Claude Code aliases plus the model IDs returned by `GET /v1/models`, which works with the workers' OAuth token. An empty model means the CLI's account default and an empty effort leaves the model's default. There is no global model and no fallback chain between settings.
- **Improvements are pull requests**: an agent never edits role configuration at runtime. A proposed improvement is a pull request against the Foundry repository, which the operator merges.

## Considered Options

- **A dedicated configuration repository tracked at its default branch**: Foundry would resolve the head SHA at dispatch and fill a content-addressed read-only volume. Rejected because the running Foundry build and the tracked branch can disagree, which would pair new prompts with old verdict-schema code.
- **Prompts editable in the dashboard, skills in the image**: rejected because nothing would check that a dashboard prompt matches the image's skills or the code's placeholders. That is the two-sources defect ADR 0080 started from.
- **Rebuild the image whenever `workers/` changes while Foundry runs**: rejected for the same skew. The API keeps the schema code it was started with, so a mid-run rebuild would mix commits.

## Consequences

- Changing a role's behaviour needs a merge and a restart. Prompts cannot be tweaked live.
- Role verdict schemas, prompts and skills are reviewed in the same pull request.

## Revisions
- 2026-11-25 — The implement model is set per Implement Demand, with an optional effort, instead of one per-role setting (#1305).
