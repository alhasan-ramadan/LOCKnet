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
}
