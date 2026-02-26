namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Verwaltet den einzigen Master-Key der Anwendung.
/// Pro Datenbank existiert immer genau ein Eintrag (Id = 1).
/// </summary>
public interface IMasterKeyRepository
{
    /// <summary>
    /// Legt den Master-Key an. Wirft eine <see cref="InvalidOperationException"/>
    /// wenn bereits ein Eintrag vorhanden ist.
    /// </summary>
    /// <exception cref="InvalidOperationException">Master-Key existiert bereits.</exception>
    void Create(MasterKeyRecord key);

    /// <summary>
    /// Liest den Master-Key aus der Datenbank.
    /// </summary>
    /// <returns>Der Master-Key, oder <c>null</c> wenn noch keiner angelegt wurde.</returns>
    MasterKeyRecord? Get();

    /// <summary>
    /// Aktualisiert den bestehenden Master-Key (z.B. nach Passwortänderung).
    /// </summary>
    void Update(MasterKeyRecord key);

    /// <summary>
    /// Löscht den Master-Key. Macht die Datenbank damit dauerhaft unzugänglich,
    /// sofern kein Backup existiert.
    /// </summary>
    void Delete();
}
