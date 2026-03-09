using Microsoft.Data.Sqlite;

namespace LOCKnet.Data;

/// <summary>
/// Oeffnet SQLite-Verbindungen ueber einen zentralen Storage-Seam.
/// </summary>
public interface ISqliteConnectionFactory
{
	/// <summary>
	/// Beschreibt den aktuell verwendeten Storage-Modus.
	/// </summary>
	VaultStorageDescriptor Storage { get; }

	/// <summary>
	/// Oeffnet eine neue SQLite-Verbindung fuer den aktuellen Storage-Modus.
	/// </summary>
	/// <returns>Eine geoeffnete <see cref="SqliteConnection"/>.</returns>
	SqliteConnection OpenConnection();
}
