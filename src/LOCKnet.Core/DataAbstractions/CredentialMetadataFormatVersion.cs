namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Versionskennungen fuer das Metadaten-Envelope-Format eines Credentials.
/// </summary>
public static class CredentialMetadataFormatVersion
{
	/// <summary>Legacy-Zustand: Metadaten liegen noch in Klartextspalten.</summary>
	public const int Legacy = 0;

	/// <summary>Versioniertes AES-GCM-Metadaten-Envelope mit AAD-Bindung.</summary>
	public const int AesGcmV1 = 1;

	/// <summary>Aktuell unterstuetzte Metadaten-Formatversion.</summary>
	public const int Current = AesGcmV1;
}
