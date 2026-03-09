using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using LOCKnet.Core.Services;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;
using System.Security;
using System.Security.Cryptography;

namespace LOCKnet.Data.Tests.Repositories;

public sealed class StorageRewriteIntegrationTests : IDisposable
{
	private readonly string _tempDirectory;
	private readonly string _databasePath;
	private readonly string _connectionString;
	private readonly MasterKeyRepository _masterKeyRepository;
	private readonly CredentialsRepository _credentialsRepository;
	private readonly SessionManager _sessionManager;
	private readonly MasterKeyManager _masterKeyManager;
	private readonly CredentialService _credentialService;

	public StorageRewriteIntegrationTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), $"locknet-rewrite-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDirectory);
		_databasePath = Path.Combine(_tempDirectory, "locknet.db");
		_connectionString = $"Data Source={_databasePath}";

		new Database(_databasePath).Initialize();

		var encryption = new AesGcmEncryptionService();
		var credentialEnvelope = new CredentialEnvelopeService(encryption);
		_sessionManager = new SessionManager();
		_masterKeyRepository = new MasterKeyRepository(_connectionString);
		_credentialsRepository = new CredentialsRepository(_connectionString);
		var vaultMigrationRepository = new VaultMigrationRepository(_connectionString);
		_masterKeyManager = new MasterKeyManager(
			new Pbkdf2KeyDerivationService(),
			_masterKeyRepository,
			vaultMigrationRepository,
			encryption,
			credentialEnvelope,
			_sessionManager,
			new SecureStringService());
		_credentialService = new CredentialService(_credentialsRepository, _masterKeyRepository, encryption, credentialEnvelope, _sessionManager, new SecureStringService());
	}

	public void Dispose()
	{
		_sessionManager.Lock();
		TryDelete(Path.Combine(_tempDirectory, "locknet.db"));
		TryDelete(Path.Combine(_tempDirectory, "locknet.db" + StorageRewriteArtifacts.RewriteTempSuffix));
		TryDelete(Path.Combine(_tempDirectory, "locknet.db" + StorageRewriteArtifacts.RewriteBackupSuffix));
		try
		{
			if (Directory.Exists(_tempDirectory))
				Directory.Delete(_tempDirectory, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	[Fact]
	public void RetryPendingStorageCompaction_SuccessfulRewrite_ClearsPendingStateAndPreservesData()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		UnlockAndOpenSession("vault-password");
		_credentialService.Add("GitHub", "alice", MakeSecure("secret-value"), "https://example.test", "note", "icon", CredentialType.ApiKey);
		MarkCleanupPending();

		var info = _masterKeyManager.RetryPendingStorageCompaction();

		Assert.False(info.IsPending, $"{info.FailureKind}: {info.UserMessage} | {info.LastError}");
		Assert.False(_masterKeyRepository.Get()!.RequiresStorageCompaction);
		Assert.Null(_masterKeyRepository.Get()!.LastStorageCompactionAttemptUtc);
		Assert.Equal(StorageCompactionFailureKind.None, _masterKeyRepository.Get()!.LastStorageCompactionFailureKind);
		Assert.Null(_masterKeyRepository.Get()!.LastStorageCompactionError);
		Assert.False(File.Exists(StorageRewriteArtifacts.GetTempPath(_databasePath)));
		Assert.False(File.Exists(StorageRewriteArtifacts.GetBackupPath(_databasePath)));

		var credential = _credentialService.GetAll().Single();
		Assert.Equal("GitHub", credential.Title);
		Assert.Equal("alice", credential.Username);
		Assert.Equal(CredentialType.ApiKey, credential.CredentialType);
	}

	[Fact]
	public void RetryPendingStorageCompaction_MalformedCurrentRow_ReturnsCorruptionAndPreservesPendingState()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		UnlockAndOpenSession("vault-password");
		_credentialService.Add("GitHub", "alice", MakeSecure("secret-value"), "https://example.test", "note", "icon", CredentialType.ApiKey);
		var credential = _credentialsRepository.GetAll().Single();
		using (var connection = new SqliteConnection(_connectionString))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = "UPDATE Credentials SET EncryptedPassword = $password WHERE Id = $id;";
			command.Parameters.AddWithValue("$password", new byte[] { 0x01, 0x02, 0x03, 0x04 });
			command.Parameters.AddWithValue("$id", credential.Id);
			command.ExecuteNonQuery();
		}
		MarkCleanupPending();

		var info = _masterKeyManager.RetryPendingStorageCompaction();

		Assert.True(info.IsPending);
		Assert.Equal(StorageCompactionFailureKind.Corruption, info.FailureKind);
		Assert.True(_masterKeyRepository.Get()!.RequiresStorageCompaction);
		Assert.Equal(StorageCompactionFailureKind.Corruption, _masterKeyRepository.Get()!.LastStorageCompactionFailureKind);
		Assert.False(File.Exists(StorageRewriteArtifacts.GetTempPath(_databasePath)));
		Assert.False(File.Exists(StorageRewriteArtifacts.GetBackupPath(_databasePath)));
	}

	[Fact]
	public void CompactStorage_WhenInterruptedBeforeReplacement_LeavesPrimaryIntactAndStartupRemovesTempArtifact()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		var originalHeader = _masterKeyRepository.Get()!.Serialize();
		var sut = new VaultMigrationRepository(
			_connectionString,
			new StorageRewriteHooks
			{
				AfterVacuumInto = _ => throw new IOException("stop-before-replace")
			});

		var info = sut.CompactStorage();

		Assert.True(info.IsPending);
		Assert.Equal(StorageCompactionFailureKind.Io, info.FailureKind);
		Assert.Equal(originalHeader, _masterKeyRepository.Get()!.Serialize());
		Assert.True(File.Exists(StorageRewriteArtifacts.GetTempPath(_databasePath)));
		Assert.False(File.Exists(StorageRewriteArtifacts.GetBackupPath(_databasePath)));

		new Database(_databasePath).Initialize();

		Assert.False(File.Exists(StorageRewriteArtifacts.GetTempPath(_databasePath)));
		Assert.Equal(originalHeader, _masterKeyRepository.Get()!.Serialize());
	}

	[Fact]
	public void CompactStorage_WhenInterruptedAfterTempCreationButBeforeCompletion_LeavesRecoverableTempArtifact()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		var originalHeader = _masterKeyRepository.Get()!.Serialize();
		var sut = new VaultMigrationRepository(
			_connectionString,
			new StorageRewriteHooks
			{
				BeforeVacuumInto = tempPath =>
				{
					File.WriteAllBytes(tempPath, new byte[] { 0x01, 0x02, 0x03 });
					throw new IOException("stop-after-temp-created");
				}
			});

		var info = sut.CompactStorage();

		Assert.True(info.IsPending);
		Assert.Equal(StorageCompactionFailureKind.Io, info.FailureKind);
		Assert.Equal(originalHeader, _masterKeyRepository.Get()!.Serialize());
		Assert.True(File.Exists(StorageRewriteArtifacts.GetTempPath(_databasePath)));

		new Database(_databasePath).Initialize();

		Assert.False(File.Exists(StorageRewriteArtifacts.GetTempPath(_databasePath)));
		Assert.Equal(originalHeader, _masterKeyRepository.Get()!.Serialize());
	}

	[Fact]
	public void DatabaseInitialize_WhenBackupExistsAndPrimaryMissing_RestoresBackupAndKeepsPendingState()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		MarkCleanupPending();
		var backupPath = StorageRewriteArtifacts.GetBackupPath(_databasePath);
		File.Copy(_databasePath, backupPath);
		Assert.True(StorageRewriteArtifacts.TryDeleteFile(_databasePath));

		new Database(_databasePath).Initialize();

		Assert.True(File.Exists(_databasePath));
		Assert.False(File.Exists(backupPath), _masterKeyRepository.Get()!.Serialize());
		Assert.True(StorageRewriteArtifacts.IsUsableSqliteDatabase(_databasePath));
		Assert.True(_masterKeyRepository.Get()!.RequiresStorageCompaction);
	}

	[Fact]
	public void DatabaseInitialize_WhenPrimaryValidAndBackupExists_FinalizesBackupCleanupAndClearsPendingState()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		MarkCleanupPending();
		var backupPath = StorageRewriteArtifacts.GetBackupPath(_databasePath);
		File.Copy(_databasePath, backupPath);

		new Database(_databasePath).Initialize();

		Assert.False(File.Exists(backupPath));
		Assert.False(_masterKeyRepository.Get()!.RequiresStorageCompaction);
		Assert.Null(_masterKeyRepository.Get()!.LastStorageCompactionAttemptUtc);
		Assert.Equal(StorageCompactionFailureKind.None, _masterKeyRepository.Get()!.LastStorageCompactionFailureKind);
		Assert.Null(_masterKeyRepository.Get()!.LastStorageCompactionError);
	}

	[Fact]
	public void DatabaseInitialize_WhenPrimaryMissingAndTempIsValid_PromotesTempAndKeepsPendingState()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		MarkCleanupPending();
		var tempPath = StorageRewriteArtifacts.GetTempPath(_databasePath);
		File.Copy(_databasePath, tempPath);
		Assert.True(StorageRewriteArtifacts.TryDeleteFile(_databasePath));

		new Database(_databasePath).Initialize();

		Assert.True(File.Exists(_databasePath));
		Assert.False(File.Exists(tempPath));
		Assert.True(StorageRewriteArtifacts.IsUsableSqliteDatabase(_databasePath));
		Assert.True(_masterKeyRepository.Get()!.RequiresStorageCompaction);
	}

	[Fact]
	public void CompactStorage_WhenMasterKeyRowMissing_ReturnsCorruption()
	{
		var sut = new VaultMigrationRepository(_connectionString);

		var info = sut.CompactStorage();

		Assert.True(info.IsPending);
		Assert.Equal(StorageCompactionFailureKind.Corruption, info.FailureKind);
		Assert.Contains("inkonsistent", info.UserMessage);
	}

	[Fact]
	public void CompactStorage_WhenBackupAndTempArtifactsExist_FinalizesArtifactsAndReturnsSuccess()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		var sut = new VaultMigrationRepository(_connectionString);
		var backupPath = StorageRewriteArtifacts.GetBackupPath(_databasePath);
		var tempPath = StorageRewriteArtifacts.GetTempPath(_databasePath);
		File.Copy(_databasePath, backupPath, overwrite: true);
		File.Copy(_databasePath, tempPath, overwrite: true);

		var info = sut.CompactStorage();

		Assert.False(info.IsPending);
		Assert.Equal(StorageCompactionFailureKind.None, info.FailureKind);
		Assert.False(File.Exists(backupPath));
		Assert.False(File.Exists(tempPath));
	}

	[Fact]
	public void CompactStorage_WhenMainInvalidButBackupExists_ReturnsCorruptionPending()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		var sut = new VaultMigrationRepository(_connectionString);
		var backupPath = StorageRewriteArtifacts.GetBackupPath(_databasePath);
		File.Copy(_databasePath, backupPath, overwrite: true);
		SqliteConnection.ClearAllPools();
		Thread.Sleep(50);
		File.WriteAllBytes(_databasePath, [0x01, 0x02, 0x03]);

		var info = sut.CompactStorage();

		Assert.True(info.IsPending);
		Assert.Equal(StorageCompactionFailureKind.Corruption, info.FailureKind);
		Assert.Contains("Vorhandene Rewrite-Artefakte", info.UserMessage);
	}

	[Fact]
	public void CompactStorage_WhenHookThrowsUnauthorizedAccess_ReturnsBusyOrLocked()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		var sut = new VaultMigrationRepository(
			_connectionString,
			new StorageRewriteHooks
			{
				BeforeVacuumInto = _ => throw new UnauthorizedAccessException("denied")
			});

		var info = sut.CompactStorage();

		Assert.True(info.IsPending);
		Assert.Equal(StorageCompactionFailureKind.BusyOrLocked, info.FailureKind);
		Assert.Contains("gesperrt", info.UserMessage);
	}

	[Fact]
	public void CompactStorage_WhenHookThrowsInvalidOperation_ReturnsCorruption()
	{
		_masterKeyManager.Initialize(MakeSecure("vault-password"));
		var sut = new VaultMigrationRepository(
			_connectionString,
			new StorageRewriteHooks
			{
				BeforeVacuumInto = _ => throw new InvalidOperationException("invalid rewrite")
			});

		var info = sut.CompactStorage();

		Assert.True(info.IsPending);
		Assert.Equal(StorageCompactionFailureKind.Corruption, info.FailureKind);
		Assert.Contains("inkonsistent", info.UserMessage);
	}

	private void UnlockAndOpenSession(string password)
	{
		var unlock = _masterKeyManager.Unlock(MakeSecure(password))!;
		var copy = unlock.VaultKey.ToArray();
		CryptographicOperations.ZeroMemory(unlock.VaultKey);
		_sessionManager.Open(copy);
	}

	private void MarkCleanupPending(StorageCompactionFailureKind failureKind = StorageCompactionFailureKind.None, string? error = null)
	{
		var header = _masterKeyRepository.Get()!;
		header.RequiresStorageCompaction = true;
		header.LastStorageCompactionAttemptUtc = null;
		header.LastStorageCompactionFailureKind = failureKind;
		header.LastStorageCompactionError = error;
		_masterKeyRepository.Update(header);
	}

	private static SecureString MakeSecure(string value)
	{
		var secure = new SecureString();
		foreach (var c in value)
			secure.AppendChar(c);
		secure.MakeReadOnly();
		return secure;
	}

	private static void TryDelete(string path)
	{
		StorageRewriteArtifacts.TryDeleteFile(path);
	}
}
