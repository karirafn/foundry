# Worker roles

A Worker Role is the job a worker run is dispatched to do (see `DOMAIN.md` and ADR 0095). Each folder
here is one role, baked into the worker image at `/opt/foundry/roles/<role>/`:

- `.claude-plugin/plugin.json`: the role's Claude Code plugin manifest. Its `description` is the role's
  purpose on the dashboard's role card.
- `prompts/system.md`: the role's system prompt template, appended after Foundry's safety preamble.
  Placeholders: `{issueNumber}`, `{issueContent}`.
- `prompts/task.md`: the role's task prompt template. It must contain `{issueNumber}`.
- `skills/<skill>/SKILL.md`, `agents/<agent>.md` (optional): the plugin's skills and agents.
- `verdict.schema.json` (judgment roles only): the JSON schema the role's verdict is validated against.

`shared/` is a plugin loaded for every role. The entrypoint loads the run's role and `shared` with
`--plugin-dir`. Role names come from Foundry's code (`WorkerRole`); a role without a folder fails the
unit tests and the startup image build. A change here takes effect at the next Foundry restart.
