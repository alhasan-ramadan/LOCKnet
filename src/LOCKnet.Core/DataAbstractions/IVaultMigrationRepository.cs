namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Fuehrt Header- und Credential-Migrationsupdates atomar in einer Transaktion aus.
/// </summary>
public interface IVaultMigrationRepository
{
	/// <summary>
	/// Liest alle Credentials fuer Migrationsentscheidungen.
	/// </summary>
	IReadOnlyList<CredentialRecord> GetAllCredentials();

	/// <summary>
	/// Persistiert einen neuen Vault-Header und eine Menge migrierter Credentials atomar.
	/// </summary>
	/// <param name="header">Der vollstaendig validierte Ziel-Header.</param>
	/// <param name="credentials">Die zu aktualisierenden Credential-Datensaetze.</param>
	void ApplyMigration(VaultHeader header, IReadOnlyList<CredentialRecord> credentials);

	/// <summary>
	/// Prueft, ob fuer die aktuelle Vault-Datei noch Rewrite-Artefakte einer vorherigen Bereinigung existieren.
	/// </summary>
	bool HasPendingStorageArtifacts();

	/// <summary>
	/// Fuehrt eine SQLite-Kompaktierung auf dem Vault durch und liefert ein strukturiertes Ergebnis.
	/// </summary>
	StorageCompactionInfo CompactStorage();
}
