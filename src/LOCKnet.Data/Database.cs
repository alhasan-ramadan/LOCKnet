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
		const string kdfParametersDefault = "TEXT NOT NULL DEFAULT '{\"HashAlgorithm\":\"SHA256\",\"Iterations\":600000,\"KeyLengthBytes\":32,\"SaltLengthBytes\":32}'";

		using var connection = new SqliteConnection(_connectionString);
		connection.Open();
		Repositories.RepositoryBase.ConfigureConnection(connection);

		// Credentials table
		using (var cmd = connection.CreateCommand())
		{
			cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Credentials (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Username TEXT,
                    EncryptedPassword BLOB NOT NULL,
                    EncryptedMetadata BLOB,
                    CredentialUuid TEXT NOT NULL DEFAULT '',
                    SecretFormatVersion INTEGER NOT NULL DEFAULT 0,
                    MetadataFormatVersion INTEGER NOT NULL DEFAULT 0,
                    URL TEXT,
                    Notes TEXT,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                );";
			cmd.ExecuteNonQuery();
		}

		try
		{
			using var migrationCommand = connection.CreateCommand();
			migrationCommand.CommandText = "ALTER TABLE Credentials ADD COLUMN IconKey TEXT;";
			migrationCommand.ExecuteNonQuery();
		}
		catch (SqliteException)
		{
		}

		try
		{
			using var mc = connection.CreateCommand();
			mc.CommandText = "ALTER TABLE Credentials ADD COLUMN CredentialType INTEGER NOT NULL DEFAULT 0;";
			mc.ExecuteNonQuery();
		}
		catch (SqliteException)
		{
		}

		// MasterKey table
		using (var cmd = connection.CreateCommand())
		{
			cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS MasterKey (
                    Id INTEGER PRIMARY KEY CHECK(Id = 1),
                    PasswordHash BLOB NOT NULL,
                    FormatVersion INTEGER NOT NULL DEFAULT 1,
                    KdfIdentifier TEXT NOT NULL DEFAULT 'PBKDF2-SHA256',
                    KdfParameters TEXT NOT NULL DEFAULT '{""HashAlgorithm"":""SHA256"",""Iterations"":600000,""KeyLengthBytes"":32,""SaltLengthBytes"":32}',
                    Salt BLOB NOT NULL,
                    WrappedVaultKey BLOB,
                    RequiresStorageCompaction INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                );";
			cmd.ExecuteNonQuery();
		}

		TryAddColumn(connection, "MasterKey", "FormatVersion", "INTEGER NOT NULL DEFAULT 1");
		TryAddColumn(connection, "MasterKey", "KdfIdentifier", "TEXT NOT NULL DEFAULT 'PBKDF2-SHA256'");
		TryAddColumn(connection, "MasterKey", "KdfParameters", kdfParametersDefault);
		TryAddColumn(connection, "MasterKey", "WrappedVaultKey", "BLOB");
		TryAddColumn(connection, "MasterKey", "UsesLegacyKeyMaterial", "INTEGER NOT NULL DEFAULT 0");
		TryAddColumn(connection, "MasterKey", "RequiresStorageCompaction", "INTEGER NOT NULL DEFAULT 0");

		TryAddColumn(connection, "Credentials", "CredentialUuid", "TEXT NOT NULL DEFAULT ''");
		TryAddColumn(connection, "Credentials", "SecretFormatVersion", "INTEGER NOT NULL DEFAULT 0");
		TryAddColumn(connection, "Credentials", "EncryptedMetadata", "BLOB");
		TryAddColumn(connection, "Credentials", "MetadataFormatVersion", "INTEGER NOT NULL DEFAULT 0");

		using (var cmd = connection.CreateCommand())
		{
			cmd.CommandText = @"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Credentials_CredentialUuid
                ON Credentials(CredentialUuid)
                WHERE CredentialUuid <> '';";
			cmd.ExecuteNonQuery();
		}

		using (var cmd = connection.CreateCommand())
		{
			cmd.CommandText = @"
                CREATE TRIGGER IF NOT EXISTS TRG_Credentials_CurrentMetadata_Insert
                BEFORE INSERT ON Credentials
                WHEN NEW.MetadataFormatVersion = 1 AND (
                    NEW.EncryptedMetadata IS NULL OR length(NEW.EncryptedMetadata) = 0 OR
                    NEW.Title <> '' OR ifnull(NEW.Username, '') <> '' OR ifnull(NEW.URL, '') <> '' OR
                    ifnull(NEW.Notes, '') <> '' OR ifnull(NEW.IconKey, '') <> '' OR ifnull(NEW.CredentialType, 0) <> 0
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Current metadata records must not persist plaintext metadata.');
                END;";
			cmd.ExecuteNonQuery();
		}

		using (var cmd = connection.CreateCommand())
		{
			cmd.CommandText = @"
                CREATE TRIGGER IF NOT EXISTS TRG_Credentials_CurrentMetadata_Update
                BEFORE UPDATE ON Credentials
                WHEN NEW.MetadataFormatVersion = 1 AND (
                    NEW.EncryptedMetadata IS NULL OR length(NEW.EncryptedMetadata) = 0 OR
                    NEW.Title <> '' OR ifnull(NEW.Username, '') <> '' OR ifnull(NEW.URL, '') <> '' OR
                    ifnull(NEW.Notes, '') <> '' OR ifnull(NEW.IconKey, '') <> '' OR ifnull(NEW.CredentialType, 0) <> 0
                )
                BEGIN
                    SELECT RAISE(ABORT, 'Current metadata records must not persist plaintext metadata.');
                END;";
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

	private static void TryAddColumn(SqliteConnection connection, string table, string column, string definition)
	{
		try
		{
			using var command = connection.CreateCommand();
			command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
			command.ExecuteNonQuery();
		}
		catch (SqliteException)
		{
		}
	}
}
