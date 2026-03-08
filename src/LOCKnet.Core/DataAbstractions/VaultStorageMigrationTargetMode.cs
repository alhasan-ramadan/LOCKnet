namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Beschreibt das Ziel einer spaeteren Storage-Migration.
/// </summary>
public enum VaultStorageMigrationTargetMode
{
	/// <summary>Keine Storage-Migration aktiv.</summary>
	None = 0,

	/// <summary>Die Plain-SQLite-Vault soll in eine spaetere encrypted SQLite-Variante exportiert werden.</summary>
	EncryptedSqlite = 1,
}
