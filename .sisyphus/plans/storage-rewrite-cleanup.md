# LOCKnet Storage Rewrite Cleanup Plan

## Goal

Replace the current VACUUM-only post-migration cleanup with a stronger fresh-file rewrite path that preserves degraded-mode semantics, validates current-format encrypted data before rewrite, and recovers safely from interrupted replacement attempts.

## Chosen Strategy

Use a same-directory fresh-file rewrite based on SQLite `VACUUM INTO`, followed by same-volume replacement of the primary database file.

- Core validates the current vault/header/records with the active VaultKey before any rewrite attempt.
- Data performs the file rewrite and replacement.
- Current degraded-mode semantics remain: cleanup only clears pending state after real success.
- Current state model remains centered on `RequiresStorageCompaction` plus deterministic temp/backup artifact discovery; no new header state is planned unless implementation proves it is necessary.

## Design Decisions

### Validation in Core

Before rewrite, `MasterKeyManager` validates:

- current header is fully current-format and not legacy
- every credential row is current-format
- UUID is valid `N` format
- no plaintext metadata residue exists on current rows
- secret envelope decrypts/authenticates with current VaultKey
- metadata envelope decrypts/authenticates with current VaultKey

If any current row is malformed, cleanup fails as a serious corruption-style condition and the pending state remains.

### Rewrite in Data

`IVaultMigrationRepository.CompactStorage()` remains parameterless.

`VaultMigrationRepository.CompactStorage()` will:

1. Resolve the file-backed database path from the SQLite connection string.
2. Use deterministic artifact paths in the same directory:
   - `<db>.rewrite.tmp`
   - `<db>.rewrite.bak`
3. Clean stale temp artifacts when safe.
4. Open the source DB with cleanup pragmas (`journal_mode=DELETE`, `synchronous=FULL`, `wal_checkpoint(TRUNCATE)`).
5. Execute `VACUUM INTO` to the temp file.
6. Verify the temp DB opens and passes `PRAGMA quick_check`.
7. Close all rewrite connections.
8. Replace the primary DB with the temp DB:
   - Windows: `File.Replace(temp, main, backup)`
   - non-Windows: same-directory `File.Move(temp, main, overwrite: true)`
9. Delete backup/temp artifacts when possible.
10. Return explicit structured failure info if replacement or artifact cleanup fails.

## Recovery Model

`Database.Initialize()` gains startup recovery for deterministic rewrite artifacts.

### Cases

- `main + temp`: stale/incomplete temp artifact; delete temp when safe
- `main valid + backup`: rewrite likely replaced main but cleanup did not remove backup; delete backup and clear pending state
- `main missing or invalid + backup valid`: restore backup to main; keep pending state
- `main missing + temp valid + no backup`: promote temp to main conservatively; keep pending state
- unusable artifact combination: fail explicitly instead of silently creating a new empty vault over recovery artifacts

## State Model

- Keep `RequiresStorageCompaction` as the single durable pending-state flag.
- Keep current `LastStorageCompaction*` metadata.
- Do not add a new durable header state unless coding proves a recovery hole that artifact discovery cannot cover.

## API / Wiring Changes

- Inject `ISessionManager` into `MasterKeyManager` so manual retry can validate with the active session key without changing the public retry API.
- Update AppServices and tests for the new constructor dependency.
- Improve persisted `StorageCompactionFailureKind` messaging so rewrite failures remain explicit after refresh/restart.

## Tests

Add/update tests for:

- successful rewrite cleanup path
- rewrite failure preserves pending state
- malformed current row blocks rewrite
- interruption before replacement leaves old DB intact
- interruption after temp creation but before completion leaves stale temp recoverable on startup
- startup recovery deletes stale temp artifacts
- startup recovery restores backup when main DB is missing/invalid
- startup recovery finalizes backup cleanup and clears pending state when appropriate
- password change still preserves pending state while rewrite cleanup is pending

## Verification

- targeted Core tests
- targeted Data file-based rewrite/recovery tests
- full `dotnet build LOCKnet.sln -c Release`
- full `dotnet test LOCKnet.sln -c Release`
