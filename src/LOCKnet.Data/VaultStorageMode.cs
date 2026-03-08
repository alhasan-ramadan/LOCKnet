namespace LOCKnet.Data;

/// <summary>
/// Beschreibt, wie die Vault-Datei auf Storage-Ebene geoeffnet wird.
/// </summary>
public enum VaultStorageMode
{
	/// <summary>
	/// Normale SQLite-Datei ohne datenbankweite Dateiverschluesselung.
	/// </summary>
	PlainSqlite = 0,

	/// <summary>
	/// Platzhalter fuer eine spaetere SQLite-Variante, die einen Schluessel bereits beim Oeffnen benoetigt.
	/// </summary>
	EncryptedSqlite = 1,
}
