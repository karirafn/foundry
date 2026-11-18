---
name: security-review
description: Security checklist for reviewing a pull request's diff. Use it to judge the review's security dimension.
---

# Security review

Work through every item below against the diff at `/handoff/in/review/diff.patch` and the code it touches. Judge only
what the change adds or alters, plus existing code the change now exposes in a new way. For each problem, return a
finding with category `security`: `blocking` when it is exploitable or leaks a secret, `advisory` when it is a
hardening gap. When no item applies, the `security` dimension is `clear`, with a note naming what you checked.

## Injection

- SQL, NoSQL, LDAP or query-language text built by concatenating or interpolating input rather than with parameters.
- Shell commands built from input, or a process started through a shell with input in its arguments.
- Templates, HTML or Markdown rendered from input without encoding (cross-site scripting).
- Input reaching `eval`, dynamic code loading, reflection by name, or a regular expression open to catastrophic
  backtracking.
- Log lines written from input without neutralising line breaks (log forging).

## Authentication and authorization

- A new endpoint, handler or message consumer with no authentication, or with weaker checks than its siblings.
- An authorization check missing, done after the action, or done on a client-supplied identity or role.
- Access to a record by id without checking that the caller may see it (insecure direct object reference).
- Session, token or cookie handling changed: expiry, rotation, `Secure`/`HttpOnly`/`SameSite` flags.

## Secrets

- A credential, token, key, password or connection string written into code, configuration, tests or fixtures.
- A secret written to logs, error messages, exceptions, telemetry, API responses or files outside a secret store.
- A secret passed on a command line or in an environment readable by less-trusted processes.

## Input validation at boundaries

- Input from a request, message, file, environment or external service used without validation of type, length,
  range and format at the boundary where it enters.
- Validation done only on the client, or after the value was already used.
- Unbounded sizes: collections, uploads, strings or recursion depth an attacker controls (resource exhaustion).

## Cryptography

- A hand-written algorithm or protocol, or a broken one (MD5 or SHA-1 for integrity or passwords, DES, ECB mode, RC4).
- Passwords stored with a fast hash rather than a slow, salted one (bcrypt, scrypt, Argon2, PBKDF2).
- Randomness for tokens, keys or nonces from a non-cryptographic generator, or a reused IV or nonce.
- TLS certificate or host-name verification disabled, or a key hard-coded or committed.
- Secrets or tokens compared with a non-constant-time comparison.

## Unsafe deserialization

- Untrusted data deserialized into arbitrary types: polymorphic type names taken from the payload, binary
  formatters, `pickle`, YAML loaders that construct objects, Java serialization.
- XML parsed with external entities or DTDs enabled (XXE).

## Path traversal

- A file path or archive entry built from input without normalising it and checking it stays inside the intended
  directory (`..`, absolute paths, symbolic links).
- Archive extraction that trusts entry names (zip slip).
- A file served or deleted by a name the caller controls.

## Server-side request forgery

- An outbound request to a URL, host or port taken from input without an allowlist.
- Redirects followed to addresses the caller controls, or requests able to reach internal networks, link-local
  metadata endpoints (`169.254.169.254`) or `localhost`.
- A webhook or callback URL stored from input and called later without the same checks.
