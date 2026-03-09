using Microsoft.Data.Sqlite;

namespace LOCKnet.Data;

/// <summary>
/// Ermittelt den aktuell verwendeten Storage-Modus fuer eine Vault.
/// </summary>
public static class VaultStorageModeDetector
{
	/// <summary>
	/// Erzeugt den aktuellen Storage-Descriptor fuer eine dateibasierte Vault.
	/// </summary>
	/// <param name="databasePath">Pfad zur Vault-Datei.</param>
	/// <returns>Der Descriptor fuer die aktuelle Plain-SQLite-Implementierung.</returns>
	public static VaultStorageDescriptor Detect(string databasePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

		var fullPath = Path.GetFullPath(databasePath);
		var builder = new SqliteConnectionStringBuilder
		{
			DataSource = fullPath,
		};

		return new VaultStorageDescriptor(
			VaultStorageMode.PlainSqlite,
			builder.ToString(),
			fullPath,
			requiresKeyAtOpen: false);
	}

	/// <summary>
	/// Erzeugt den aktuellen Storage-Descriptor fuer einen bereits vorliegenden Connection-String.
	/// </summary>
	/// <param name="connectionString">SQLite-Connection-String.</param>
	/// <returns>Der Descriptor fuer die aktuelle Plain-SQLite-Implementierung.</returns>
	internal static VaultStorageDescriptor DetectFromConnectionString(string connectionString)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

		return new VaultStorageDescriptor(
			VaultStorageMode.PlainSqlite,
			connectionString,
			StorageRewriteArtifacts.TryResolveDatabasePath(connectionString),
			requiresKeyAtOpen: false);
	}
}
