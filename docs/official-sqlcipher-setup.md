# Official SQLCipher Setup for LOCKnet

This document describes how to wire official SQLCipher package sources and licensed native artifacts into LOCKnet.

## 1. What is required

To run `UseOfficialSqlCipher=true`, you need all of the following:

1. Official SQLCipher package source URL (vendor-provided feed).
2. Credentials/token for that package source.
3. Licensed native runtime package name(s) and version(s) for your target platforms.
4. Licensing terms/compliance process for developer machines and CI.

LOCKnet already includes the managed provider pieces for official mode:

- `SQLitePCLRaw.bundle_zetetic`
- `SQLitePCLRaw.provider.sqlcipher`

But the vendor native runtime package is intentionally externalized via MSBuild properties:

- `OfficialSqlCipherRuntimePackage`
- `OfficialSqlCipherRuntimeVersion`

If these are missing while `UseOfficialSqlCipher=true`, restore fails fast with a clear error.

## 2. Local developer setup

1. Copy `NuGet.official-sqlcipher.config.template` to `NuGet.official-sqlcipher.config`.
2. Fill in:
   - `zetetic-official` package source URL
   - source username/token credentials
3. Do not commit this file.
4. Restore/build/test with official mode enabled:

```bash
dotnet restore LOCKnet.sln \
  --configfile NuGet.official-sqlcipher.config \
  -p:UseOfficialSqlCipher=true \
  -p:OfficialSqlCipherRuntimePackage=<vendor-runtime-package> \
  -p:OfficialSqlCipherRuntimeVersion=<vendor-runtime-version>

dotnet test tests/LOCKnet.Data.Tests/LOCKnet.Data.Tests.csproj \
  -c Release \
  --no-restore \
  -p:UseOfficialSqlCipher=true \
  -p:OfficialSqlCipherRuntimePackage=<vendor-runtime-package> \
  -p:OfficialSqlCipherRuntimeVersion=<vendor-runtime-version> \
  --filter "FullyQualifiedName~SqlCipherEncryptedVaultMigrationExporterTests"
```

## 3. CI setup

The SQLCipher smoke workflow supports official mode plumbing while keeping default mode usable.

### Required CI secrets

- `SQLCIPHER_NUGET_SOURCE_URL`
- `SQLCIPHER_NUGET_USERNAME`
- `SQLCIPHER_NUGET_PASSWORD`

### Required CI environment values

- `USE_OFFICIAL_SQLCIPHER=true`
- `OFFICIAL_SQLCIPHER_RUNTIME_PACKAGE=<vendor-runtime-package>`
- `OFFICIAL_SQLCIPHER_RUNTIME_VERSION=<vendor-runtime-version>`

The workflow generates a temporary `NuGet.official-sqlcipher.config` at runtime and deletes it at the end.

## 4. Release/publish environment setup

Release environments must have the same package-source access and runtime package properties as CI.

Minimum steps before official encrypted-path release validation:

1. Restore with official config + runtime package properties.
2. Publish target RIDs with `UseOfficialSqlCipher=true`.
3. Execute SQLCipher runtime smoke tests on supported runners.
4. Treat `osx-arm64` as publish-only unless native arm64 execution is available.

## 5. Validation checklist

When official source + native package are configured, the following must pass:

1. Restore succeeds with `UseOfficialSqlCipher=true`.
2. Runtime probe test passes:
   - `SqlCipherEncryptedVaultMigrationExporterTests.ProbeRuntime_LoadsSqlCipherProvider`
3. Exporter test suite passes:
   - encrypted target creation
   - reopen with correct key
   - wrong-key detection
   - invalid target handling
   - coordinator happy/failure paths
4. Multi-RID publish succeeds for `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`.

## 6. Security handling rules

- Never hardcode credentials, license codes, or vendor URLs in source code.
- Keep `NuGet.official-sqlcipher.config` untracked.
- Inject secrets via CI secret store and ephemeral generated config files.

## 7. Current manual/vendor-dependent blockers

- Vendor package source access is required.
- Licensed native runtime package name/version must be confirmed with vendor distribution.
- Native runtime validation on real Apple Silicon hardware is still required.
