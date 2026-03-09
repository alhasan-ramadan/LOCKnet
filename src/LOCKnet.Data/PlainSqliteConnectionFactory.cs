using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data;

/// <summary>
/// Zentraler Connection-Factory fuer die aktuelle Plain-SQLite-Implementierung.
/// </summary>
public sealed class PlainSqliteConnectionFactory : ISqliteConnectionFactory
{
	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="PlainSqliteConnectionFactory"/> fuer einen Datei-Pfad.
	/// </summary>
	/// <param name="databasePath">Pfad zur SQLite-Datenbankdatei.</param>
	public PlainSqliteConnectionFactory(string databasePath)
		: this(VaultStorageModeDetector.Detect(databasePath))
	{
	}

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="PlainSqliteConnectionFactory"/> fuer einen bestehenden Descriptor.
	/// </summary>
	/// <param name="storage">Der zu verwendende Storage-Descriptor.</param>
	public PlainSqliteConnectionFactory(VaultStorageDescriptor storage)
	{
		ArgumentNullException.ThrowIfNull(storage);

		if (storage.Mode != VaultStorageMode.PlainSqlite)
			throw new InvalidOperationException("PlainSqliteConnectionFactory unterstuetzt nur PlainSqlite-Storage.");

		Storage = storage;
	}

	/// <summary>
	/// Der verwendete Storage-Descriptor.
	/// </summary>
	public VaultStorageDescriptor Storage { get; }

	/// <summary>
	/// Oeffnet eine neue Plain-SQLite-Verbindung und wendet die aktuellen Hardening-PRAGMAs an.
	/// </summary>
	/// <returns>Eine geoeffnete <see cref="SqliteConnection"/>.</returns>
	public SqliteConnection OpenConnection()
	{
		var connection = new SqliteConnection(Storage.ConnectionString);
		connection.Open();
		RepositoryBase.ConfigureConnection(connection);
		return connection;
	}

	/// <summary>
	/// Erzeugt eine Plain-SQLite-Factory fuer Tests mit bereits vorhandenem Connection-String.
	/// </summary>
	/// <param name="connectionString">SQLite-Connection-String.</param>
	/// <returns>Eine Factory fuer die aktuelle Plain-SQLite-Implementierung.</returns>
	internal static PlainSqliteConnectionFactory FromConnectionString(string connectionString)
		=> new(VaultStorageModeDetector.DetectFromConnectionString(connectionString));
}
