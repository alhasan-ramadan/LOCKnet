using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.Data.Repositories;

internal static class StoredCredentialGuard
{
	public static void ValidateForPersistence(CredentialRecord credential)
	{
		ArgumentNullException.ThrowIfNull(credential);

		if (credential.MetadataFormatVersion != CredentialMetadataFormatVersion.Current)
			throw new InvalidOperationException("Direkte Persistenz akzeptiert nur aktuelle verschluesselte Metadatenformate.");

		if (credential.SecretFormatVersion == CredentialSecretFormatVersion.Current && credential.EncryptedPassword.Length == 0)
			throw new InvalidOperationException("Aktuelle Secret-Records muessen verschluesselte Secret-Daten enthalten.");

		if (!Guid.TryParseExact(credential.CredentialUuid, "N", out _))
			throw new InvalidOperationException("Aktuelle Metadata-Records muessen eine stabile CredentialUuid im N-Format enthalten.");

		if (credential.EncryptedMetadata.Length == 0)
			throw new InvalidOperationException("Aktuelle Metadata-Records muessen verschluesselte Metadaten enthalten.");

		if (HasPlaintextMetadataResidue(credential))
			throw new InvalidOperationException("Aktuelle Metadata-Records duerfen keine Klartext-Metadaten persistieren.");
	}

	private static bool HasPlaintextMetadataResidue(CredentialRecord credential)
		=> !string.IsNullOrEmpty(credential.Title) ||
			!string.IsNullOrEmpty(credential.Username) ||
			!string.IsNullOrEmpty(credential.Url) ||
			!string.IsNullOrEmpty(credential.Notes) ||
			!string.IsNullOrEmpty(credential.IconKey) ||
			credential.CredentialType != CredentialType.Password;
}
