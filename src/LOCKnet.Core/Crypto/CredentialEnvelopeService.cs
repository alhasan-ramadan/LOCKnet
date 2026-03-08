using LOCKnet.Core.DataAbstractions;
namespace LOCKnet.Core.Crypto;

/// <summary>
/// Versionierter Envelope fuer Credential-Secrets.
/// V1 speichert <c>[Version][Nonce][Tag][Ciphertext]</c> und bindet den Ciphertext per AAD an stabile Metadaten.
/// </summary>
public sealed class CredentialEnvelopeService : ICredentialEnvelopeService
{
	private const int EnvelopeHeaderBytes = 1;
	private const byte SecretFieldDiscriminator = 1;
	private readonly IEncryptionService _encryption;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="CredentialEnvelopeService"/>.
	/// </summary>
	public CredentialEnvelopeService(IEncryptionService encryption)
	{
		ArgumentNullException.ThrowIfNull(encryption);
		_encryption = encryption;
	}

	/// <inheritdoc/>
	public int CurrentVersion => CredentialSecretFormatVersion.Current;

	/// <inheritdoc/>
	public byte[] Encrypt(byte[] plaintext, byte[] key, CredentialRecord credential, int vaultFormatVersion)
	{
		ArgumentNullException.ThrowIfNull(plaintext);
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(credential);
		ValidateCredentialContext(credential);

		var aad = BuildAssociatedData(vaultFormatVersion, credential.CredentialUuid, credential.CredentialType);
		var packet = _encryption.Encrypt(plaintext, key, aad);
		var envelope = new byte[EnvelopeHeaderBytes + packet.Length];
		envelope[0] = (byte)CurrentVersion;
		packet.CopyTo(envelope, EnvelopeHeaderBytes);
		return envelope;
	}

	/// <inheritdoc/>
	public byte[] Decrypt(CredentialRecord credential, byte[] key, int vaultFormatVersion)
	{
		ArgumentNullException.ThrowIfNull(credential);
		ArgumentNullException.ThrowIfNull(key);

		return credential.SecretFormatVersion switch
		{
			CredentialSecretFormatVersion.Legacy => _encryption.Decrypt(credential.EncryptedPassword, key),
			CredentialSecretFormatVersion.AesGcmV1 => DecryptV1(credential, key, vaultFormatVersion),
			_ => throw new InvalidOperationException($"Nicht unterstuetzte Secret-Formatversion: {credential.SecretFormatVersion}"),
		};
	}

	private byte[] DecryptV1(CredentialRecord credential, byte[] key, int vaultFormatVersion)
	{
		ValidateCredentialContext(credential);
		if (credential.EncryptedPassword.Length <= EnvelopeHeaderBytes)
			throw new InvalidOperationException("Credential-Envelope ist zu kurz.");

		var versionByte = credential.EncryptedPassword[0];
		if (versionByte != CredentialSecretFormatVersion.AesGcmV1)
			throw new InvalidOperationException($"Envelope-Version {versionByte} passt nicht zum gespeicherten Secret-Format.");

		var packet = credential.EncryptedPassword[EnvelopeHeaderBytes..];
		var aad = BuildAssociatedData(vaultFormatVersion, credential.CredentialUuid, credential.CredentialType);
		return _encryption.Decrypt(packet, key, aad);
	}

	private static byte[] BuildAssociatedData(int vaultFormatVersion, string credentialUuid, CredentialType credentialType)
	{
		if (vaultFormatVersion != VaultHeaderFormatVersion.Current)
			throw new InvalidOperationException($"Secret-Envelope erwartet VaultHeader-Format {VaultHeaderFormatVersion.Current}, erhielt aber {vaultFormatVersion}.");

		var aad = new byte[22];
		BitConverter.GetBytes(vaultFormatVersion).CopyTo(aad, 0);
		Guid.ParseExact(credentialUuid, "N").TryWriteBytes(aad.AsSpan(4, 16));
		aad[20] = SecretFieldDiscriminator;
		aad[21] = (byte)credentialType;
		return aad;
	}

	private static void ValidateCredentialContext(CredentialRecord credential)
	{
		if (!Guid.TryParseExact(credential.CredentialUuid, "N", out _))
			throw new InvalidOperationException("CredentialUuid muss eine GUID im N-Format sein.");
	}
}
