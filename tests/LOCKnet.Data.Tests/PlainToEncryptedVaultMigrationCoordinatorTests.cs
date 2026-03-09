using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests;

public sealed class PlainToEncryptedVaultMigrationCoordinatorTests : IDisposable
{
	private readonly string _tempDirectory;
	private readonly string _databasePath;
	private readonly PlainSqliteConnectionFactory _factory;
	private readonly MasterKeyRepository _masterKeyRepository;
	private readonly CredentialsRepository _credentialsRepository;

	public PlainToEncryptedVaultMigrationCoordinatorTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), $"locknet-encrypted-migration-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDirectory);
		_databasePath = Path.Combine(_tempDirectory, "locknet.db");
		_factory = new PlainSqliteConnectionFactory(_databasePath);
		new Database(_factory).Initialize();
		_masterKeyRepository = new MasterKeyRepository(_factory);
		_credentialsRepository = new CredentialsRepository(_factory);
	}

	public void Dispose()
	{
		TryDelete(_databasePath);
		TryDelete(PlainToEncryptedVaultMigrationArtifacts.GetEncryptedTempPath(_databasePath));
		TryDelete(PlainToEncryptedVaultMigrationArtifacts.GetPlainBackupPath(_databasePath));
		if (Directory.Exists(_tempDirectory))
		{
			try
			{
				Directory.Delete(_tempDirectory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void Execute_SuccessfulFakeExport_ReplacesPrimaryAndTransitionsToFinalizationPending()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var exporter = new FakeEncryptedVaultMigrationExporter();

		var result = coordinator.Execute(MakeRequest(), exporter);
		var storedHeader = _masterKeyRepository.Get()!;
		var expectsBackup = File.Exists(result.PlainBackupPath);

		Assert.Equal(expectsBackup ? VaultStorageMigrationState.FinalizationPending : VaultStorageMigrationState.None, result.StorageMigrationState);
		Assert.Equal(result.StorageMigrationState, storedHeader.StorageMigrationState);
		Assert.Equal(expectsBackup ? VaultStorageMigrationTargetMode.EncryptedSqlite : VaultStorageMigrationTargetMode.None, storedHeader.StorageMigrationTargetMode);
		if (expectsBackup)
			Assert.NotNull(storedHeader.LastStorageMigrationAttemptUtc);
		else
			Assert.Null(storedHeader.LastStorageMigrationAttemptUtc);
		Assert.Null(storedHeader.LastStorageMigrationError);
		Assert.False(File.Exists(result.EncryptedTempPath));
		Assert.Equal(expectsBackup, File.Exists(result.PlainBackupPath));
		Assert.True(exporter.ExportCalled);
		Assert.True(exporter.ValidateCalled);

		using var connection = _factory.OpenConnection();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
		command.Parameters.AddWithValue("$key", FakeEncryptedVaultMigrationExporter.ExportMarkerKey);
		Assert.Equal(FakeEncryptedVaultMigrationExporter.ExportMarkerValue, command.ExecuteScalar() as string);
	}

	[Fact]
	public void FinalizeSuccessfulMigration_RemovesBackupAndClearsMigrationState()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var exporter = new FakeEncryptedVaultMigrationExporter();
		var execute = coordinator.Execute(MakeRequest(), exporter);
		if (!File.Exists(execute.PlainBackupPath))
			return;

		var result = coordinator.FinalizeSuccessfulMigration(_masterKeyRepository.Get()!, exporter);
		var storedHeader = _masterKeyRepository.Get()!;

		Assert.Equal(VaultStorageMigrationState.None, result.StorageMigrationState);
		Assert.Equal(VaultStorageMigrationState.None, storedHeader.StorageMigrationState);
		Assert.Equal(VaultStorageMigrationTargetMode.None, storedHeader.StorageMigrationTargetMode);
		Assert.Null(storedHeader.LastStorageMigrationAttemptUtc);
		Assert.Null(storedHeader.LastStorageMigrationError);
		Assert.False(File.Exists(result.PlainBackupPath));
	}

	[Fact]
	public void Execute_WhenExporterFails_PreservesPlainVaultAndMarksFailure()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var exporter = new FakeEncryptedVaultMigrationExporter { ThrowOnExport = true };

		var result = coordinator.Execute(MakeRequest(), exporter);
		var storedHeader = _masterKeyRepository.Get()!;

		Assert.Equal(VaultStorageMigrationState.Failed, result.StorageMigrationState);
		Assert.Equal(VaultStorageMigrationState.Failed, storedHeader.StorageMigrationState);
		Assert.Equal(VaultStorageMigrationTargetMode.EncryptedSqlite, storedHeader.StorageMigrationTargetMode);
		Assert.NotNull(storedHeader.LastStorageMigrationAttemptUtc);
		Assert.Contains("Simulierter Exportfehler", storedHeader.LastStorageMigrationError);
		Assert.True(File.Exists(_databasePath));
		Assert.False(File.Exists(result.PlainBackupPath));
	}

	[Fact]
	public void Execute_WhenValidationFails_PreservesPlainVaultAndLeavesTempArtifactForDiagnosis()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var exporter = new FakeEncryptedVaultMigrationExporter { WriteInvalidTarget = true };

		var result = coordinator.Execute(MakeRequest(), exporter);
		var storedHeader = _masterKeyRepository.Get()!;

		Assert.Equal(VaultStorageMigrationState.Failed, storedHeader.StorageMigrationState);
		Assert.Contains("ungueltiges Zielartefakt", storedHeader.LastStorageMigrationError);
		Assert.True(File.Exists(_databasePath));
		Assert.True(File.Exists(result.EncryptedTempPath));
		Assert.False(File.Exists(result.PlainBackupPath));
	}

	[Fact]
	public void RecoveryDecision_AfterSuccessfulExecute_RequestsFinalizationUntilBackupRemoved()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var execute = coordinator.Execute(MakeRequest(), new FakeEncryptedVaultMigrationExporter());
		if (!File.Exists(execute.PlainBackupPath))
			return;

		var decision = coordinator.GetRecoveryDecision(_masterKeyRepository.Get()!);

		Assert.Equal(PlainToEncryptedVaultMigrationRecoveryAction.FinalizeBackupCleanup, decision.Action);
		Assert.True(File.Exists(decision.PlainBackupPath!));
	}

	[Fact]
	public void Prepare_ValidPlainVault_ReturnsMigrationPlan()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);

		var plan = coordinator.Prepare(new PlainToEncryptedVaultMigrationRequest(
			_masterKeyRepository.Get()!,
			_credentialsRepository.GetAll(),
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true));

		Assert.Equal(_databasePath, plan.SourcePath);
		Assert.Equal(PlainToEncryptedVaultMigrationArtifacts.GetEncryptedTempPath(_databasePath), plan.EncryptedTempPath);
		Assert.Equal(PlainToEncryptedVaultMigrationArtifacts.GetPlainBackupPath(_databasePath), plan.PlainBackupPath);
		Assert.Equal(VaultStorageMigrationTargetMode.EncryptedSqlite, plan.TargetMode);
	}

	[Fact]
	public void Prepare_MalformedCurrentCredential_RejectsWholeMigration()
	{
		SeedValidCurrentVault();
		var bad = _credentialsRepository.GetAll().Single();
		bad.CredentialUuid = "not-a-valid-uuid";
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);

		var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Prepare(new PlainToEncryptedVaultMigrationRequest(
			_masterKeyRepository.Get()!,
			[bad],
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true)));

		Assert.Contains("ungueltige CredentialUuid", ex.Message);
	}

	[Fact]
	public void Prepare_WithoutPriorVaultKeyValidation_IsRejected()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);

		var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Prepare(new PlainToEncryptedVaultMigrationRequest(
			_masterKeyRepository.Get()!,
			_credentialsRepository.GetAll(),
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: false)));

		Assert.Contains("VaultKey", ex.Message);
	}

	[Fact]
	public void Prepare_MissingSettingsTable_RejectsMigration()
	{
		SeedValidCurrentVault();
		using (var connection = _factory.OpenConnection())
		{
			using var command = connection.CreateCommand();
			command.CommandText = "DROP TABLE Settings;";
			command.ExecuteNonQuery();
		}
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);

		var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Prepare(new PlainToEncryptedVaultMigrationRequest(
			_masterKeyRepository.Get()!,
			_credentialsRepository.GetAll(),
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true)));

		Assert.Contains("Settings", ex.Message);
	}

	[Fact]
	public void RecoveryDecision_InProgressWithTempArtifact_RequestsResume()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var header = coordinator.MarkInProgress(_masterKeyRepository.Get()!, VaultStorageMigrationTargetMode.EncryptedSqlite, DateTime.UtcNow);
		File.WriteAllBytes(PlainToEncryptedVaultMigrationArtifacts.GetEncryptedTempPath(_databasePath), [0x01, 0x02]);

		var decision = coordinator.GetRecoveryDecision(header);

		Assert.Equal(PlainToEncryptedVaultMigrationRecoveryAction.ResumeExport, decision.Action);
	}

	[Fact]
	public void RecoveryDecision_FinalizationPendingWithBackupArtifact_RequestsFinalization()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var header = coordinator.MarkFinalizationPending(_masterKeyRepository.Get()!, VaultStorageMigrationTargetMode.EncryptedSqlite, DateTime.UtcNow);
		File.WriteAllBytes(PlainToEncryptedVaultMigrationArtifacts.GetPlainBackupPath(_databasePath), [0x01, 0x02]);

		var decision = coordinator.GetRecoveryDecision(header);

		Assert.Equal(PlainToEncryptedVaultMigrationRecoveryAction.FinalizeBackupCleanup, decision.Action);
	}

	[Fact]
	public void RecoveryDecision_FailedStatePreservesOldVaultAndDoesNotSilentlyRecover()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var header = coordinator.MarkFailed(_masterKeyRepository.Get()!, VaultStorageMigrationTargetMode.EncryptedSqlite, DateTime.UtcNow, "export failed");
		File.WriteAllBytes(PlainToEncryptedVaultMigrationArtifacts.GetEncryptedTempPath(_databasePath), [0x01, 0x02]);
		File.WriteAllBytes(PlainToEncryptedVaultMigrationArtifacts.GetPlainBackupPath(_databasePath), [0x03, 0x04]);

		var decision = coordinator.GetRecoveryDecision(header);

		Assert.Equal(PlainToEncryptedVaultMigrationRecoveryAction.Fail, decision.Action);
		Assert.Contains("nicht stillschweigend", decision.Message);
		Assert.True(File.Exists(_databasePath));
	}

	[Fact]
	public void StateTransitions_RoundTripThroughHeaderFields()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var header = _masterKeyRepository.Get()!;

		var inProgress = coordinator.MarkInProgress(header, VaultStorageMigrationTargetMode.EncryptedSqlite, DateTime.UtcNow.AddMinutes(-2));
		Assert.Equal(VaultStorageMigrationState.InProgress, inProgress.StorageMigrationState);

		var finalizing = coordinator.MarkFinalizationPending(inProgress, VaultStorageMigrationTargetMode.EncryptedSqlite, DateTime.UtcNow.AddMinutes(-1));
		Assert.Equal(VaultStorageMigrationState.FinalizationPending, finalizing.StorageMigrationState);

		var failed = coordinator.MarkFailed(finalizing, VaultStorageMigrationTargetMode.EncryptedSqlite, DateTime.UtcNow, "backup locked");
		Assert.Equal(VaultStorageMigrationState.Failed, failed.StorageMigrationState);
		Assert.Equal("backup locked", failed.LastStorageMigrationError);

		var cleared = coordinator.ClearMigrationState(failed, DateTime.UtcNow);
		Assert.Equal(VaultStorageMigrationState.None, cleared.StorageMigrationState);
		Assert.Equal(VaultStorageMigrationTargetMode.None, cleared.StorageMigrationTargetMode);
		Assert.Null(cleared.LastStorageMigrationAttemptUtc);
		Assert.Null(cleared.LastStorageMigrationError);
	}

	[Fact]
	public void Execute_WhenExporterTargetModeDiffers_ThrowsInvalidOperationException()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var exporter = new WrongTargetModeExporter();

		Assert.Throws<InvalidOperationException>(() => coordinator.Execute(MakeRequest(), exporter));
	}

	[Fact]
	public void Execute_WhenExporterDoesNotCreateTargetFile_MarksMigrationAsFailed()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var exporter = new MissingTargetExporter();

		var result = coordinator.Execute(MakeRequest(), exporter);

		Assert.Equal(VaultStorageMigrationState.Failed, result.StorageMigrationState);
		Assert.Contains("kein Zielartefakt", result.LastStorageMigrationError);
	}

	[Fact]
	public void FinalizeSuccessfulMigration_WhenHeaderStateIsNotFinalizationPending_Throws()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var exporter = new FakeEncryptedVaultMigrationExporter();
		var header = _masterKeyRepository.Get()!;

		Assert.Throws<InvalidOperationException>(() => coordinator.FinalizeSuccessfulMigration(header, exporter));
	}

	[Fact]
	public void Prepare_WithUnsupportedSourceStorageMode_Throws()
	{
		var descriptor = new VaultStorageDescriptor(
			VaultStorageMode.EncryptedSqlite,
			"Data Source=test.db",
			Path.Combine(_tempDirectory, "test.db"),
			requiresKeyAtOpen: true);
		var factory = new ThrowingFactory(descriptor);
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(factory);

		var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Prepare(new PlainToEncryptedVaultMigrationRequest(
			new VaultHeader
			{
				FormatVersion = VaultHeaderFormatVersion.Current,
				KdfIdentifier = "PBKDF2-SHA256",
				KdfParameters = new VaultKdfParameters(),
				Salt = Enumerable.Repeat((byte)0x01, 32).ToArray(),
				WrappedVaultKey = Enumerable.Repeat((byte)0x02, 60).ToArray(),
				LegacyPasswordHash = [],
			},
			[],
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true)));

		Assert.Contains("Plain-SQLite", ex.Message);
	}

	[Fact]
	public void Prepare_WhenStorageRequiresOpenTimeKey_Throws()
	{
		var descriptor = new VaultStorageDescriptor(
			VaultStorageMode.PlainSqlite,
			"Data Source=test.db",
			Path.Combine(_tempDirectory, "test.db"),
			requiresKeyAtOpen: true);
		var factory = new ThrowingFactory(descriptor);
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(factory);

		var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Prepare(new PlainToEncryptedVaultMigrationRequest(
			new VaultHeader
			{
				FormatVersion = VaultHeaderFormatVersion.Current,
				KdfIdentifier = "PBKDF2-SHA256",
				KdfParameters = new VaultKdfParameters(),
				Salt = Enumerable.Repeat((byte)0x01, 32).ToArray(),
				WrappedVaultKey = Enumerable.Repeat((byte)0x02, 60).ToArray(),
				LegacyPasswordHash = [],
			},
			[],
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true)));

		Assert.Contains("Open-Time-Keying", ex.Message);
	}

	[Theory]
	[InlineData(VaultStorageMigrationState.InProgress)]
	[InlineData(VaultStorageMigrationState.FinalizationPending)]
	public void Prepare_WhenHeaderHasActiveMigrationState_Throws(VaultStorageMigrationState state)
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var header = _masterKeyRepository.Get()!;
		header.StorageMigrationState = state;

		var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Prepare(new PlainToEncryptedVaultMigrationRequest(
			header,
			_credentialsRepository.GetAll(),
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true)));

		Assert.Contains("parallel", ex.Message);
	}

	[Fact]
	public void Prepare_WhenLegacyPasswordHashStillPresent_Throws()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var header = _masterKeyRepository.Get()!;
		header.LegacyPasswordHash = [0x01];

		var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Prepare(new PlainToEncryptedVaultMigrationRequest(
			header,
			_credentialsRepository.GetAll(),
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true)));

		Assert.Contains("Legacy-Passwort-Hash", ex.Message);
	}

	[Fact]
	public void Prepare_WhenCredentialContainsPlaintextResidue_Throws()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var credential = _credentialsRepository.GetAll().Single();
		credential.Title = "plain";

		var ex = Assert.Throws<InvalidOperationException>(() => coordinator.Prepare(new PlainToEncryptedVaultMigrationRequest(
			_masterKeyRepository.Get()!,
			[credential],
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true)));

		Assert.Contains("Klartext-Metadaten", ex.Message);
	}

	[Fact]
	public void RecoveryDecision_WithUnknownMigrationState_ReturnsFailDecision()
	{
		SeedValidCurrentVault();
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_factory);
		var header = _masterKeyRepository.Get()!;
		header.StorageMigrationState = (VaultStorageMigrationState)999;

		var decision = coordinator.GetRecoveryDecision(header);

		Assert.Equal(PlainToEncryptedVaultMigrationRecoveryAction.Fail, decision.Action);
		Assert.Contains("Unbekannter", decision.Message);
	}

	[Fact]
	public void RecoveryDecision_WithNullPathAndNoState_ReturnsNone()
	{
		var header = new VaultHeader
		{
			FormatVersion = VaultHeaderFormatVersion.Current,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters(),
			Salt = Enumerable.Repeat((byte)0x01, 32).ToArray(),
			WrappedVaultKey = Enumerable.Repeat((byte)0x02, 60).ToArray(),
			LegacyPasswordHash = [],
		};
		var descriptor = new VaultStorageDescriptor(VaultStorageMode.PlainSqlite, "Data Source=:memory:", databasePath: null, requiresKeyAtOpen: false);
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(new ThrowingFactory(descriptor));

		var decision = coordinator.GetRecoveryDecision(header);

		Assert.Equal(PlainToEncryptedVaultMigrationRecoveryAction.None, decision.Action);
		Assert.Null(decision.Message);
	}

	private void SeedValidCurrentVault()
	{
		if (_masterKeyRepository.Get() is null)
		{
			_masterKeyRepository.Create(new VaultHeader
			{
				FormatVersion = VaultHeaderFormatVersion.Current,
				KdfIdentifier = "PBKDF2-SHA256",
				KdfParameters = new VaultKdfParameters(),
				Salt = Enumerable.Repeat((byte)0xAA, 32).ToArray(),
				WrappedVaultKey = Enumerable.Repeat((byte)0xCC, 60).ToArray(),
				LegacyPasswordHash = [],
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow,
			});
		}

		if (_credentialsRepository.GetAll().Count == 0)
		{
			_credentialsRepository.Add(new CredentialRecord
			{
				Title = string.Empty,
				Username = null,
				EncryptedPassword = [0x01, 0x02, 0x03],
				EncryptedMetadata = [0x04, 0x05, 0x06],
				CredentialUuid = Guid.NewGuid().ToString("N"),
				SecretFormatVersion = CredentialSecretFormatVersion.Current,
				MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
				CredentialType = CredentialType.Password,
			});
		}
	}

	private PlainToEncryptedVaultMigrationRequest MakeRequest()
		=> new(
			_masterKeyRepository.Get()!,
			_credentialsRepository.GetAll(),
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true);

	private static void TryDelete(string path)
	{
		if (File.Exists(path))
		{
			try
			{
				File.Delete(path);
			}
			catch (IOException)
			{
			}
		}
	}

	private sealed class FakeEncryptedVaultMigrationExporter : IEncryptedVaultMigrationExporter
	{
		internal const string ExportMarkerKey = "FakeEncryptedExportMarker";
		internal const string ExportMarkerValue = "not-real-encryption";

		public VaultStorageMigrationTargetMode TargetMode => VaultStorageMigrationTargetMode.EncryptedSqlite;

		public bool ThrowOnExport { get; init; }

		public bool WriteInvalidTarget { get; init; }

		public bool ExportCalled { get; private set; }

		public bool ValidateCalled { get; private set; }

		public void ExportPlaintextVault(string sourceConnectionString, string destinationPath)
		{
			ExportCalled = true;
			if (ThrowOnExport)
				throw new IOException("Simulierter Exportfehler.");

			var sourcePath = StorageRewriteArtifacts.TryResolveDatabasePath(sourceConnectionString)
				?? throw new InvalidOperationException("Fake-Exporter erwartet eine dateibasierte Quell-Vault.");
			File.Copy(sourcePath, destinationPath, overwrite: true);

			if (WriteInvalidTarget)
			{
				File.WriteAllBytes(destinationPath, [0x01, 0x02, 0x03]);
				return;
			}

			using var connection = new SqliteConnection($"Data Source={destinationPath}");
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = @"
				INSERT INTO Settings (Key, Value) VALUES ($key, $value)
				ON CONFLICT(Key) DO UPDATE SET Value = $value, UpdatedAt = CURRENT_TIMESTAMP;";
			command.Parameters.AddWithValue("$key", ExportMarkerKey);
			command.Parameters.AddWithValue("$value", ExportMarkerValue);
			command.ExecuteNonQuery();
		}

		public void ValidateExportedVault(string destinationPath)
		{
			ValidateCalled = true;
			if (!StorageRewriteArtifacts.IsUsableSqliteDatabase(destinationPath))
				throw new InvalidOperationException("Fake-Exporter hat ein ungueltiges Zielartefakt erzeugt.");

			using var connection = new SqliteConnection($"Data Source={destinationPath}");
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
			command.Parameters.AddWithValue("$key", ExportMarkerKey);
			var marker = command.ExecuteScalar() as string;
			if (!string.Equals(marker, ExportMarkerValue, StringComparison.Ordinal))
				throw new InvalidOperationException("Fake-Exporter konnte das Export-Marker-Setting nicht verifizieren.");
		}

		public void PersistMigratedHeader(string databasePath, VaultHeader header)
		{
			using var connection = new SqliteConnection($"Data Source={databasePath}");
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = @"
				UPDATE MasterKey
				SET FormatVersion = $formatVersion,
					KdfIdentifier = $kdfIdentifier,
					KdfParameters = $kdfParameters,
					Salt = $salt,
					WrappedVaultKey = $wrappedVaultKey,
					UsesLegacyKeyMaterial = $usesLegacyKeyMaterial,
					RequiresStorageCompaction = $requiresStorageCompaction,
					LastStorageCompactionAttemptUtc = $lastStorageCompactionAttemptUtc,
					LastStorageCompactionFailureKind = $lastStorageCompactionFailureKind,
					LastStorageCompactionError = $lastStorageCompactionError,
					StorageMigrationState = $storageMigrationState,
					StorageMigrationTargetMode = $storageMigrationTargetMode,
					LastStorageMigrationAttemptUtc = $lastStorageMigrationAttemptUtc,
					LastStorageMigrationError = $lastStorageMigrationError,
					UpdatedAt = CURRENT_TIMESTAMP
				WHERE Id = 1;";
			command.Parameters.AddWithValue("$formatVersion", header.FormatVersion);
			command.Parameters.AddWithValue("$kdfIdentifier", header.KdfIdentifier);
			command.Parameters.AddWithValue("$kdfParameters", header.KdfParameters.Serialize());
			command.Parameters.AddWithValue("$salt", header.Salt);
			command.Parameters.AddWithValue("$wrappedVaultKey", (object?)header.WrappedVaultKey ?? DBNull.Value);
			command.Parameters.AddWithValue("$usesLegacyKeyMaterial", header.UsesLegacyKeyMaterial ? 1 : 0);
			command.Parameters.AddWithValue("$requiresStorageCompaction", header.RequiresStorageCompaction ? 1 : 0);
			command.Parameters.AddWithValue("$lastStorageCompactionAttemptUtc", (object?)header.LastStorageCompactionAttemptUtc?.ToString("O") ?? DBNull.Value);
			command.Parameters.AddWithValue("$lastStorageCompactionFailureKind", (int)header.LastStorageCompactionFailureKind);
			command.Parameters.AddWithValue("$lastStorageCompactionError", (object?)header.LastStorageCompactionError ?? DBNull.Value);
			command.Parameters.AddWithValue("$storageMigrationState", (int)header.StorageMigrationState);
			command.Parameters.AddWithValue("$storageMigrationTargetMode", (int)header.StorageMigrationTargetMode);
			command.Parameters.AddWithValue("$lastStorageMigrationAttemptUtc", (object?)header.LastStorageMigrationAttemptUtc?.ToString("O") ?? DBNull.Value);
			command.Parameters.AddWithValue("$lastStorageMigrationError", (object?)header.LastStorageMigrationError ?? DBNull.Value);
			command.ExecuteNonQuery();
		}
	}

	private sealed class WrongTargetModeExporter : IEncryptedVaultMigrationExporter
	{
		public VaultStorageMigrationTargetMode TargetMode => VaultStorageMigrationTargetMode.None;

		public void ExportPlaintextVault(string sourceConnectionString, string destinationPath)
		{
		}

		public void ValidateExportedVault(string destinationPath)
		{
		}

		public void PersistMigratedHeader(string databasePath, VaultHeader header)
		{
		}
	}

	private sealed class MissingTargetExporter : IEncryptedVaultMigrationExporter
	{
		public VaultStorageMigrationTargetMode TargetMode => VaultStorageMigrationTargetMode.EncryptedSqlite;

		public void ExportPlaintextVault(string sourceConnectionString, string destinationPath)
		{
			if (File.Exists(destinationPath))
				File.Delete(destinationPath);
		}

		public void ValidateExportedVault(string destinationPath)
		{
		}

		public void PersistMigratedHeader(string databasePath, VaultHeader header)
		{
		}
	}

	private sealed class ThrowingFactory : ISqliteConnectionFactory
	{
		public ThrowingFactory(VaultStorageDescriptor storage)
		{
			Storage = storage;
		}

		public VaultStorageDescriptor Storage { get; }

		public SqliteConnection OpenConnection() => throw new InvalidOperationException("Should not open connection in this test path.");
	}
}
