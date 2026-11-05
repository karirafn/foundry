# Pin the Data Protection application name and re-encrypt legacy ciphertext at startup

## Context

`AddDataProtection()` without `SetApplicationName` binds every protected value to the host's content-root path (`HostingApplicationDiscriminator`: content root plus a trailing separator), so moving an install — another folder, or Windows to Linux — made every encrypted column (`accounts.token`, `accounts.branch_rules_token`, `monitored_repositories.webhook_secret`, `claude_account.api_key`) undecryptable even with the key ring copied along.

## Decision

The WebApi pins `SetApplicationName("Foundry")` (`Program.cs`). This name is permanent: changing it strands every stored secret exactly as the path-derived default did.

Existing ciphertext migrates once, automatically: after EF migrations and before hosted services start, a startup step rewrites every value that decrypts only under the legacy discriminator. The legacy discriminator is `DataProtection:LegacyApplicationName` when configured (a bootstrap key, as [ADR 0080](0080-all-worker-settings-in-global-settings.md) permits), otherwise the framework's own `GetApplicationUniqueIdentifier()` for the current host. Values already under "Foundry" are untouched, undecryptable values are logged by table, column and row id and left as-is, and all rewrites commit in one transaction.

Encrypted columns are discovered from the EF model, not listed: every Data Protection converter implements `IDataProtectedConverter` (its purpose string), and a reflection test fails when a converter that takes an `IDataProtectionProvider` lacks the marker. All converters share one storage codec (`DataProtectedText`, base64 of the protected UTF-8 bytes), so the rewrite produces exactly what the converters read.

## Considered Options

- **Fallback decryption inside each converter** — keeps two discriminators alive indefinitely across three converters; a one-time rewrite converges.
- **Set the application name to the old Windows path** — preserves data but binds the install to a path that no longer exists.
- **Hand-maintained column list in the host** — a new encrypted column can be forgotten, and the host learns module table names.

## Consequences

The legacy provider is built over the host's file-system key repository (`FileSystemXmlRepository.Directory`); moving key storage to another repository type requires extending it, or the fallback is skipped.

The re-encryption is one-way. After it runs, the data is readable only under the pinned name "Foundry"; reverting to a build without the pinned name makes the encrypted columns unreadable.

The legacy provider shares only the key repository, not the host's key-at-rest encryptor. If `ProtectKeysWith*` is ever added, the legacy keys become unreadable to the legacy provider and the affected values are reported as undecryptable.
