# SQLCipher Provider Validation (Migration Export Path)

## Scope

This document records the production-readiness validation status of the current SQLCipher exporter spike.

The exporter path in this repository now supports two build modes:

- **Default mode (`UseOfficialSqlCipher=false`)**
  - `Microsoft.Data.Sqlite.Core`
  - `SQLitePCLRaw.bundle_e_sqlcipher`
  - viability path that is executable in CI/local today
- **Official-path mode (`UseOfficialSqlCipher=true`)**
  - `Microsoft.Data.Sqlite.Core`
  - `SQLitePCLRaw.bundle_zetetic`
  - `SQLitePCLRaw.provider.sqlcipher`
  - intended to align with official SQLCipher provider activation semantics

The official-path mode is wired, but not considered fully production-ready in this repository until licensed/native SQLCipher packages from Zetetic are available in package sources.

## Why this is still constrained

- The public `e_sqlcipher` bundles are legacy/deprecated upstream.
- Official SQLCipher for .NET packaging from Zetetic is commercial and distributed via vendor packages and licensing flow.
- Without those licensed native packages available to restore/load, `UseOfficialSqlCipher=true` can compile but runtime provider loading fails (`DllNotFoundException: sqlcipher`).
- Therefore the default path remains viability-only while the official-path mode is maintained as a reproducible blocked integration path.

## Runtime smoke validation matrix

The repository includes `.github/workflows/sqlcipher-smoke.yml`.

The workflow does three things:

1. Publish smoke artifacts for all target RIDs:
   - `win-x64`
   - `linux-x64`
   - `osx-x64`
   - `osx-arm64`
2. Execute SQLCipher runtime smoke tests where the runner can execute binaries directly:
   - executed: `win-x64`, `linux-x64`, `osx-x64`
   - publish-only: `osx-arm64`
3. Run an **official-path dry-run** (`UseOfficialSqlCipher=true`) as a non-blocking check so missing licensed native provider artifacts are visible in CI logs.

## Executed smoke checks

Runtime smoke tests (default path) verify at least:

- native SQLCipher provider probe with provider-path identity
- encrypted target creation
- reopen with correct key
- wrong-key failure classification
- export/validate/coordinator happy path
- plain-vault preservation on failure path

## Current blocker details for official provider path

- `UseOfficialSqlCipher=true` switches to `bundle_zetetic + provider.sqlcipher` and activates the SQLCipher provider explicitly.
- Runtime currently fails to load native `sqlcipher` in this environment because official Zetetic native packages are not present in restore sources.
- This is expected until vendor package source + license-backed package distribution are configured.

## Remaining gap before encrypted-primary rollout

- Provide licensed official SQLCipher native packages in package sources used by local/CI builds.
- Validate `UseOfficialSqlCipher=true` runtime smoke as green on win-x64, linux-x64, osx-x64.
- Execute runtime smoke on native Apple Silicon hardware for `osx-arm64`.
- Keep official-path smoke green before wiring encrypted-primary startup.
