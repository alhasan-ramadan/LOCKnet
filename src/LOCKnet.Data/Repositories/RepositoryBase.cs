using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

/// <summary>
/// Abstrakte Basisklasse für alle SQLite-Repositories.
/// Hält den Connection-String und öffnet bei Bedarf eine neue Verbindung.
/// Jede Methode öffnet eine eigene Verbindung — kein Connection-Pooling.
/// </summary>
public abstract class RepositoryBase
{
	/// <summary>SQLite-Connection-String für diese Repository-Instanz.</summary>
	protected readonly string _connectionString;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="RepositoryBase"/>.
	/// </summary>
	/// <param name="connectionString">Der vollständige SQLite-Connection-String.</param>
	protected RepositoryBase(string connectionString)
	{
		_connectionString = connectionString;
	}

	/// <summary>
	/// Öffnet eine neue SQLite-Verbindung und gibt sie zurück.
	/// Der Caller ist verantwortlich für das Schließen (via <c>using</c>).
	/// </summary>
	/// <returns>Eine geöffnete <see cref="Microsoft.Data.Sqlite.SqliteConnection"/>.</returns>
	protected SqliteConnection GetConnection()
	{
		var conn = new SqliteConnection(_connectionString);
		conn.Open();
		return conn;
	}
}
