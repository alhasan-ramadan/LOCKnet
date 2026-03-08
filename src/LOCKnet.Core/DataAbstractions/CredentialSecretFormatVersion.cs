namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Versionskennungen fuer das Secret-Envelope-Format eines Credentials.
/// </summary>
public static class CredentialSecretFormatVersion
{
	/// <summary>Legacy-Format ohne expliziten Envelope-Header und ohne AAD-Bindung.</summary>
	public const int Legacy = 0;

	/// <summary>Aktuelles Envelope-Format mit Versionsbyte und AAD-Bindung.</summary>
	public const int AesGcmV1 = 1;

	/// <summary>Aktuell unterstuetzte Secret-Formatversion fuer neue Schreibvorgaenge.</summary>
	public const int Current = AesGcmV1;
}
