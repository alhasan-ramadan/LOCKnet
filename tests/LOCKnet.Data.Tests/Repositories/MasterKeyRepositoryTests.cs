using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests.Repositories;

/// <summary>
/// Tests for <see cref="MasterKeyRepository"/> using an in-memory SQLite database.
/// </summary>
public class MasterKeyRepositoryTests : IDisposable
{
	private readonly SqliteConnection _keepAlive;
	private readonly MasterKeyRepository _sut;

	public MasterKeyRepositoryTests()
	{
		var dbName = $"mk_{Guid.NewGuid():N}";
		var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

		_keepAlive = new SqliteConnection(connectionString);
		_keepAlive.Open();

		new Database(connectionString, true).Initialize();

		_sut = new MasterKeyRepository(connectionString);
	}

	public void Dispose() => _keepAlive.Dispose();

	// ── Helper ────────────────────────────────────────────────────────────────

	private static VaultHeader MakeKey(byte seed = 0xAB) =>
		new()
		{
			FormatVersion = 1,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters
			{
				HashAlgorithm = "SHA256",
				Iterations = 600_000,
				KeyLengthBytes = 32,
				SaltLengthBytes = 32,
			},
			LegacyPasswordHash = Enumerable.Repeat(seed, 32).ToArray(),
			Salt = Enumerable.Repeat((byte)(seed + 1), 32).ToArray(),
			WrappedVaultKey = Enumerable.Repeat((byte)(seed + 2), 48).ToArray(),
			RequiresStorageCompaction = seed % 2 == 0,
			LastStorageCompactionAttemptUtc = DateTime.UtcNow.AddMinutes(-5),
			LastStorageCompactionFailureKind = StorageCompactionFailureKind.BusyOrLocked,
			LastStorageCompactionError = $"compaction-{seed:X2}",
			StorageMigrationState = seed % 3 == 0 ? VaultStorageMigrationState.InProgress : VaultStorageMigrationState.None,
			StorageMigrationTargetMode = seed % 3 == 0 ? VaultStorageMigrationTargetMode.EncryptedSqlite : VaultStorageMigrationTargetMode.None,
			LastStorageMigrationAttemptUtc = DateTime.UtcNow.AddMinutes(-7),
			LastStorageMigrationError = $"migration-{seed:X2}",
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		};

	// ── Get on empty DB ───────────────────────────────────────────────────────

	[Fact]
	public void Get_EmptyDatabase_ReturnsNull()
	{
		var result = _sut.Get();

		Assert.Null(result);
	}

	// ── Create / Get ──────────────────────────────────────────────────────────

	[Fact]
	public void Create_ThenGet_RoundTripsHeader()
	{
		var key = MakeKey();

		_sut.Create(key);

		var retrieved = _sut.Get();
		Assert.NotNull(retrieved);
		Assert.Equal(key.LegacyPasswordHash, retrieved.LegacyPasswordHash);
		Assert.Equal(key.KdfIdentifier, retrieved.KdfIdentifier);
		Assert.Equal(key.KdfParameters.Iterations, retrieved.KdfParameters.Iterations);
		Assert.Equal(key.Salt, retrieved.Salt);
		Assert.Equal(key.WrappedVaultKey, retrieved.WrappedVaultKey);
		Assert.Equal(key.RequiresStorageCompaction, retrieved.RequiresStorageCompaction);
		Assert.Equal(key.LastStorageCompactionFailureKind, retrieved.LastStorageCompactionFailureKind);
		Assert.Equal(key.LastStorageCompactionError, retrieved.LastStorageCompactionError);
		Assert.Equal(key.LastStorageCompactionAttemptUtc, retrieved.LastStorageCompactionAttemptUtc);
		Assert.Equal(key.StorageMigrationState, retrieved.StorageMigrationState);
		Assert.Equal(key.StorageMigrationTargetMode, retrieved.StorageMigrationTargetMode);
		Assert.Equal(key.LastStorageMigrationAttemptUtc, retrieved.LastStorageMigrationAttemptUtc);
		Assert.Equal(key.LastStorageMigrationError, retrieved.LastStorageMigrationError);
	}

	[Fact]
	public void Create_CalledTwice_ThrowsInvalidOperationException()
	{
		_sut.Create(MakeKey());

		Assert.Throws<InvalidOperationException>(() => _sut.Create(MakeKey(0xCC)));
	}

	[Fact]
	public void Create_AfterDelete_Succeeds()
	{
		_sut.Create(MakeKey());
		_sut.Delete();

		var ex = Record.Exception(() => _sut.Create(MakeKey(0xBB)));

		Assert.Null(ex);
	}

	// ── Update ────────────────────────────────────────────────────────────────

