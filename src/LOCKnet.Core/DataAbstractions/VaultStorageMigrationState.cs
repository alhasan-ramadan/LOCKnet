namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Beschreibt den persistierten Zustand einer Plain-zu-encrypted-Storage-Migration.
/// </summary>
public enum VaultStorageMigrationState
{
	/// <summary>Keine Storage-Migration aktiv.</summary>
	None = 0,

	/// <summary>Die Export-/Konvertierungsphase laeuft oder muss nach Unterbrechung fortgesetzt werden.</summary>
	InProgress = 1,

	/// <summary>Die Ziel-Vault wurde erstellt, aber alte Plain-Artefakte muessen noch final bereinigt werden.</summary>
	FinalizationPending = 2,

	/// <summary>Die letzte Migration ist fehlgeschlagen und erfordert explizite Wiederaufnahme oder Diagnose.</summary>
	Failed = 3,
}
