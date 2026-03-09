namespace LOCKnet.Data;

/// <summary>
/// Beschreibt die aktuell erkannte Storage-Konfiguration fuer eine Vault-Datei.
/// </summary>
public sealed class VaultStorageDescriptor
{
	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="VaultStorageDescriptor"/>.
	/// </summary>
	/// <param name="mode">Der erkannte Storage-Modus.</param>
	/// <param name="connectionString">Der aktuell verwendbare SQLite-Connection-String.</param>
	/// <param name="databasePath">Der aufgeloeste Dateipfad oder <see langword="null"/> bei nicht-dateibasierten Verbindungen.</param>
	/// <param name="requiresKeyAtOpen">Gibt an, ob die Datenbank spaeter bereits zum Oeffnen einen Schluessel benoetigt.</param>
	public VaultStorageDescriptor(VaultStorageMode mode, string connectionString, string? databasePath, bool requiresKeyAtOpen)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

		Mode = mode;
		ConnectionString = connectionString;
		DatabasePath = databasePath;
		RequiresKeyAtOpen = requiresKeyAtOpen;
	}

	/// <summary>
	/// Der erkannte Storage-Modus.
	/// </summary>
	public VaultStorageMode Mode { get; }

	/// <summary>
	/// Der aktuell verwendete SQLite-Connection-String.
	/// </summary>
	public string ConnectionString { get; }

	/// <summary>
	/// Der aufgeloeste Dateipfad oder <see langword="null"/> bei nicht-dateibasierten Verbindungen.
	/// </summary>
	public string? DatabasePath { get; }

	/// <summary>
	/// Gibt an, ob diese Storage-Variante bereits beim Oeffnen einen Schluessel benoetigt.
	/// </summary>
	public bool RequiresKeyAtOpen { get; }
}