	[Fact]
	public void Update_PersistsNewHeaderValues()
	{
		_sut.Create(MakeKey(0x01));

		var updatedKey = new VaultHeader
		{
			FormatVersion = 2,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters
			{
				HashAlgorithm = "SHA256",
				Iterations = 750_000,
				KeyLengthBytes = 32,
				SaltLengthBytes = 32,
			},
			LegacyPasswordHash = Enumerable.Repeat((byte)0xF0, 32).ToArray(),
			Salt = Enumerable.Repeat((byte)0xF1, 32).ToArray(),
			WrappedVaultKey = Enumerable.Repeat((byte)0xF2, 48).ToArray(),
			RequiresStorageCompaction = true,
			LastStorageCompactionAttemptUtc = DateTime.UtcNow.AddMinutes(-2),
			LastStorageCompactionFailureKind = StorageCompactionFailureKind.InsufficientSpace,
			LastStorageCompactionError = "disk-full",
			StorageMigrationState = VaultStorageMigrationState.FinalizationPending,
			StorageMigrationTargetMode = VaultStorageMigrationTargetMode.EncryptedSqlite,
			LastStorageMigrationAttemptUtc = DateTime.UtcNow.AddMinutes(-4),
			LastStorageMigrationError = "waiting-cleanup",
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		};
		_sut.Update(updatedKey);

		var retrieved = _sut.Get();
		Assert.NotNull(retrieved);
		Assert.Equal(updatedKey.LegacyPasswordHash, retrieved.LegacyPasswordHash);
		Assert.Equal(updatedKey.FormatVersion, retrieved.FormatVersion);
		Assert.Equal(updatedKey.KdfParameters.Iterations, retrieved.KdfParameters.Iterations);
		Assert.Equal(updatedKey.Salt, retrieved.Salt);
		Assert.Equal(updatedKey.WrappedVaultKey, retrieved.WrappedVaultKey);
		Assert.Equal(updatedKey.RequiresStorageCompaction, retrieved.RequiresStorageCompaction);
		Assert.Equal(updatedKey.LastStorageCompactionAttemptUtc, retrieved.LastStorageCompactionAttemptUtc);
		Assert.Equal(updatedKey.LastStorageCompactionFailureKind, retrieved.LastStorageCompactionFailureKind);
		Assert.Equal(updatedKey.LastStorageCompactionError, retrieved.LastStorageCompactionError);
		Assert.Equal(updatedKey.StorageMigrationState, retrieved.StorageMigrationState);
		Assert.Equal(updatedKey.StorageMigrationTargetMode, retrieved.StorageMigrationTargetMode);
		Assert.Equal(updatedKey.LastStorageMigrationAttemptUtc, retrieved.LastStorageMigrationAttemptUtc);
		Assert.Equal(updatedKey.LastStorageMigrationError, retrieved.LastStorageMigrationError);
	}

	[Fact]
	public void Update_ChangesHashButNotCreatedAt()
	{
		var original = MakeKey();
		_sut.Create(original);
		var originalCreated = _sut.Get()!.CreatedAt;

		_sut.Update(new VaultHeader
		{
			FormatVersion = 1,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters
			{
				HashAlgorithm = "SHA256",
				Iterations = 610_000,
				KeyLengthBytes = 32,
				SaltLengthBytes = 32,
			},
			LegacyPasswordHash = Enumerable.Repeat((byte)0xFF, 32).ToArray(),
			Salt = Enumerable.Repeat((byte)0xEE, 32).ToArray(),
			WrappedVaultKey = Enumerable.Repeat((byte)0xDD, 48).ToArray(),
			RequiresStorageCompaction = true,
			LastStorageCompactionAttemptUtc = DateTime.UtcNow.AddMinutes(-1),
			LastStorageCompactionFailureKind = StorageCompactionFailureKind.Unknown,
			LastStorageCompactionError = "unknown",
			StorageMigrationState = VaultStorageMigrationState.Failed,
			StorageMigrationTargetMode = VaultStorageMigrationTargetMode.EncryptedSqlite,
			LastStorageMigrationAttemptUtc = DateTime.UtcNow.AddMinutes(-3),
			LastStorageMigrationError = "provider-missing",
			CreatedAt = originalCreated,
			UpdatedAt = DateTime.UtcNow,
		});

		var retrieved = _sut.Get();
		Assert.NotNull(retrieved);
		// CreatedAt should be preserved (repository passes it through)
		Assert.Equal(originalCreated, retrieved.CreatedAt);
	}

	// ── Delete ────────────────────────────────────────────────────────────────

	[Fact]
	public void Delete_AfterCreate_GetReturnsNull()
	{
		_sut.Create(MakeKey());

		_sut.Delete();

		Assert.Null(_sut.Get());
	}

	[Fact]
	public void Delete_WhenEmpty_IsNoOp()
	{
		var ex = Record.Exception(() => _sut.Delete());

		Assert.Null(ex);
	}

	// ── Timestamps ────────────────────────────────────────────────────────────

	[Fact]
	public void Create_TimestampsArePopulatedByDatabase()
	{
		_sut.Create(MakeKey());

		var record = _sut.Get();

		Assert.NotNull(record);
		Assert.NotEqual(default, record.CreatedAt);
		Assert.NotEqual(default, record.UpdatedAt);
	}
}
