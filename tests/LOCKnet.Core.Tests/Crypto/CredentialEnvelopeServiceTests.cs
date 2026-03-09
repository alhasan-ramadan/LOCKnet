using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LOCKnet.Core.Tests.Crypto;

public sealed class CredentialEnvelopeServiceTests
{
	private readonly AesGcmEncryptionService _encryption = new();

	private CredentialEnvelopeService CreateSut() => new(_encryption);

	private static byte[] MakeKey() => RandomNumberGenerator.GetBytes(32);

	private static CredentialRecord CreateCredential() => new()
	{
		Id = 42,
		Title = "Title",
		Username = "alice",
		Url = "https://example.test",
		Notes = "note",
		IconKey = "Key",
		CredentialType = CredentialType.ApiKey,
		CredentialUuid = Guid.NewGuid().ToString("N"),
		SecretFormatVersion = CredentialSecretFormatVersion.Current,
		MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
		CreatedAt = DateTime.UtcNow,
		UpdatedAt = DateTime.UtcNow,
	};

	[Fact]
	public void EncryptAndDecrypt_CurrentSecretFormat_RoundTrips()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		var plaintext = Encoding.UTF8.GetBytes("secret-value");

		credential.EncryptedPassword = sut.Encrypt(plaintext, key, credential, VaultHeaderFormatVersion.Current);
		credential.SecretFormatVersion = CredentialSecretFormatVersion.AesGcmV2;

		var decrypted = sut.Decrypt(credential, key, VaultHeaderFormatVersion.Current);

