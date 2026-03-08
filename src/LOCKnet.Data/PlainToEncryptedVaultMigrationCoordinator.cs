using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data;

internal sealed class PlainToEncryptedVaultMigrationCoordinator
{
	private readonly ISqliteConnectionFactory _sourceConnectionFactory;
	private readonly MasterKeyRepository _headerRepository;

	internal PlainToEncryptedVaultMigrationCoordinator(ISqliteConnectionFactory sourceConnectionFactory)
	{
		ArgumentNullException.ThrowIfNull(sourceConnectionFactory);
		_sourceConnectionFactory = sourceConnectionFactory;
		_headerRepository = new MasterKeyRepository(sourceConnectionFactory);
	}

	internal PlainToEncryptedVaultMigrationExecutionResult Execute(PlainToEncryptedVaultMigrationRequest request, IEncryptedVaultMigrationExporter exporter)
	{
		ArgumentNullException.ThrowIfNull(exporter);
		if (exporter.TargetMode != request.TargetMode)
			throw new InvalidOperationException("Exporter-Zielmodus passt nicht zum angeforderten Storage-Migrationsziel.");

		var utcNow = DateTime.UtcNow;
		var plan = Prepare(request);
		var inProgressHeader = MarkInProgress(request.Header, request.TargetMode, utcNow);
		_headerRepository.Update(inProgressHeader);

		try
		{
			exporter.ExportPlaintextVault(_sourceConnectionFactory.Storage.ConnectionString, plan.EncryptedTempPath);

			if (!File.Exists(plan.EncryptedTempPath))
				throw new InvalidOperationException("Exporter hat kein Zielartefakt erstellt.");

			exporter.ValidateExportedVault(plan.EncryptedTempPath);

			var finalizationPendingHeader = MarkFinalizationPending(inProgressHeader, request.TargetMode, DateTime.UtcNow);
			exporter.PersistMigratedHeader(plan.EncryptedTempPath, finalizationPendingHeader);
			StorageRewriteArtifacts.ReplacePrimaryDatabase(plan.EncryptedTempPath, plan.SourcePath, plan.PlainBackupPath);

			if (!File.Exists(plan.PlainBackupPath))
			{
				var clearedHeader = ClearMigrationState(finalizationPendingHeader, DateTime.UtcNow);
				exporter.PersistMigratedHeader(plan.SourcePath, clearedHeader);
				return new PlainToEncryptedVaultMigrationExecutionResult(
					clearedHeader.StorageMigrationState,
					clearedHeader.StorageMigrationTargetMode,
					clearedHeader.LastStorageMigrationAttemptUtc,
					clearedHeader.LastStorageMigrationError,
					plan.EncryptedTempPath,
					plan.PlainBackupPath);
			}

			return new PlainToEncryptedVaultMigrationExecutionResult(
				finalizationPendingHeader.StorageMigrationState,
				finalizationPendingHeader.StorageMigrationTargetMode,
				finalizationPendingHeader.LastStorageMigrationAttemptUtc,
				finalizationPendingHeader.LastStorageMigrationError,
				plan.EncryptedTempPath,
				plan.PlainBackupPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SqliteException)
		{
			var failedHeader = MarkFailed(_headerRepository.Get() ?? inProgressHeader, request.TargetMode, DateTime.UtcNow, ex.Message);
			_headerRepository.Update(failedHeader);

			return new PlainToEncryptedVaultMigrationExecutionResult(
				failedHeader.StorageMigrationState,
				failedHeader.StorageMigrationTargetMode,
				failedHeader.LastStorageMigrationAttemptUtc,
				failedHeader.LastStorageMigrationError,
				plan.EncryptedTempPath,
				plan.PlainBackupPath);
		}
	}

	internal PlainToEncryptedVaultMigrationExecutionResult FinalizeSuccessfulMigration(VaultHeader header, IEncryptedVaultMigrationExporter exporter)
	{
		ArgumentNullException.ThrowIfNull(header);
		ArgumentNullException.ThrowIfNull(exporter);

		var sourcePath = _sourceConnectionFactory.Storage.DatabasePath
			?? throw new InvalidOperationException("Finalisierung der Plain-zu-encrypted-Migration benoetigt eine dateibasierte Vault.");
		var backupPath = PlainToEncryptedVaultMigrationArtifacts.GetPlainBackupPath(sourcePath);
		if (header.StorageMigrationState != VaultStorageMigrationState.FinalizationPending)
			throw new InvalidOperationException("Finalisierung erwartet einen Storage-Migrationszustand FinalizationPending.");

		if (File.Exists(backupPath) && !StorageRewriteArtifacts.TryDeleteFile(backupPath))
			throw new IOException("Alte Plain-Sicherung konnte nicht entfernt werden.");

		var cleared = ClearMigrationState(header, DateTime.UtcNow);
		exporter.PersistMigratedHeader(sourcePath, cleared);

		return new PlainToEncryptedVaultMigrationExecutionResult(
			cleared.StorageMigrationState,
			cleared.StorageMigrationTargetMode,
			cleared.LastStorageMigrationAttemptUtc,
			cleared.LastStorageMigrationError,
			PlainToEncryptedVaultMigrationArtifacts.GetEncryptedTempPath(sourcePath),
			backupPath);
	}

	internal PlainToEncryptedVaultMigrationPlan Prepare(PlainToEncryptedVaultMigrationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(request.Header);
		ArgumentNullException.ThrowIfNull(request.Credentials);

		var storage = _sourceConnectionFactory.Storage;
		if (storage.Mode != VaultStorageMode.PlainSqlite)
			throw new InvalidOperationException("Plain-zu-encrypted-Migration darf nur von einer Plain-SQLite-Quelle starten.");

		if (storage.RequiresKeyAtOpen)
			throw new InvalidOperationException("Die Quell-Vault darf fuer den Plain-zu-encrypted-Export kein Open-Time-Keying benoetigen.");

		var sourcePath = storage.DatabasePath
			?? throw new InvalidOperationException("Plain-zu-encrypted-Migration benoetigt eine dateibasierte Quell-Vault.");

		if (!request.SourceValidatedWithActiveVaultKey)
			throw new InvalidOperationException("Plain-zu-encrypted-Migration setzt eine vorherige Quellvalidierung mit aktivem VaultKey voraus.");

		ValidateHeader(request.Header);
		ValidateSettingsState();
		foreach (var credential in request.Credentials)
			ValidateCredential(credential);

		if (PlainToEncryptedVaultMigrationArtifacts.HasPendingArtifacts(sourcePath))
			throw new InvalidOperationException("Plain-zu-encrypted-Migration kann nicht starten, solange alte Migrationsartefakte vorhanden sind.");

		return new PlainToEncryptedVaultMigrationPlan(
			sourcePath,
			PlainToEncryptedVaultMigrationArtifacts.GetEncryptedTempPath(sourcePath),
			PlainToEncryptedVaultMigrationArtifacts.GetPlainBackupPath(sourcePath),
			request.TargetMode);
	}

	internal VaultHeader MarkInProgress(VaultHeader header, VaultStorageMigrationTargetMode targetMode, DateTime utcNow)
	{
		var updated = CloneHeader(header);
		updated.StorageMigrationState = VaultStorageMigrationState.InProgress;
		updated.StorageMigrationTargetMode = targetMode;
		updated.LastStorageMigrationAttemptUtc = utcNow;
		updated.LastStorageMigrationError = null;
		updated.UpdatedAt = utcNow;
		return updated;
	}

	internal VaultHeader MarkFinalizationPending(VaultHeader header, VaultStorageMigrationTargetMode targetMode, DateTime utcNow)
	{
		var updated = CloneHeader(header);
		updated.StorageMigrationState = VaultStorageMigrationState.FinalizationPending;
		updated.StorageMigrationTargetMode = targetMode;
		updated.LastStorageMigrationAttemptUtc = utcNow;
		updated.LastStorageMigrationError = null;
		updated.UpdatedAt = utcNow;
		return updated;
	}

	internal VaultHeader MarkFailed(VaultHeader header, VaultStorageMigrationTargetMode targetMode, DateTime utcNow, string error)
	{
		var updated = CloneHeader(header);
		updated.StorageMigrationState = VaultStorageMigrationState.Failed;
		updated.StorageMigrationTargetMode = targetMode;
		updated.LastStorageMigrationAttemptUtc = utcNow;
		updated.LastStorageMigrationError = error;
		updated.UpdatedAt = utcNow;
		return updated;
	}

	internal VaultHeader ClearMigrationState(VaultHeader header, DateTime utcNow)
	{
		var updated = CloneHeader(header);
		updated.StorageMigrationState = VaultStorageMigrationState.None;
		updated.StorageMigrationTargetMode = VaultStorageMigrationTargetMode.None;
		updated.LastStorageMigrationAttemptUtc = null;
		updated.LastStorageMigrationError = null;
		updated.UpdatedAt = utcNow;
		return updated;
	}

	internal PlainToEncryptedVaultMigrationRecoveryDecision GetRecoveryDecision(VaultHeader header)
		=> PlainToEncryptedVaultMigrationArtifacts.Decide(_sourceConnectionFactory.Storage.DatabasePath, header);

	private void ValidateSettingsState()
	{
		using var connection = _sourceConnectionFactory.OpenConnection();
		RequireTable(connection, "MasterKey");
		RequireTable(connection, "Credentials");
		RequireTable(connection, "Settings");

		using var masterKeyCount = connection.CreateCommand();
		masterKeyCount.CommandText = "SELECT COUNT(*) FROM MasterKey;";
		if (Convert.ToInt64(masterKeyCount.ExecuteScalar() ?? 0L) != 1)
			throw new InvalidOperationException("Plain-zu-encrypted-Migration erwartet genau einen MasterKey-Datensatz.");
	}

	private static void RequireTable(SqliteConnection connection, string tableName)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
		command.Parameters.AddWithValue("$name", tableName);
		if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) <= 0)
			throw new InvalidOperationException($"Plain-zu-encrypted-Migration erwartet die Tabelle '{tableName}'.");
	}

	private static void ValidateHeader(VaultHeader header)
	{
		if (header.FormatVersion != VaultHeaderFormatVersion.Current)
			throw new InvalidOperationException("Plain-zu-encrypted-Migration erwartet einen aktuellen VaultHeader.");

		if (header.UsesLegacyKeyMaterial)
			throw new InvalidOperationException("Plain-zu-encrypted-Migration darf nicht mit Legacy-Keymaterial starten.");

		if (header.LegacyPasswordHash.Length > 0)
			throw new InvalidOperationException("Plain-zu-encrypted-Migration erwartet keinen Legacy-Passwort-Hash mehr.");

		if (header.StorageMigrationState is not VaultStorageMigrationState.None and not VaultStorageMigrationState.Failed)
			throw new InvalidOperationException("Plain-zu-encrypted-Migration darf nicht parallel zu einer bereits laufenden Storage-Migration starten.");
	}

	private static void ValidateCredential(CredentialRecord credential)
	{
		if (credential.SecretFormatVersion != CredentialSecretFormatVersion.Current)
			throw new InvalidOperationException($"Credential {credential.Id} verwendet noch ein Legacy-Secret-Format.");

		if (credential.MetadataFormatVersion != CredentialMetadataFormatVersion.Current)
			throw new InvalidOperationException($"Credential {credential.Id} verwendet noch ein Legacy-Metadaten-Format.");

		if (!Guid.TryParseExact(credential.CredentialUuid, "N", out _))
			throw new InvalidOperationException($"Credential {credential.Id} enthaelt eine ungueltige CredentialUuid.");

		if (credential.EncryptedPassword.Length == 0)
			throw new InvalidOperationException($"Credential {credential.Id} enthaelt keine verschluesselten Secret-Daten.");

		if (credential.EncryptedMetadata.Length == 0)
			throw new InvalidOperationException($"Credential {credential.Id} enthaelt keine verschluesselten Metadaten.");

		if (!string.IsNullOrEmpty(credential.Title) ||
			!string.IsNullOrEmpty(credential.Username) ||
			!string.IsNullOrEmpty(credential.Url) ||
			!string.IsNullOrEmpty(credential.Notes) ||
			!string.IsNullOrEmpty(credential.IconKey) ||
			credential.CredentialType != CredentialType.Password)
		{
			throw new InvalidOperationException($"Credential {credential.Id} enthaelt unerwartete Klartext-Metadaten.");
		}
	}

	private static VaultHeader CloneHeader(VaultHeader header) => new()
	{
		FormatVersion = header.FormatVersion,
		KdfIdentifier = header.KdfIdentifier,
		KdfParameters = new VaultKdfParameters
		{
			HashAlgorithm = header.KdfParameters.HashAlgorithm,
			Iterations = header.KdfParameters.Iterations,
			KeyLengthBytes = header.KdfParameters.KeyLengthBytes,
			SaltLengthBytes = header.KdfParameters.SaltLengthBytes,
		},
		Salt = header.Salt.ToArray(),
		WrappedVaultKey = header.WrappedVaultKey.ToArray(),
		LegacyPasswordHash = header.LegacyPasswordHash.ToArray(),
		UsesLegacyKeyMaterial = header.UsesLegacyKeyMaterial,
		RequiresStorageCompaction = header.RequiresStorageCompaction,
		LastStorageCompactionAttemptUtc = header.LastStorageCompactionAttemptUtc,
		LastStorageCompactionFailureKind = header.LastStorageCompactionFailureKind,
		LastStorageCompactionError = header.LastStorageCompactionError,
		StorageMigrationState = header.StorageMigrationState,
		StorageMigrationTargetMode = header.StorageMigrationTargetMode,
		LastStorageMigrationAttemptUtc = header.LastStorageMigrationAttemptUtc,
		LastStorageMigrationError = header.LastStorageMigrationError,
		CreatedAt = header.CreatedAt,
		UpdatedAt = header.UpdatedAt,
	};
}

internal sealed record PlainToEncryptedVaultMigrationRequest(
	VaultHeader Header,
	IReadOnlyList<CredentialRecord> Credentials,
	VaultStorageMigrationTargetMode TargetMode,
	bool SourceValidatedWithActiveVaultKey);

internal sealed record PlainToEncryptedVaultMigrationPlan(
	string SourcePath,
	string EncryptedTempPath,
	string PlainBackupPath,
	VaultStorageMigrationTargetMode TargetMode);

internal sealed record PlainToEncryptedVaultMigrationExecutionResult(
	VaultStorageMigrationState StorageMigrationState,
	VaultStorageMigrationTargetMode StorageMigrationTargetMode,
	DateTime? LastStorageMigrationAttemptUtc,
	string? LastStorageMigrationError,
	string EncryptedTempPath,
	string PlainBackupPath);
