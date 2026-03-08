using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests;

public sealed class SqliteConnectionSeamTests : IDisposable
{
	private readonly string _databasePath;
	private readonly CountingConnectionFactory _factory;

	public SqliteConnectionSeamTests()
	{
		_databasePath = Path.Combine(Path.GetTempPath(), $"locknet-seam-{Guid.NewGuid():N}.db");

		_factory = new CountingConnectionFactory(_databasePath);
		new Database(_factory).Initialize();
		_factory.Reset();
	}

	public void Dispose()
	{
		if (File.Exists(_databasePath))
		{
			try
			{
				File.Delete(_databasePath);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void RepositoryBase_UsesCentralConnectionFactory()
	{
		using var connection = new TestRepository(_factory).Open();

		Assert.Equal(1, _factory.OpenCount);
		Assert.Equal("delete", ReadString(connection, "PRAGMA journal_mode;"));
	}

	[Fact]
	public void CredentialsRepository_WorksThroughFactorySeam()
	{
		var repository = new CredentialsRepository(_factory);

		repository.Add(new CredentialRecord
		{
			Title = string.Empty,
			Username = null,
			EncryptedPassword = [0x01, 0x02, 0x03],
			EncryptedMetadata = [0x0A, 0x0B],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			CredentialType = CredentialType.Password,
		});

		var stored = repository.GetAll();

		Assert.Single(stored);
		Assert.True(_factory.OpenCount >= 2);
		Assert.NotEmpty(stored[0].EncryptedMetadata);
	}

	private static string ReadString(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
	}

	private sealed class TestRepository : RepositoryBase
	{
		public TestRepository(ISqliteConnectionFactory connectionFactory) : base(connectionFactory)
		{
		}

		public SqliteConnection Open() => GetConnection();
	}

	private sealed class CountingConnectionFactory : ISqliteConnectionFactory
	{
		private readonly PlainSqliteConnectionFactory _inner;

		public CountingConnectionFactory(string databasePath)
		{
			_inner = new PlainSqliteConnectionFactory(databasePath);
			Storage = _inner.Storage;
		}

		public VaultStorageDescriptor Storage { get; }

		public int OpenCount { get; private set; }

		public SqliteConnection OpenConnection()
		{
			OpenCount++;
			return _inner.OpenConnection();
		}

		public void Reset() => OpenCount = 0;
	}
}