		Assert.Equal(plaintext, decrypted);
	}

	[Fact]
	public void Decrypt_WhenLegacyFormat_UsesRawAesGcmPacket()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		var plaintext = Encoding.UTF8.GetBytes("legacy-secret");
		credential.SecretFormatVersion = CredentialSecretFormatVersion.Legacy;
		credential.EncryptedPassword = _encryption.Encrypt(plaintext, key);

		var decrypted = sut.Decrypt(credential, key, VaultHeaderFormatVersion.Current);

		Assert.Equal(plaintext, decrypted);
	}

	[Fact]
	public void Decrypt_WhenUnsupportedSecretFormat_Throws()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.SecretFormatVersion = -123;

		var ex = Assert.Throws<InvalidOperationException>(() => sut.Decrypt(credential, key, VaultHeaderFormatVersion.Current));
		Assert.Contains("Secret-Formatversion", ex.Message);
	}

	[Fact]
	public void DecryptV1_WithValidEnvelope_DecryptsSuccessfully()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.SecretFormatVersion = CredentialSecretFormatVersion.AesGcmV1;

		var plaintext = Encoding.UTF8.GetBytes("v1-secret");
		var aad = BuildV1Aad(VaultHeaderFormatVersion.Current, credential.CredentialUuid, credential.CredentialType, fieldDiscriminator: 1);
		var packet = _encryption.Encrypt(plaintext, key, aad);
		credential.EncryptedPassword = PrependVersion((byte)CredentialSecretFormatVersion.AesGcmV1, packet);

		var decrypted = sut.Decrypt(credential, key, VaultHeaderFormatVersion.Current);

		Assert.Equal(plaintext, decrypted);
	}

	[Fact]
	public void DecryptV1_WithTooShortEnvelope_Throws()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.SecretFormatVersion = CredentialSecretFormatVersion.AesGcmV1;
		credential.EncryptedPassword = [(byte)CredentialSecretFormatVersion.AesGcmV1];

		Assert.Throws<InvalidOperationException>(() => sut.Decrypt(credential, key, VaultHeaderFormatVersion.Current));
	}

	[Fact]
	public void DecryptV1_WithVersionMismatch_Throws()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.SecretFormatVersion = CredentialSecretFormatVersion.AesGcmV1;
		credential.EncryptedPassword = PrependVersion((byte)CredentialSecretFormatVersion.AesGcmV2, _encryption.Encrypt(Encoding.UTF8.GetBytes("x"), key));

		Assert.Throws<InvalidOperationException>(() => sut.Decrypt(credential, key, VaultHeaderFormatVersion.Current));
	}

	[Fact]
	public void DecryptV1_WithUnsupportedVaultFormat_Throws()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.SecretFormatVersion = CredentialSecretFormatVersion.AesGcmV1;
		credential.EncryptedPassword = PrependVersion((byte)CredentialSecretFormatVersion.AesGcmV1, _encryption.Encrypt(Encoding.UTF8.GetBytes("x"), key));

		Assert.Throws<InvalidOperationException>(() => sut.Decrypt(credential, key, VaultHeaderFormatVersion.Legacy));
	}

	[Fact]
	public void Encrypt_WithInvalidCredentialUuid_Throws()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.CredentialUuid = "not-a-guid";

		Assert.Throws<InvalidOperationException>(() => sut.Encrypt(Encoding.UTF8.GetBytes("x"), key, credential, VaultHeaderFormatVersion.Current));
	}

	[Fact]
	public void DecryptV2_WithInvalidCredentialUuid_Throws()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.CredentialUuid = "invalid";
		credential.SecretFormatVersion = CredentialSecretFormatVersion.AesGcmV2;
		credential.EncryptedPassword = PrependVersion((byte)CredentialSecretFormatVersion.AesGcmV2, _encryption.Encrypt(Encoding.UTF8.GetBytes("x"), key));

		Assert.Throws<InvalidOperationException>(() => sut.Decrypt(credential, key, VaultHeaderFormatVersion.Current));
	}

	[Fact]
	public void EncryptMetadataAndDecryptMetadata_CurrentFormat_RoundTripsFields()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.MetadataFormatVersion = CredentialMetadataFormatVersion.AesGcmV1;

		credential.EncryptedMetadata = sut.EncryptMetadata(credential, key, VaultHeaderFormatVersion.Current);

		var decrypted = sut.DecryptMetadata(credential, key, VaultHeaderFormatVersion.Current);

		Assert.Equal("Title", decrypted.Title);
		Assert.Equal("alice", decrypted.Username);
		Assert.Equal("https://example.test", decrypted.Url);
		Assert.Equal("note", decrypted.Notes);
		Assert.Equal("Key", decrypted.IconKey);
		Assert.Equal(CredentialType.ApiKey, decrypted.CredentialType);
	}

	[Fact]
	public void DecryptMetadata_WhenLegacyFormat_ReturnsClone()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.MetadataFormatVersion = CredentialMetadataFormatVersion.Legacy;
		credential.EncryptedMetadata = [];

		var clone = sut.DecryptMetadata(credential, key, VaultHeaderFormatVersion.Current);

		Assert.NotSame(credential, clone);
		Assert.Equal(credential.Title, clone.Title);
		Assert.Equal(credential.EncryptedPassword, clone.EncryptedPassword);
	}

	[Fact]
	public void DecryptMetadata_WhenUnsupportedFormat_Throws()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.MetadataFormatVersion = 999;

		Assert.Throws<InvalidOperationException>(() => sut.DecryptMetadata(credential, key, VaultHeaderFormatVersion.Current));
	}

	[Fact]
	public void DecryptMetadataV1_WithTooShortEnvelope_Throws()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.MetadataFormatVersion = CredentialMetadataFormatVersion.AesGcmV1;
		credential.EncryptedMetadata = [(byte)CredentialMetadataFormatVersion.AesGcmV1];

		Assert.Throws<InvalidOperationException>(() => sut.DecryptMetadata(credential, key, VaultHeaderFormatVersion.Current));
	}

	[Fact]
	public void DecryptMetadataV1_WithVersionMismatch_Throws()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.MetadataFormatVersion = CredentialMetadataFormatVersion.AesGcmV1;
		credential.EncryptedMetadata = PrependVersion((byte)CredentialSecretFormatVersion.AesGcmV2, _encryption.Encrypt(Encoding.UTF8.GetBytes("{}"), key));

		Assert.Throws<InvalidOperationException>(() => sut.DecryptMetadata(credential, key, VaultHeaderFormatVersion.Current));
	}

	[Fact]
	public void DecryptMetadataV1_WithInvalidJsonPayload_ThrowsJsonException()
	{
		var sut = CreateSut();
		var key = MakeKey();
		var credential = CreateCredential();
		credential.MetadataFormatVersion = CredentialMetadataFormatVersion.AesGcmV1;
		var aad = BuildV2Aad(VaultHeaderFormatVersion.Current, credential.CredentialUuid, fieldDiscriminator: 2);
		var nonJsonPayload = Encoding.UTF8.GetBytes("not-json");
		var packet = _encryption.Encrypt(nonJsonPayload, key, aad);
		credential.EncryptedMetadata = PrependVersion((byte)CredentialMetadataFormatVersion.AesGcmV1, packet);

		Assert.Throws<JsonException>(() => sut.DecryptMetadata(credential, key, VaultHeaderFormatVersion.Current));
	}

	private static byte[] BuildV1Aad(int vaultFormatVersion, string credentialUuid, CredentialType credentialType, byte fieldDiscriminator)
	{
		var aad = new byte[22];
		BitConverter.GetBytes(vaultFormatVersion).CopyTo(aad, 0);
		Guid.ParseExact(credentialUuid, "N").TryWriteBytes(aad.AsSpan(4, 16));
		aad[20] = fieldDiscriminator;
		aad[21] = (byte)credentialType;
		return aad;
	}

	private static byte[] BuildV2Aad(int vaultFormatVersion, string credentialUuid, byte fieldDiscriminator)
	{
		var aad = new byte[21];
		BitConverter.GetBytes(vaultFormatVersion).CopyTo(aad, 0);
		Guid.ParseExact(credentialUuid, "N").TryWriteBytes(aad.AsSpan(4, 16));
		aad[20] = fieldDiscriminator;
		return aad;
	}

	private static byte[] PrependVersion(byte version, byte[] packet)
	{
		var envelope = new byte[packet.Length + 1];
		envelope[0] = version;
		packet.CopyTo(envelope, 1);
		return envelope;
	}
}
