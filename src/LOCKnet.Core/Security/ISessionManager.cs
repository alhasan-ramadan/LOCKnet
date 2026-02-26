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
    /// Gibt den aktuellen Session-Key zurück.
    /// </summary>
    /// <returns>Der 32-Byte AES-Schlüssel, oder <c>null</c> wenn die Sitzung gesperrt ist.</returns>
    /// <remarks>
    /// Das zurückgegebene Array darf NICHT gecacht werden — es wird bei <see cref="Lock"/> genullt.
    /// Immer direkt nach Benutzung wegwerfen.
    /// </remarks>
    byte[]? GetSessionKey();

    /// <summary>
    /// Entsperrt die Sitzung mit dem angegebenen Schlüssel.
    /// Wird typischerweise vom <see cref="IMasterKeyManager.Unlock"/> aufgerufen.
    /// </summary>
    /// <param name="sessionKey">Der 32-Byte AES-Schlüssel (Ownership geht über — nicht weiter verwenden).</param>
    void Open(byte[] sessionKey);

    /// <summary>
    /// Sperrt die Sitzung: nullt den Session-Key im RAM und löst <see cref="Locked"/> aus.
    /// </summary>
    void Lock();
}
