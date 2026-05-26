# Domain Glossary

| Term | Definition |
|------|-----------|
| **Foundry** | The service itself — monitors repositories for tagged issues and dispatches sandboxed AI workers to implement them. |
| **Worker** | A Claude Code Docker container dispatched to implement a single issue. Ephemeral — created on demand, destroyed after completion. |
| **Provider** | A git hosting platform (GitHub, GitLab). Each provider has its own API client and label format. |
| **Account** | A set of credentials (PAT) for a specific provider. Multiple accounts can exist per provider. |
| **Monitor** | The background process that polls configured repositories for issues with trigger labels. |
| **Run** | A single execution of a worker against an issue. An issue can have multiple runs (e.g. after retry). |
