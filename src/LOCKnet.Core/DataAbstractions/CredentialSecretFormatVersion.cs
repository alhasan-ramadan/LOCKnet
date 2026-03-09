namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Versionskennungen fuer das Secret-Envelope-Format eines Credentials.
/// </summary>
public static class CredentialSecretFormatVersion
{
	/// <summary>Legacy-Format ohne expliziten Envelope-Header und ohne AAD-Bindung.</summary>
	public const int Legacy = 0;

	/// <summary>Erstes Envelope-Format mit Versionsbyte und AAD-Bindung inklusive CredentialType.</summary>
	public const int AesGcmV1 = 1;

	/// <summary>Aktuelles Envelope-Format ohne Klartext-Abhaengigkeit von CredentialType.</summary>
	public const int AesGcmV2 = 2;

	/// <summary>Aktuell unterstuetzte Secret-Formatversion fuer neue Schreibvorgaenge.</summary>
	public const int Current = AesGcmV2;
}
