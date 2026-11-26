# Worker roles

A Worker Role is the job a worker run is dispatched to do (see `DOMAIN.md` and ADR 0095). Each folder
here is one role, baked into the worker image at `/opt/foundry/roles/<role>/`:

- `.claude-plugin/plugin.json`: the role's Claude Code plugin manifest. Its `description` is the role's
  purpose on the dashboard's role card.
- `prompts/system.md`: the role's system prompt template, appended after Foundry's safety preamble and the Worker Principles.
  Placeholders: `{issueNumber}`, `{issueContent}` (the issue's content followed by its Tracker Context, see `DOMAIN.md`).
- `prompts/task.md`: the role's task prompt template. It must contain `{issueNumber}`.
- `skills/<skill>/SKILL.md`, `agents/<agent>.md` (optional): the plugin's skills and agents.
- `verdict.schema.json` (optional, for any role): the JSON schema of the verdict the role returns (ADR 0094).

`shared/` is a plugin loaded for every role. The entrypoint loads the run's role and `shared` with
`--plugin-dir`. Its `hooks/hooks.json` declares hooks that apply to every role: a PreToolUse
hook blocks a `pgrep -f`/`pkill -f` Bash command, whose pattern matches the probing shell's own command line
(tests: `workers/tests/shared-hooks.bats`). Role names come from Foundry's code (`WorkerRole`); a role without a folder fails the
unit tests and the startup image build. A change here takes effect at the next Foundry restart.

The engineering standards (the Worker Principles) live under `shared/prompts/`. `principles.md` holds only the
framing line; the standards themselves are split into Principle Sections, one file each under
`shared/prompts/principles/`: `testing.md`, `design.md`, `code.md` and `domain.md` (Domain modelling). Each role
declares in Foundry's code (`WorkerRole`) which sections its system prompt carries, never in its folder: implement
and review carry all four (a unit test keeps review's sections equal to implement's), triage carries Design and
Domain modelling. Foundry places the framing line followed by the role's
sections, in the fixed order Testing, Design, Code, Domain modelling, after the safety preamble and before the role
prompt; a role with no sections gets neither the framing nor any principles. They yield to the target repository's
style, testing and design conventions, never to the safety rules. Every file is required and takes no placeholders,
and the framing line with all four sections together stays within 8 KiB (UTF-8 bytes): a missing, blank or
placeholder-bearing file, or files over the combined cap, fail the unit tests and the startup image build. A role's
own prompt says how it uses them (implement: "Apply the engineering principles above to every change.").

Each bullet of a section file gets a principle id `<section>.<n>` when Foundry assembles the principles:
`testing`, `design`, `code` or `domain`, then the bullet's 1-based position in its file after the heading line
(`- [design.1] Invalid states are …`). The ids are never written in the files, so the prompts and the role cards
always show the same ids. A review finding cites an id as its rule; inserting a bullet renumbers the later ids for
future runs only. The ids count towards the 8 KiB cap.

`shared/prompts/decisions.md` holds the ask-or-decide rule (ADR 0123): ask a person only about uncertain business
logic and important implementation details, and decide the rest, stating each answer with its ground and evidence.
It is not a Principle Section: it carries no ids and no "repository conventions win" framing, and does not count
towards the 8 KiB cap. Each role declares in Foundry's code (`WorkerRole.CarriesDecisionRule`) whether its system
prompt carries it: triage, implement and review do. Foundry places it under its own heading after the Worker
Principles and before the role prompt; a role that does not carry it gets neither the heading nor the text. The file
is required, non-blank and takes no placeholders, like the principle files: a missing, blank or placeholder-bearing
file fails the unit tests and the startup image build. Each role's own prompt says how it applies the rule (triage
returns its decisions in `decisions`, implement records them in the pull request `notes`/`attention`, review raises a
decision that should have been asked as an advisory finding).

## Capability profiles

Each role declares the `WorkerCapability` it runs under in Foundry's code (`WorkerRole`), never in its folder
(ADR 0098). The container spec and the entrypoint branch on the profile (`WORKER_CAPABILITY`), never on the role name:

