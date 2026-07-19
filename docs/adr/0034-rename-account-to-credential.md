# Rename Account to Credential

## Context

The `Account` aggregate in the Monitoring module represented a provider identity (GitHub/GitLab username + token + base URL). As the system grows to scope credentials by repository namespace rather than by identity, the term "account" became misleading — it implies a user account, but the aggregate's job is to hold credentials and map them to repository namespaces.

## Decision

Rename `Account` → `Credential`, `GitHubAccount` → `GitHubCredential`, `GitLabAccount` → `GitLabCredential`, and `AccountId` → `CredentialId` throughout the Monitoring module and Contracts. The rename is in-place: new files carry the new names; old files are emptied to avoid file-deletion friction in the tool environment.

EF discriminator string values (`"github"`, `"gitlab"`) and the `accounts` table name are preserved to avoid a data migration in this step. The `MonitoredRepository.CredentialId` column retains the column name `account_id` in EF config until Step 3 adds the migration.

The `RepositorySummary.AccountId` property (public HTTP API JSON field) is kept unchanged to avoid a breaking API contract change — that rename is a separate decision.

## Considered Options

- Keep `Account` and add a parallel `Credential` hierarchy: rejected — two names for the same concept adds confusion.
- Rename only at the service/feature layer and keep `Account` as the domain name: rejected — ubiquitous language should be consistent from domain to API.
