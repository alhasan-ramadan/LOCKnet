using Microsoft.Data.Sqlite;

namespace LOCKnet.Data;

public class Database
{
	private readonly string _connectionString;

	public Database(string databasePath = "credentials.db")
	{
		_connectionString = $"Data Source={databasePath}";
	}

	public void Initialize()
	{
		using var connection = new SqliteConnection(_connectionString);
		connection.Open();

		// Credentials table
		using (var cmd = connection.CreateCommand())
		{
			cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Credentials (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Username TEXT,
                    EncryptedPassword BLOB NOT NULL,
                    URL TEXT,
                    Notes TEXT,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                );";
			cmd.ExecuteNonQuery();
		}

		// MasterKey table
		using (var cmd = connection.CreateCommand())
		{
			cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS MasterKey (
                    Id INTEGER PRIMARY KEY CHECK(Id = 1),
                    PasswordHash BLOB NOT NULL,
                    Salt BLOB NOT NULL,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                );";
			cmd.ExecuteNonQuery();
		}

		// Settings table
		using (var cmd = connection.CreateCommand())
		{
			cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Settings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Key TEXT NOT NULL UNIQUE,
                    Value TEXT NOT NULL,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                );";
			cmd.ExecuteNonQuery();
		}
	}
}
