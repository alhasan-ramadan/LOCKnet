using LOCKnet.Core.DataAbstractions;
using System.Security;

namespace LOCKnet.Core.Security;

/// <summary>
/// Verwaltet den Master-Key der Anwendung: Erstanlage, Verifikation und Passwortänderung.
/// Hält keinen Schlüsselmaterial selbst im RAM — der Session-Key liegt im <see cref="ISessionManager"/>.
/// </summary>
public interface IMasterKeyManager
{
	/// <summary>
	/// Gibt an, ob bereits ein Master-Key in der Datenbank angelegt wurde.
	/// </summary>
	bool IsInitialized { get; }

	/// <summary>
	/// Legt einen neuen Master-Key an (Ersteinrichtung).
	/// Leitet Salt und Hash ab und speichert sie über das Repository.
	/// </summary>
	/// <param name="password">Das neue Master-Passwort.</param>
	/// <exception cref="InvalidOperationException">Ein Master-Key ist bereits vorhanden.</exception>
	void Initialize(SecureString password);

	/// <summary>
	/// Prueft das angegebene Master-Passwort, migriert bei Bedarf Legacy-Credentialdaten
	/// in das aktuelle Secret-Format und gibt bei Erfolg den aktiven VaultKey zurueck.
	/// </summary>
	/// <param name="password">Das eingegebene Passwort.</param>
	/// <returns>
	/// Das Ergebnis des Unlocks inklusive VaultKey und Compaction-Status, oder <c>null</c> wenn das Passwort falsch ist.
	/// Der Aufrufer ist verantwortlich, den Schluessel nach Benutzung zu nullen.
	/// </returns>
	UnlockResult? Unlock(SecureString password);

	/// <summary>
	/// Liefert den aktuell persistierten Status der Storage-Kompaktierung fuer UI und Diagnostik.
	/// </summary>
	StorageCompactionInfo GetStorageCompactionInfo();

	/// <summary>
	/// Startet einen manuellen Kompaktierungsversuch fuer einen bereits entschluesselten Vault-Zustand.
	/// </summary>
	StorageCompactionInfo RetryPendingStorageCompaction();

	/// <summary>
	/// Aendert das Master-Passwort.
	/// Vor dem Rewrap wird sichergestellt, dass keine Legacy-Credentialdaten mehr aktiv sind.
	/// </summary>
	/// <param name="currentPassword">Das bisherige Passwort zur Verifikation.</param>
	/// <param name="newPassword">Das neue Passwort.</param>
	/// <exception cref="UnauthorizedAccessException">Das aktuelle Passwort ist falsch.</exception>
	/// <exception cref="InvalidOperationException">Kein Master-Key vorhanden.</exception>
	void ChangePassword(SecureString currentPassword, SecureString newPassword);
}
