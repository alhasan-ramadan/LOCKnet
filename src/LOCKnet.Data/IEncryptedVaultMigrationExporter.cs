using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.Data;

/// <summary>
/// Vertrag fuer eine spaetere Ziel-Engine, die eine Plain-Vault in eine encrypted SQLite-Vault exportieren kann.
/// </summary>
public interface IEncryptedVaultMigrationExporter
{
	/// <summary>
	/// Das Ziel-Storage-Format dieser Export-Implementierung.
	/// </summary>
	VaultStorageMigrationTargetMode TargetMode { get; }

	/// <summary>
	/// Erstellt aus einer Plain-Vault eine Ziel-Vault am angegebenen Temp-Pfad.
	/// </summary>
	/// <param name="sourceConnectionString">Connection-String der bereits oeffenbaren Plain-Vault.</param>
	/// <param name="destinationPath">Temp-Pfad fuer die spaetere encrypted Ziel-Vault.</param>
	void ExportPlaintextVault(string sourceConnectionString, string destinationPath);

	/// <summary>
	/// Validiert die exportierte Ziel-Vault vor finalem Austausch mit der alten Hauptdatei.
	/// </summary>
	/// <param name="destinationPath">Pfad zur exportierten Ziel-Vault.</param>
	void ValidateExportedVault(string destinationPath);

	/// <summary>
	/// Persistiert den migrierten Header in die Ziel-Vault, bevor oder nachdem diese zur Hauptdatei geworden ist.
	/// </summary>
	/// <param name="databasePath">Pfad zur Ziel-Vault.</param>
	/// <param name="header">Der zu persistierende Headerzustand.</param>
	void PersistMigratedHeader(string databasePath, VaultHeader header);
}
