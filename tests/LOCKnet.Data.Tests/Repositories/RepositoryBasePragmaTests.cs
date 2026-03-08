using LOCKnet.Data;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests.Repositories;

public sealed class RepositoryBasePragmaTests : IDisposable
{
	private readonly string _databasePath;

	public RepositoryBasePragmaTests()
	{
		_databasePath = Path.Combine(Path.GetTempPath(), $"locknet-pragmas-{Guid.NewGuid():N}.db");
		new Database(_databasePath).Initialize();
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
	public void GetConnection_AppliesHardeningPragmas()
	{
		using var connection = new TestRepository($"Data Source={_databasePath}").Open();

		Assert.Equal("delete", ReadString(connection, "PRAGMA journal_mode;"));
		Assert.Equal(2L, ReadInt64(connection, "PRAGMA synchronous;"));
		Assert.Equal(2L, ReadInt64(connection, "PRAGMA temp_store;"));
		Assert.Equal(1L, ReadInt64(connection, "PRAGMA secure_delete;"));
		Assert.Equal("normal", ReadString(connection, "PRAGMA locking_mode;"));
	}

	private static long ReadInt64(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return (long)(command.ExecuteScalar() ?? 0L);
	}

	private static string ReadString(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
	}

	private sealed class TestRepository : RepositoryBase
	{
		public TestRepository(string connectionString) : base(connectionString)
		{
		}

		public SqliteConnection Open() => GetConnection();
	}
}
