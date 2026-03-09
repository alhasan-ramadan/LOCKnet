namespace LOCKnet.Core.Security;

/// <summary>
/// Verwaltet den Auth-Status der laufenden Sitzung.
/// Hält den Session-Key (AES-256) im RAM — wird bei Lock nullt.
/// Ist das zentrale Koordinationsobjekt zwischen MasterKeyManager und ActivityMonitor.
/// </summary>
public interface ISessionManager
{
	/// <summary>
	/// Gibt an, ob die Sitzung aktuell entsperrt ist (Session-Key im RAM vorhanden).
	/// </summary>
	bool IsUnlocked { get; }

	/// <summary>
	/// Wird ausgelöst, wenn die Sitzung gesperrt wird (durch Lock() oder Auto-Lock).
	/// </summary>
	event EventHandler? Locked;

	/// <summary>
	/// Gibt eine Kopie des aktuellen Session-Keys zurueck.
	/// </summary>
	/// <returns>Eine 32-Byte-Kopie des AES-Schluessels, oder <c>null</c> wenn die Sitzung gesperrt ist.</returns>
	/// <remarks>
	/// Das zurueckgegebene Array ist nie der interne Puffer.
	/// Aufrufer muessen die Kopie nach Benutzung selbst nullen.
	/// </remarks>
	byte[]? GetSessionKey();

	/// <summary>
	/// Entsperrt die Sitzung mit dem angegebenen Schlüssel.
	/// Wird typischerweise vom <see cref="IMasterKeyManager.Unlock"/> aufgerufen.
	/// </summary>
	/// <param name="sessionKey">Der 32-Byte AES-Schluessel. Der uebergebene Puffer wird nach dem Kopieren genullt.</param>
	void Open(byte[] sessionKey);

	/// <summary>
	/// Sperrt die Sitzung: nullt den Session-Key im RAM und löst <see cref="Locked"/> aus.
	/// </summary>
	void Lock();
}
