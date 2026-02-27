namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Master-Key-Eintrag wie er in der Datenbank gespeichert ist.
/// Exakt ein Eintrag pro Datenbank (Id = 1).
/// </summary>
public class MasterKeyRecord
{
	public byte[] PasswordHash { get; set; } = [];
	public byte[] Salt { get; set; } = [];
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}
