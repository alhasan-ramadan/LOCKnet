using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

/// <summary>
/// Abstrakte Basisklasse für alle SQLite-Repositories.
/// Hält den Connection-String und öffnet bei Bedarf eine neue Verbindung.
/// Jede Methode öffnet eine eigene Verbindung — kein Connection-Pooling.
/// </summary>
public abstract class RepositoryBase
{
	private readonly ISqliteConnectionFactory _connectionFactory;

	/// <summary>SQLite-Connection-String fuer diese Repository-Instanz.</summary>
	protected readonly string _connectionString;
	protected readonly string? _databasePath;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="RepositoryBase"/>.
	/// </summary>
	/// <param name="connectionString">Der vollständige SQLite-Connection-String.</param>
	protected RepositoryBase(string connectionString)
		: this(PlainSqliteConnectionFactory.FromConnectionString(connectionString))
	{
	}

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="RepositoryBase"/> mit einer zentralen Connection-Factory.
	/// </summary>
	/// <param name="connectionFactory">Factory fuer Storage-spezifische SQLite-Verbindungen.</param>
	protected RepositoryBase(ISqliteConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);

		_connectionFactory = connectionFactory;
		_connectionString = connectionFactory.Storage.ConnectionString;
		_databasePath = connectionFactory.Storage.DatabasePath;
	}

	/// <summary>
	/// Öffnet eine neue SQLite-Verbindung und gibt sie zurück.
	/// Der Caller ist verantwortlich für das Schließen (via <c>using</c>).
	/// </summary>
	/// <returns>Eine geöffnete <see cref="Microsoft.Data.Sqlite.SqliteConnection"/>.</returns>
	protected SqliteConnection GetConnection()
		=> _connectionFactory.OpenConnection();

	internal static void ConfigureConnection(SqliteConnection conn)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
                PRAGMA foreign_keys = ON;
                PRAGMA journal_mode = DELETE;
                PRAGMA synchronous = FULL;
                PRAGMA temp_store = MEMORY;
                PRAGMA secure_delete = ON;
                PRAGMA busy_timeout = 5000;
                PRAGMA locking_mode = NORMAL;";
		cmd.ExecuteNonQuery();
	}
}
