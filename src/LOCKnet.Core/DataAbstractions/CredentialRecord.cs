namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Credential-Datensatz wie er aus der Datenbank kommt.
/// Das Passwort ist immer verschlüsselt — Entschlüsselung passiert im Core-Service.
/// </summary>
public class CredentialRecord
{
	public int Id { get; set; }
	public string Title { get; set; } = string.Empty;
	public string? Username { get; set; }
	public byte[] EncryptedPassword { get; set; } = [];
	public string? Url { get; set; }
	public string? Notes { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
}
