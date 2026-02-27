namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Credential-Datensatz wie er aus der Datenbank kommt.
/// Das Passwort ist immer verschlüsselt — Entschlüsselung passiert im Core-Service.
/// </summary>
public class CredentialRecord
{
	/// <summary>Eindeutige Datenbank-ID des Eintrags.</summary>
	public int Id { get; set; }
	/// <summary>Bezeichnung des Eintrags (z.B. "GitHub", "E-Mail").</summary>
	public string Title { get; set; } = string.Empty;
	/// <summary>Optionaler Benutzername oder E-Mail-Adresse.</summary>
	public string? Username { get; set; }
	/// <summary>AES-256-GCM-verschlüsseltes Passwort als Byte-Array. Niemals Klartext.</summary>
	public byte[] EncryptedPassword { get; set; } = [];
	/// <summary>Optionale URL des zugehörigen Dienstes.</summary>
	public string? Url { get; set; }
	/// <summary>Optionale Freitextnotizen zum Eintrag.</summary>
	public string? Notes { get; set; }
	/// <summary>UTC-Zeitstempel der Erstellung (von SQLite gesetzt).</summary>
	public DateTime CreatedAt { get; set; }
	/// <summary>UTC-Zeitstempel der letzten Änderung (von SQLite gesetzt).</summary>
	public DateTime UpdatedAt { get; set; }
	/// <summary>Optionaler Icon-Schluessel fuer Material-Icon-Darstellung.</summary>
	public string? IconKey { get; set; }
}
