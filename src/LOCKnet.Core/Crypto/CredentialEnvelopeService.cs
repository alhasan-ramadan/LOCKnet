using LOCKnet.Core.DataAbstractions;
using System.Security.Cryptography;
using System.Text.Json;

namespace LOCKnet.Core.Crypto;

/// <summary>
/// Versionierter Envelope fuer Credential-Secrets.
/// V1 speichert <c>[Version][Nonce][Tag][Ciphertext]</c> und bindet den Ciphertext per AAD an stabile Metadaten.
/// </summary>
public sealed class CredentialEnvelopeService : ICredentialEnvelopeService
{
	private const int EnvelopeHeaderBytes = 1;
	private const byte SecretFieldDiscriminator = 1;
	private const byte MetadataFieldDiscriminator = 2;
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
	public int CurrentMetadataVersion => CredentialMetadataFormatVersion.Current;

	/// <inheritdoc/>
	public byte[] Encrypt(byte[] plaintext, byte[] key, CredentialRecord credential, int vaultFormatVersion)
	{
		ArgumentNullException.ThrowIfNull(plaintext);
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(credential);
		ValidateCredentialContext(credential);

		var aad = BuildAssociatedDataV2(vaultFormatVersion, credential.CredentialUuid, SecretFieldDiscriminator);
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
			CredentialSecretFormatVersion.AesGcmV2 => DecryptV2(credential, key, vaultFormatVersion),
			_ => throw new InvalidOperationException($"Nicht unterstuetzte Secret-Formatversion: {credential.SecretFormatVersion}"),
		};
	}

	/// <inheritdoc/>
	public byte[] EncryptMetadata(CredentialRecord credential, byte[] key, int vaultFormatVersion)
	{
		ArgumentNullException.ThrowIfNull(credential);
		ArgumentNullException.ThrowIfNull(key);
		ValidateCredentialContext(credential);

		var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(new CredentialMetadataPayload
		{
			Title = credential.Title,
			Username = credential.Username,
			Url = credential.Url,
			Notes = credential.Notes,
			IconKey = credential.IconKey,
			CredentialType = credential.CredentialType,
		});

		try
		{
			var aad = BuildAssociatedDataV2(vaultFormatVersion, credential.CredentialUuid, MetadataFieldDiscriminator);
			var packet = _encryption.Encrypt(metadataBytes, key, aad);
			var envelope = new byte[EnvelopeHeaderBytes + packet.Length];
			envelope[0] = (byte)CurrentMetadataVersion;
			packet.CopyTo(envelope, EnvelopeHeaderBytes);
			return envelope;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(metadataBytes);
		}
	}

	/// <inheritdoc/>
	public CredentialRecord DecryptMetadata(CredentialRecord credential, byte[] key, int vaultFormatVersion)
	{
		ArgumentNullException.ThrowIfNull(credential);
		ArgumentNullException.ThrowIfNull(key);

		return credential.MetadataFormatVersion switch
		{
			CredentialMetadataFormatVersion.Legacy => CloneRecord(credential),
			CredentialMetadataFormatVersion.AesGcmV1 => DecryptMetadataV1(credential, key, vaultFormatVersion),
			_ => throw new InvalidOperationException($"Nicht unterstuetzte Metadaten-Formatversion: {credential.MetadataFormatVersion}"),
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
		var aad = BuildAssociatedDataV1(vaultFormatVersion, credential.CredentialUuid, credential.CredentialType, SecretFieldDiscriminator);
		return _encryption.Decrypt(packet, key, aad);
	}

	private byte[] DecryptV2(CredentialRecord credential, byte[] key, int vaultFormatVersion)
	{
		ValidateCredentialContext(credential);
		if (credential.EncryptedPassword.Length <= EnvelopeHeaderBytes)
			throw new InvalidOperationException("Credential-Envelope ist zu kurz.");

		var versionByte = credential.EncryptedPassword[0];
		if (versionByte != CredentialSecretFormatVersion.AesGcmV2)
			throw new InvalidOperationException($"Envelope-Version {versionByte} passt nicht zum gespeicherten Secret-Format.");

		var packet = credential.EncryptedPassword[EnvelopeHeaderBytes..];
		var aad = BuildAssociatedDataV2(vaultFormatVersion, credential.CredentialUuid, SecretFieldDiscriminator);
		return _encryption.Decrypt(packet, key, aad);
	}

	private CredentialRecord DecryptMetadataV1(CredentialRecord credential, byte[] key, int vaultFormatVersion)
	{
		ValidateCredentialContext(credential);
		if (credential.EncryptedMetadata.Length <= EnvelopeHeaderBytes)
			throw new InvalidOperationException("Metadaten-Envelope ist zu kurz.");

		var versionByte = credential.EncryptedMetadata[0];
		if (versionByte != CredentialMetadataFormatVersion.AesGcmV1)
			throw new InvalidOperationException($"Envelope-Version {versionByte} passt nicht zum gespeicherten Metadaten-Format.");

		var packet = credential.EncryptedMetadata[EnvelopeHeaderBytes..];
		var aad = BuildAssociatedDataV2(vaultFormatVersion, credential.CredentialUuid, MetadataFieldDiscriminator);
		var plaintext = _encryption.Decrypt(packet, key, aad);
		try
		{
			var payload = JsonSerializer.Deserialize<CredentialMetadataPayload>(plaintext)
				?? throw new InvalidOperationException("Metadaten konnten nicht deserialisiert werden.");

			var clone = CloneRecord(credential);
			clone.Title = payload.Title;
			clone.Username = payload.Username;
			clone.Url = payload.Url;
			clone.Notes = payload.Notes;
			clone.IconKey = payload.IconKey;
			clone.CredentialType = payload.CredentialType;
			return clone;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(plaintext);
		}
	}

	private static byte[] BuildAssociatedDataV1(int vaultFormatVersion, string credentialUuid, CredentialType credentialType, byte fieldDiscriminator)
	{
		if (vaultFormatVersion != VaultHeaderFormatVersion.Current)
			throw new InvalidOperationException($"Envelope erwartet VaultHeader-Format {VaultHeaderFormatVersion.Current}, erhielt aber {vaultFormatVersion}.");

		var aad = new byte[22];
		BitConverter.GetBytes(vaultFormatVersion).CopyTo(aad, 0);
		Guid.ParseExact(credentialUuid, "N").TryWriteBytes(aad.AsSpan(4, 16));
		aad[20] = fieldDiscriminator;
		aad[21] = (byte)credentialType;
		return aad;
	}

	private static byte[] BuildAssociatedDataV2(int vaultFormatVersion, string credentialUuid, byte fieldDiscriminator)
	{
		if (vaultFormatVersion != VaultHeaderFormatVersion.Current)
			throw new InvalidOperationException($"Envelope erwartet VaultHeader-Format {VaultHeaderFormatVersion.Current}, erhielt aber {vaultFormatVersion}.");

		var aad = new byte[21];
		BitConverter.GetBytes(vaultFormatVersion).CopyTo(aad, 0);
		Guid.ParseExact(credentialUuid, "N").TryWriteBytes(aad.AsSpan(4, 16));
		aad[20] = fieldDiscriminator;
		return aad;
	}

	private static void ValidateCredentialContext(CredentialRecord credential)
	{
		if (!Guid.TryParseExact(credential.CredentialUuid, "N", out _))
			throw new InvalidOperationException("CredentialUuid muss eine GUID im N-Format sein.");
	}

	private static CredentialRecord CloneRecord(CredentialRecord credential) => new()
	{
		Id = credential.Id,
		Title = credential.Title,
		Username = credential.Username,
		EncryptedPassword = credential.EncryptedPassword.ToArray(),
		EncryptedMetadata = credential.EncryptedMetadata.ToArray(),
		CredentialUuid = credential.CredentialUuid,
		SecretFormatVersion = credential.SecretFormatVersion,
		MetadataFormatVersion = credential.MetadataFormatVersion,
		Url = credential.Url,
		Notes = credential.Notes,
		CreatedAt = credential.CreatedAt,
		UpdatedAt = credential.UpdatedAt,
		IconKey = credential.IconKey,
		CredentialType = credential.CredentialType,
	};

	private sealed class CredentialMetadataPayload
	{
		public string Title { get; set; } = string.Empty;
		public string? Username { get; set; }
		public string? Url { get; set; }
		public string? Notes { get; set; }
		public string? IconKey { get; set; }
		public CredentialType CredentialType { get; set; }
	}
}
