using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.Core.Crypto;

/// <summary>
/// Erstellt und validiert versionierte Ciphertext-Envelopes fuer Credential-Secrets.
/// </summary>
public interface ICredentialEnvelopeService
{
	/// <summary>Aktuelle Secret-Formatversion fuer neue Writes.</summary>
	int CurrentVersion { get; }

	/// <summary>
	/// Verschluesselt ein Credential-Secret in ein versioniertes Envelope-Format.
	/// </summary>
	byte[] Encrypt(byte[] plaintext, byte[] key, CredentialRecord credential, int vaultFormatVersion);

	/// <summary>
	/// Entschluesselt ein Credential-Secret und validiert bei neuen Formaten die AAD-Bindung.
	/// </summary>
	byte[] Decrypt(CredentialRecord credential, byte[] key, int vaultFormatVersion);
}
