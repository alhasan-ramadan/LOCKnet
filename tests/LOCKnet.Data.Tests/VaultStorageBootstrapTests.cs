using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests;

public sealed class VaultStorageBootstrapTests : IDisposable
{
	private readonly SqliteConnection _keepAlive;
	private readonly string _connectionString;

	public VaultStorageBootstrapTests()
	{
		var dbName = $"bootstrap_{Guid.NewGuid():N}";
		_connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
		_keepAlive = new SqliteConnection(_connectionString);
		_keepAlive.Open();
	}

	public void Dispose() => _keepAlive.Dispose();

	[Fact]
	public void FromConnectionString_DetectsCurrentPlainStorageMode()
	{
		var bootstrap = VaultStorageBootstrap.FromConnectionString(_connectionString);

		Assert.Equal(VaultStorageMode.PlainSqlite, bootstrap.Storage.Mode);
		Assert.False(bootstrap.Storage.RequiresKeyAtOpen);
	}

	[Fact]
	public void InitializeAccessibleStorage_PlainMode_CreatesCurrentSchema()
	{
		var bootstrap = VaultStorageBootstrap.FromConnectionString(_connectionString);

		bootstrap.InitializeAccessibleStorage();

		Assert.True(TableExists("Credentials"));
		Assert.True(TableExists("MasterKey"));
		Assert.True(TableExists("Settings"));
	}

	[Fact]
	public void InitializeAccessibleStorage_KeyedMode_DoesNotOpenConnection()
	{
		var factory = new FutureEncryptedProbeFactory();
		var bootstrap = new VaultStorageBootstrap(factory);

		bootstrap.InitializeAccessibleStorage();

		Assert.False(factory.OpenAttempted);
		Assert.True(bootstrap.Storage.RequiresKeyAtOpen);
	}

	[Fact]
	public void CreateRepositories_PlainMode_RepositoriesRemainUsable()
	{
		var bootstrap = VaultStorageBootstrap.FromConnectionString(_connectionString);
		bootstrap.InitializeAccessibleStorage();

		var masterKeyRepository = bootstrap.CreateMasterKeyRepository();
		masterKeyRepository.Create(new VaultHeader
		{
			FormatVersion = VaultHeaderFormatVersion.Current,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters(),
			Salt = Enumerable.Repeat((byte)0x01, 32).ToArray(),
			WrappedVaultKey = Enumerable.Repeat((byte)0x02, 60).ToArray(),
			LegacyPasswordHash = [],
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		});

		var storedHeader = masterKeyRepository.Get();

		Assert.NotNull(storedHeader);
		Assert.Equal(VaultHeaderFormatVersion.Current, storedHeader!.FormatVersion);
	}

	private bool TableExists(string tableName)
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
		cmd.Parameters.AddWithValue("$name", tableName);
		return Convert.ToInt64(cmd.ExecuteScalar()!) > 0;
	}

	private sealed class FutureEncryptedProbeFactory : ISqliteConnectionFactory
	{
		public FutureEncryptedProbeFactory()
		{
			Storage = new VaultStorageDescriptor(
				VaultStorageMode.EncryptedSqlite,
				"Data Source=future-vault.db",
				Path.Combine(Path.GetTempPath(), $"future-{Guid.NewGuid():N}.db"),
				requiresKeyAtOpen: true);
		}

		public VaultStorageDescriptor Storage { get; }

		public bool OpenAttempted { get; private set; }

		public SqliteConnection OpenConnection()
		{
			OpenAttempted = true;
			throw new InvalidOperationException("Future encrypted storage should not be opened during pre-unlock bootstrap.");
		}
	}
}