- `full` (`implement`): no provider token (ADR 0100), a branch pre-created on the provider and
  `--dangerously-skip-permissions`. Foundry reads the issue title, body and comments at dispatch and renders them
  into `{issueContent}` as XML-encoded untrusted data (ADR 0100); when the thread does not fit the prompt budget, the
  oldest comments are dropped first.
  Foundry also reads the Referenced Issues and the Open-Issue Index (the Tracker Context) and renders them after the
  issue content as XML-encoded untrusted data; when the prompt does not fit, Referenced Issue bodies and then the
  open-issue list are shed before the issue's own content.
- `restricted` (`triage`, `review`): `--restricted --strict-mcp-config` and never the permission skip, no provider token, no
  branch, and a clone of a Publisher base bundle holding only the default branch. Foundry reads the issue title, body and comments and renders them
  into `{issueContent}` as XML-encoded untrusted data. Foundry also reads the Referenced Issues and the Open-Issue
  Index and renders them after the issue content as XML-encoded untrusted data; when the prompt does not fit,
  Referenced Issue bodies and then the open-issue list are shed before the issue's own content. The run returns its
  verdict only.

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
  `{"question": string, "context": string, "recommendation": string}` object, with a non-empty
  `question` and `recommendation` and no other properties. `recommendation` is the one option the
  run recommends, with its reason. Every role that asks questions references this one definition
  (`{"$ref": "#/$defs/questions"}`), and Foundry reads it with `VerdictQuestionsReader`, which
  rejects a question without a recommendation.

  Reply rule: a reply that leaves a question unaddressed accepts its recommendation, so a bare "ok"
  accepts them all and a reply answering some questions by number accepts the rest. Foundry does not
  apply recommendations in code; the next run reads the reply and the recommendations together from
  the clarification log, whose lead sentence states the rule.

For a run that exits 0, Foundry records the verdict's status on the worker run: `done`,
`needs_input` (with its questions), `missing` (no `structured_output`, e.g. a run that hit
`--max-turns`) or `invalid` (fails the typed parse). The verdict is recorded only; the run's outcome
is still decided from facts (merge request, commits, exit code).

`implement/verdict.schema.json` requires `outcome` (`done` or `needs_input`), `questions` when
`outcome` is `needs_input`, and `pullRequest` otherwise (`done`), expressed as one top-level
`if`/`then`/`else`.

`triage/verdict.schema.json` requires `outcome` (`ready`, `needs_input` or `blocked`) and `kind` (`feature`, `bug`,
`refactor` or `documentation`), and `questions` when `outcome` is `needs_input`. A `blocked` verdict carries `blockedBy`:
one to 50 `{issue, reason}` objects naming open issues that must land first, each with a reason of 1 to 500 characters.
The schema's conditional stays flat (`needs_input` requires `questions`); the typed parser requires a non-empty
`blockedBy` for `blocked`, collapses each reason to one line without format characters (a reason blank after that
breaks the verdict), and the resolver rejects an issue outside the run's supplied open issues, a duplicate or an
over-cap list as `verdict_invalid`. A `ready` verdict carries `demand` (`routine` or `demanding`) and `demandReason`
(one line of 1 to 200 characters); the schema lists both as optional and the typed parser requires them for `ready`
and refuses them for `needs_input` and `blocked`. Foundry records `ready` and `blocked` as `done`. A `blocked` verdict the resolver
rejects is recorded as `invalid`.

`review/verdict.schema.json` requires `summary` (1 to 2000 characters), `findings` (0 to 50
`{path, line?, severity, category, rule?, body}` objects; `severity` is `blocking` or `advisory`, `category` one of
`correctness`, `criteria`, `tests`, `security`, `quality`, `design`, `conventions`, and `rule` a principle id or a
convention `file` with a verbatim `quote`), `dimensions` (every category, each `{status, note}` with `status`
`clear`, `findings` or `not_applicable`), `criteria` (0 to 50 `{id, status, evidence}` objects answering the
supplied `AC<n>` ids with `met`, `unmet`, `not_verifiable` or, when the checks gate passed, `covered_by_checks`) and `tests` (`{assessment, reason, coveringTests}`,
`assessment` `adequate`, `missing` or `not_needed`). It has no `outcome`: Foundry derives the outcome from the
verdict in code (ADR 0113). Until the review verdict parser lands, a review run that exits cleanly fails as
`verdict_invalid`, so a review can never approve anything; nothing dispatches a review yet.

The review role's `skills/security-review/SKILL.md` holds its whole security checklist in one file, with no
`references/` folder: a restricted run cannot read files beside `SKILL.md`, because the plugin directory lies outside
its working directories.
