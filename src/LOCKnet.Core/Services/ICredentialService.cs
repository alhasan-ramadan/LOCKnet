using LOCKnet.Core.DataAbstractions;
using System.Security;

namespace LOCKnet.Core.Services;

/// <summary>
/// Verwaltet Zugangsdaten: CRUD-Operationen mit transparenter Ver- und Entschlüsselung.
/// Das Repository speichert verschlüsselte Bytes — dieser Service kümmert sich um die Konvertierung.
/// </summary>
public interface ICredentialService
{
	/// <summary>
	/// Fügt ein neues Credential hinzu. Das Passwort wird vor dem Speichern verschlüsselt.
	/// </summary>
	/// <param name="title">Bezeichnung des Eintrags (z.B. "GitHub").</param>
	/// <param name="username">Optionaler Benutzername.</param>
	/// <param name="password">Das Klartextpasswort. Wird intern verschlüsselt, nie gespeichert.</param>
	/// <param name="url">Optionale URL.</param>
	/// <param name="notes">Optionale Notizen.</param>
	/// <param name="iconKey">Optionaler Material-Icon-Schluessel.</param>
	/// <exception cref="InvalidOperationException">Sitzung ist gesperrt.</exception>
	void Add(string title, string? username, SecureString password, string? url = null, string? notes = null, string? iconKey = null);

	/// <summary>
	/// Gibt alle Credentials zurück. Passwörter bleiben verschlüsselt.
	/// Zum Anzeigen im UI — das Klartext-Passwort wird nur per <see cref="GetPassword"/> abgerufen.
	/// </summary>
	/// <returns>Liste aller Credentials (ohne entschlüsselte Passwörter).</returns>
	/// <exception cref="InvalidOperationException">Sitzung ist gesperrt.</exception>
	IReadOnlyList<CredentialRecord> GetAll();

	/// <summary>
	/// Entschlüsselt das Passwort eines einzelnen Credentials und gibt es als <see cref="SecureString"/> zurück.
	/// </summary>
	/// <param name="id">Die ID des Credentials.</param>
	/// <returns>Das entschlüsselte Passwort, oder <c>null</c> wenn das Credential nicht existiert.</returns>
	/// <exception cref="InvalidOperationException">Sitzung ist gesperrt.</exception>
	SecureString? GetPassword(int id);

	/// <summary>
	/// Aktualisiert ein bestehendes Credential. Wird <paramref name="newPassword"/> übergeben,
	/// wird es verschlüsselt gespeichert; andernfalls bleibt das bisherige Passwort erhalten.
	/// </summary>
	/// <param name="id">Die ID des zu aktualisierenden Credentials.</param>
	/// <param name="title">Neue Bezeichnung.</param>
	/// <param name="username">Neuer Benutzername (oder <c>null</c>).</param>
	/// <param name="newPassword">Neues Passwort, oder <c>null</c> um das bisherige zu behalten.</param>
	/// <param name="url">Neue URL (oder <c>null</c>).</param>
	/// <param name="notes">Neue Notizen (oder <c>null</c>).</param>
	/// <param name="iconKey">Neuer Material-Icon-Schluessel (oder <c>null</c>).</param>
	/// <exception cref="InvalidOperationException">Sitzung ist gesperrt oder Credential nicht gefunden.</exception>
	void Update(int id, string title, string? username, SecureString? newPassword, string? url = null, string? notes = null, string? iconKey = null);

	/// <summary>
	/// Löscht das Credential mit der angegebenen ID.
	/// </summary>
	/// <param name="id">Die ID des zu löschenden Credentials.</param>
	/// <exception cref="InvalidOperationException">Sitzung ist gesperrt.</exception>
	void Remove(int id);
}
