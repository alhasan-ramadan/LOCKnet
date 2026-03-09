namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Verwaltet den einzigen Vault-Header der Anwendung.
/// Pro Datenbank existiert immer genau ein Eintrag (Id = 1).
/// </summary>
public interface IMasterKeyRepository
{
	/// <summary>
	/// Legt den Vault-Header an. Wirft eine <see cref="InvalidOperationException"/>
	/// wenn bereits ein Eintrag vorhanden ist.
	/// </summary>
	/// <exception cref="InvalidOperationException">Vault-Header existiert bereits.</exception>
	void Create(VaultHeader header);

	/// <summary>
	/// Liest den Vault-Header aus der Datenbank.
	/// </summary>
	/// <returns>Der Vault-Header, oder <c>null</c> wenn noch keiner angelegt wurde.</returns>
	VaultHeader? Get();

	/// <summary>
	/// Aktualisiert den bestehenden Vault-Header (z.B. nach Passwortaenderung).
	/// </summary>
	void Update(VaultHeader header);

	/// <summary>
	/// Loescht den Vault-Header. Macht die Datenbank damit dauerhaft unzugaenglich,
	/// sofern kein Backup existiert.
	/// </summary>
	void Delete();
}
