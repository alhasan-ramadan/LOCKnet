using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests;

public sealed class StorageRewriteArtifactsTests : IDisposable
{
	private readonly string _tempDirectory;

	public StorageRewriteArtifactsTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), $"locknet-rewrite-artifacts-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDirectory);
	}

	public void Dispose()
	{
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
	public void TryResolveDatabasePath_WithWhitespaceOrMissingDataSource_ReturnsNull()
	{
		Assert.Null(StorageRewriteArtifacts.TryResolveDatabasePath("  "));
		Assert.Null(StorageRewriteArtifacts.TryResolveDatabasePath("Mode=ReadWriteCreate"));
		Assert.Null(StorageRewriteArtifacts.TryResolveDatabasePath("Data Source=:memory:"));
	}

	[Fact]
	public void IsUsableSqliteDatabase_WithMissingOrCorruptedFile_ReturnsFalse()
	{
		var missingPath = Path.Combine(_tempDirectory, "missing.db");
		var corruptedPath = Path.Combine(_tempDirectory, "corrupted.db");
		File.WriteAllBytes(corruptedPath, [0x01, 0x02, 0x03, 0x04]);

		Assert.False(StorageRewriteArtifacts.IsUsableSqliteDatabase(missingPath));
		Assert.False(StorageRewriteArtifacts.IsUsableSqliteDatabase(corruptedPath));
	}

	[Fact]
	public void Recover_WhenMainMissingAndOnlyInvalidArtifactsExist_Throws()
	{
		var databasePath = Path.Combine(_tempDirectory, "locknet.db");
		var backupPath = StorageRewriteArtifacts.GetBackupPath(databasePath);
		var tempPath = StorageRewriteArtifacts.GetTempPath(databasePath);
		File.WriteAllBytes(backupPath, [0xAA, 0xBB, 0xCC]);
		File.WriteAllBytes(tempPath, [0x11, 0x22, 0x33]);

		Assert.Throws<InvalidOperationException>(() => StorageRewriteArtifacts.Recover(databasePath));
	}

	[Fact]
	public void Recover_WhenMainExistsButInvalidAndArtifactsRemain_Throws()
	{
		var databasePath = Path.Combine(_tempDirectory, "locknet.db");
		var backupPath = StorageRewriteArtifacts.GetBackupPath(databasePath);
		File.WriteAllBytes(databasePath, [0x10, 0x20, 0x30]);
		File.WriteAllBytes(backupPath, [0x40, 0x50, 0x60]);

		Assert.Throws<InvalidOperationException>(() => StorageRewriteArtifacts.Recover(databasePath));
	}

	[Fact]
	public void Recover_WithWhitespacePath_ReturnsDefaultOutcome()
	{
		var outcome = StorageRewriteArtifacts.Recover("   ");

		Assert.False(outcome.ShouldClearPendingState);
	}

	[Fact]
	public void ClearPendingState_ResetsCompactionColumns()
	{
		var databasePath = Path.Combine(_tempDirectory, "state.db");
		new Database(databasePath).Initialize();
		var factory = new PlainSqliteConnectionFactory(databasePath);
		var repo = new LOCKnet.Data.Repositories.MasterKeyRepository(factory);
		repo.Create(new Core.DataAbstractions.VaultHeader
		{
			FormatVersion = Core.DataAbstractions.VaultHeaderFormatVersion.Current,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new Core.DataAbstractions.VaultKdfParameters(),
			Salt = Enumerable.Repeat((byte)0x01, 32).ToArray(),
			WrappedVaultKey = Enumerable.Repeat((byte)0x02, 60).ToArray(),
			LegacyPasswordHash = [],
			RequiresStorageCompaction = true,
			LastStorageCompactionFailureKind = Core.DataAbstractions.StorageCompactionFailureKind.Io,
			LastStorageCompactionError = "io-error",
			LastStorageCompactionAttemptUtc = DateTime.UtcNow,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		});

		using var connection = new SqliteConnection($"Data Source={databasePath}");
		connection.Open();
		StorageRewriteArtifacts.ClearPendingState(connection);

		var header = repo.Get()!;
		Assert.False(header.RequiresStorageCompaction);
		Assert.Equal(Core.DataAbstractions.StorageCompactionFailureKind.None, header.LastStorageCompactionFailureKind);
		Assert.Null(header.LastStorageCompactionAttemptUtc);
		Assert.Null(header.LastStorageCompactionError);
	}
}
