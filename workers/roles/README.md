# Worker roles

A Worker Role is the job a worker run is dispatched to do (see `DOMAIN.md` and ADR 0095). Each folder
here is one role, baked into the worker image at `/opt/foundry/roles/<role>/`:

- `.claude-plugin/plugin.json`: the role's Claude Code plugin manifest. Its `description` is the role's
  purpose on the dashboard's role card.
- `prompts/system.md`: the role's system prompt template, appended after Foundry's safety preamble and the Worker Principles.
  Placeholders: `{issueNumber}`, `{issueContent}`.
- `prompts/task.md`: the role's task prompt template. It must contain `{issueNumber}`.
- `skills/<skill>/SKILL.md`, `agents/<agent>.md` (optional): the plugin's skills and agents.
- `verdict.schema.json` (optional, for any role): the JSON schema of the verdict the role returns (ADR 0094).

`shared/` is a plugin loaded for every role. The entrypoint loads the run's role and `shared` with
`--plugin-dir`. Role names come from Foundry's code (`WorkerRole`); a role without a folder fails the
unit tests and the startup image build. A change here takes effect at the next Foundry restart.

The engineering standards (the Worker Principles) live under `shared/prompts/`. `principles.md` holds only the
framing line; the standards themselves are split into Principle Sections, one file each under
`shared/prompts/principles/`: `testing.md`, `design.md`, `code.md` and `domain.md` (Domain modelling). Each role
declares in Foundry's code (`WorkerRole`) which sections its system prompt carries, never in its folder: implement
carries all four, triage carries Design and Domain modelling. Foundry places the framing line followed by the role's
sections, in the fixed order Testing, Design, Code, Domain modelling, after the safety preamble and before the role
prompt; a role with no sections gets neither the framing nor any principles. They yield to the target repository's
style, testing and design conventions, never to the safety rules. Every file is required and takes no placeholders,
and the framing line with all four sections together stays within 8 KiB (UTF-8 bytes): a missing, blank or
placeholder-bearing file, or files over the combined cap, fail the unit tests and the startup image build. A role's
own prompt says how it uses them (implement: "Apply the engineering principles above to every change.").

## Capability profiles

Each role declares the `WorkerCapability` it runs under in Foundry's code (`WorkerRole`), never in its folder
(ADR 0098). The container spec and the entrypoint branch on the profile (`WORKER_CAPABILITY`), never on the role name:

- `full` (`implement`): the account token, a branch pre-created on the provider and `--dangerously-skip-permissions`.
  Foundry reads the issue title, body and comments at dispatch and renders them into `{issueContent}` as XML-encoded
  untrusted data (ADR 0100); when the thread does not fit the prompt budget, the oldest comments are dropped first.
- `restricted` (`triage`): `--restricted --strict-mcp-config` and never the permission skip, no provider token, no
  branch, and a shallow clone of the default branch. Foundry reads the issue title, body and comments and renders them
  into `{issueContent}` as XML-encoded untrusted data. The run returns its verdict only.

## Verdicts

When a role has a `verdict.schema.json`, the entrypoint passes it to both `claude` invocations (the
first run and the transient-error `claude -c` resume) with `--json-schema`, and the run's final
stream-json `result` event carries the verdict as `structured_output`. Foundry reads the last result
event's `structured_output` and parses it with the role's typed verdict parser
(`IWorkerVerdictParser`). The schema and its parser are one contract: change them in the same pull
request.

A role schema may reference the definitions in `shared/verdict.defs.json` by `#/$defs/<name>`. The
entrypoint merges that file's `$defs` into the role schema (the role's own `$defs` win on a name
clash), so `claude` receives one self-contained schema. A role schema or `shared/verdict.defs.json`
that is not a JSON object fails the unit tests, the startup image build and the run's bootstrap.

`claude` sends the schema to the Claude API as a tool's `input_schema`. The API rejects `allOf`,
`anyOf` and `oneOf` at the top level of an `input_schema` (nested, for example inside a property,
they are accepted). Express a condition on the whole verdict with one top-level `if`/`then`/`else`,
which is accepted. A role schema with a top-level `allOf`, `anyOf` or `oneOf` fails the unit tests
and the run's bootstrap (`worker_bootstrap_failed`, `stage=role`, naming the file and keyword)
before cloning.

Shared definitions:

- `questions`: questions a person must answer before the work can continue. An array of at least one
  `{"question": string, "context": string}` object, with a non-empty `question` and no other
  properties. Every role that asks questions references this one definition
  (`{"$ref": "#/$defs/questions"}`), and Foundry reads it with `VerdictQuestionsReader`.

For a run that exits 0, Foundry records the verdict's status on the worker run: `done`,
`needs_input` (with its questions), `missing` (no `structured_output`, e.g. a run that hit
`--max-turns`) or `invalid` (fails the typed parse). The verdict is recorded only; the run's outcome
is still decided from facts (merge request, commits, exit code).

`implement/verdict.schema.json` requires `outcome` (`done` or `needs_input`), `questions` when
`outcome` is `needs_input`, and `pullRequest` otherwise (`done`), expressed as one top-level
`if`/`then`/`else`.

`triage/verdict.schema.json` requires `outcome` (`ready` or `needs_input`) and `kind` (`feature`, `bug`, `refactor` or
`documentation`), and `questions` when `outcome` is `needs_input`. Foundry records `ready` as `done`.
