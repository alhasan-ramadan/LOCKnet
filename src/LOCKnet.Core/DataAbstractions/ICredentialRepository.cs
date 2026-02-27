namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Verwaltet verschlüsselte Zugangsdaten in der Datenbank.
/// Das Verschlüsseln und Entschlüsseln des Passworts obliegt dem Core-Service,
/// nicht diesem Repository.
/// </summary>
public interface ICredentialRepository
{
	/// <summary>
	/// Fügt ein neues Credential ein. <see cref="CredentialRecord.EncryptedPassword"/>
	/// muss bereits verschlüsselt sein.
	/// </summary>
	void Add(CredentialRecord credential);

	/// <summary>
	/// Gibt alle gespeicherten Credentials zurück.
	/// Die Passwörter sind verschlüsselt.
	/// </summary>
	IReadOnlyList<CredentialRecord> GetAll();

	/// <summary>
	/// Gibt ein einzelnes Credential anhand seiner ID zurück,
	/// oder <c>null</c> wenn es nicht existiert.
	/// </summary>
	CredentialRecord? GetById(int id);

	/// <summary>
	/// Aktualisiert ein bestehendes Credential vollständig.
	/// </summary>
	void Update(CredentialRecord credential);

	/// <summary>
	/// Löscht das Credential mit der angegebenen ID.
	/// </summary>
	void Remove(int id);
}
