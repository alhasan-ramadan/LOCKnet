using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

public abstract class RepositoryBase
{
	protected readonly string _connectionString;

	protected RepositoryBase(string connectionString)
	{
		_connectionString = connectionString;
	}

	protected SqliteConnection GetConnection()
	{
		var conn = new SqliteConnection(_connectionString);
		conn.Open();
		return conn;
	}
}
