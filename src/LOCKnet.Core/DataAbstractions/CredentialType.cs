namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Legt den Typ eines Credential-Eintrags fest.
/// </summary>
public enum CredentialType
{
	/// <summary>Klassisches Benutzername/Passwort-Credential. Standard-Wert für Abwärtskompatibilität.</summary>
	Password = 0,

	/// <summary>API-Schlüssel, Token oder Secret — kein Benutzername erforderlich.</summary>
	ApiKey = 1,
}
