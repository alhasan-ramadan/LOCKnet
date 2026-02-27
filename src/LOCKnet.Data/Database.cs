using Microsoft.Data.Sqlite;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("LOCKnet.Data.Tests")]

namespace LOCKnet.Data;

/// <summary>
/// Verwaltet die SQLite-Datenbankverbindung und initialisiert das Schema.
/// Erstellt beim ersten Aufruf von <see cref="Initialize"/> alle benötigten Tabellen
/// (Credentials, MasterKey, Settings) mit <c>CREATE TABLE IF NOT EXISTS</c>.
/// </summary>
public class Database
{
	private readonly string _connectionString;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="Database"/> mit einem Datei-Pfad.
	/// </summary>
	/// <param name="databasePath">
	/// Pfad zur SQLite-Datenbankdatei (Standard: <c>credentials.db</c> im Arbeitsverzeichnis).
	/// </param>
	public Database(string databasePath = "credentials.db")
	{
		_connectionString = $"Data Source={databasePath}";
	}

	/// <summary>
	/// Initializes a <see cref="Database"/> with a fully-formed connection string.
	/// Use this overload in tests when an in-memory connection string is needed.
	/// </summary>
	internal Database(string connectionString, bool useConnectionStringDirectly)
	{
		_ = useConnectionStringDirectly; // discriminator — not stored
		_connectionString = connectionString;
	}

	/// <summary>
	/// Erstellt alle Tabellen (Credentials, MasterKey, Settings) via <c>CREATE TABLE IF NOT EXISTS</c>.
	/// Kann mehrfach aufgerufen werden — idempotent.
	/// </summary>
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
