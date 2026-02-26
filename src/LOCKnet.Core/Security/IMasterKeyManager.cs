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
    /// Prüft, ob das angegebene Passwort mit dem gespeicherten Hash übereinstimmt,
    /// und gibt bei Erfolg den abgeleiteten AES-256-Schlüssel zurück.
    /// </summary>
    /// <param name="password">Das eingegebene Passwort.</param>
    /// <returns>
    /// Der 32-Byte AES-Schlüssel bei korrektem Passwort, oder <c>null</c> wenn das Passwort falsch ist.
    /// Der Aufrufer ist verantwortlich, den Schlüssel nach Benutzung zu nullen.
    /// </returns>
    byte[]? Unlock(SecureString password);

    /// <summary>
    /// Ändert das Master-Passwort. Prüft zuerst das alte Passwort.
    /// </summary>
    /// <param name="currentPassword">Das bisherige Passwort zur Verifikation.</param>
    /// <param name="newPassword">Das neue Passwort.</param>
    /// <exception cref="UnauthorizedAccessException">Das aktuelle Passwort ist falsch.</exception>
    /// <exception cref="InvalidOperationException">Kein Master-Key vorhanden.</exception>
    void ChangePassword(SecureString currentPassword, SecureString newPassword);
}
