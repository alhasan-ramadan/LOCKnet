namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Master-Key-Eintrag wie er in der Datenbank gespeichert ist.
/// Exakt ein Eintrag pro Datenbank (Id = 1).
/// </summary>
public class MasterKeyRecord
{
	/// <summary>PBKDF2-Hash des Master-Passworts (zum Verifizieren beim Login).</summary>
	public byte[] PasswordHash { get; set; } = [];
	/// <summary>Kryptografischer Salt für die Schlüsselableitung und den Passwort-Hash.</summary>
	public byte[] Salt { get; set; } = [];
	/// <summary>UTC-Zeitstempel der Erstellung (von SQLite gesetzt).</summary>
	public DateTime CreatedAt { get; set; }
	/// <summary>UTC-Zeitstempel der letzten Änderung (von SQLite gesetzt).</summary>
	public DateTime UpdatedAt { get; set; }
}
