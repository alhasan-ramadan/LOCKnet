using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using System.Security;
using System.Security.Cryptography;

namespace LOCKnet.Core.Tests.Security;

sealed class InMemoryVaultStore : IMasterKeyRepository, IVaultMigrationRepository
{
	private VaultHeader? _header;
	private readonly List<CredentialRecord> _credentials = [];
	private int _nextId = 1;

	public bool ThrowOnApplyMigration { get; set; }
	public bool ThrowOnCompactStorage { get; set; }
	public int CompactStorageCallCount { get; private set; }

	public void Create(VaultHeader header)
	{
		if (_header is not null)
			throw new InvalidOperationException("Vault-Header existiert bereits.");

		_header = CloneHeader(header);
	}

	public VaultHeader? Get() => _header is null ? null : CloneHeader(_header);

	public void Update(VaultHeader header) => _header = CloneHeader(header);

	public void Delete() => _header = null;

	public IReadOnlyList<CredentialRecord> GetAllCredentials()
		=> _credentials.Select(CloneCredential).ToList();

	public void ApplyMigration(VaultHeader header, IReadOnlyList<CredentialRecord> credentials)
	{
		if (ThrowOnApplyMigration)
			throw new InvalidOperationException("Simulierter Migrationsabbruch.");

		_header = CloneHeader(header);
		foreach (var credential in credentials)
		{
			var index = _credentials.FindIndex(c => c.Id == credential.Id);
			if (index >= 0)
				_credentials[index] = CloneCredential(credential);
		}
	}

	public void CompactStorage()
	{
		CompactStorageCallCount++;
		if (ThrowOnCompactStorage)
			throw new InvalidOperationException("Simulierter Kompaktierungsfehler.");
	}

	public CredentialRecord AddCredential(CredentialRecord credential)
	{
		var stored = CloneCredential(credential);
		stored.Id = _nextId++;
		_credentials.Add(stored);
		return CloneCredential(stored);
	}

	public CredentialRecord GetCredential(int id)
		=> CloneCredential(_credentials.Single(c => c.Id == id));

	private static VaultHeader CloneHeader(VaultHeader header) => new()
	{
		FormatVersion = header.FormatVersion,
		KdfIdentifier = header.KdfIdentifier,
		KdfParameters = new VaultKdfParameters
		{
			HashAlgorithm = header.KdfParameters.HashAlgorithm,
			Iterations = header.KdfParameters.Iterations,
			KeyLengthBytes = header.KdfParameters.KeyLengthBytes,
			SaltLengthBytes = header.KdfParameters.SaltLengthBytes,
		},
		Salt = header.Salt.ToArray(),
		WrappedVaultKey = header.WrappedVaultKey.ToArray(),
		LegacyPasswordHash = header.LegacyPasswordHash.ToArray(),
		UsesLegacyKeyMaterial = header.UsesLegacyKeyMaterial,
		RequiresStorageCompaction = header.RequiresStorageCompaction,
		CreatedAt = header.CreatedAt,
		UpdatedAt = header.UpdatedAt,
	};

	private static CredentialRecord CloneCredential(CredentialRecord credential) => new()
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
}

public class MasterKeyManagerTests
{
	private static SecureString MakeSecure(string value)
	{
		var s = new SecureString();
		foreach (var c in value) s.AppendChar(c);
		s.MakeReadOnly();
		return s;
	}

	private static MasterKeyManager BuildSut(out InMemoryVaultStore store)
	{
		store = new InMemoryVaultStore();
		var encryption = new AesGcmEncryptionService();
		return new MasterKeyManager(
			new Pbkdf2KeyDerivationService(),
			store,
			store,
			encryption,
			new CredentialEnvelopeService(encryption),
			new SecureStringService());
	}

	private static CredentialRecord AddLegacyCredential(InMemoryVaultStore store, byte[] legacyKey, string secret, string title = "Legacy")
	{
		var encryption = new AesGcmEncryptionService();
		var plaintext = System.Text.Encoding.UTF8.GetBytes(secret);
		try
		{
			return store.AddCredential(new CredentialRecord
			{
				Title = title,
				Username = "legacy-user",
				EncryptedPassword = encryption.Encrypt(plaintext, legacyKey),
				EncryptedMetadata = [],
				SecretFormatVersion = CredentialSecretFormatVersion.Legacy,
				MetadataFormatVersion = CredentialMetadataFormatVersion.Legacy,
				CredentialUuid = string.Empty,
				CredentialType = CredentialType.ApiKey,
				Url = "https://legacy.example",
				Notes = "legacy-note",
				IconKey = "legacy-icon",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow,
			});
		}
		finally
		{
			CryptographicOperations.ZeroMemory(plaintext);
		}
	}

	private static CredentialRecord AddCurrentCredential(InMemoryVaultStore store, byte[] vaultKey, string secret, string title = "Current")
	{
		var encryption = new AesGcmEncryptionService();
		var envelope = new CredentialEnvelopeService(encryption);
		var plaintext = System.Text.Encoding.UTF8.GetBytes(secret);
		var record = new CredentialRecord
		{
			Title = title,
			Username = "current-user",
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			CredentialType = CredentialType.ApiKey,
			Url = "https://current.example",
			Notes = "current-note",
			IconKey = "current-icon",
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		};

		try
		{
			record.EncryptedPassword = envelope.Encrypt(plaintext, vaultKey, record, VaultHeaderFormatVersion.Current);
			record.EncryptedMetadata = envelope.EncryptMetadata(record, vaultKey, VaultHeaderFormatVersion.Current);
			record.Title = string.Empty;
			record.Username = null;
			record.Url = null;
			record.Notes = null;
			record.IconKey = null;
			record.CredentialType = CredentialType.Password;
			return store.AddCredential(record);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(plaintext);
		}
	}

	private static void SeedLegacyVault(InMemoryVaultStore store, string password, bool withWrappedLegacyKey)
	{
		var kdf = new Pbkdf2KeyDerivationService();
		var encryption = new AesGcmEncryptionService();
		var secure = new SecureStringService();
		var securePassword = MakeSecure(password);
		var passwordBytes = secure.ToByteArray(securePassword);
		var parameters = kdf.GetDefaultParameters();
		var salt = kdf.GenerateSalt(parameters.SaltLengthBytes);

		try
		{
			var legacyKey = kdf.DeriveKey(passwordBytes, salt, parameters);
			var header = new VaultHeader
			{
				FormatVersion = withWrappedLegacyKey ? VaultHeaderFormatVersion.WrappedVaultKeyV1 : VaultHeaderFormatVersion.Legacy,
				KdfIdentifier = kdf.Identifier,
				KdfParameters = parameters,
				Salt = salt,
				WrappedVaultKey = withWrappedLegacyKey ? encryption.Encrypt(legacyKey, legacyKey) : [],
				LegacyPasswordHash = kdf.ComputePasswordHash(passwordBytes, salt, parameters),
				UsesLegacyKeyMaterial = withWrappedLegacyKey,
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow,
			};
			store.Create(header);
			AddLegacyCredential(store, legacyKey, "legacy-secret-1", "Legacy One");
		}
		finally
		{
			secure.ZeroMemory(passwordBytes);
		}
	}

	[Fact]
	public void IsInitialized_BeforeSetup_ReturnsFalse()
	{
		var sut = BuildSut(out _);
		Assert.False(sut.IsInitialized);
	}

	[Fact]
	public void Initialize_PersistsCurrentHeaderWithWrappedRandomVaultKey()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("masterkey"));

		var header = store.Get();
		Assert.NotNull(header);
		Assert.Equal(VaultHeaderFormatVersion.Current, header.FormatVersion);
		Assert.Equal("PBKDF2-SHA256", header.KdfIdentifier);
		Assert.NotEmpty(header.WrappedVaultKey);
		Assert.Empty(header.LegacyPasswordHash);
		Assert.False(header.UsesLegacyKeyMaterial);
	}

	[Fact]
	public void Unlock_CorrectPassword_Returns32ByteVaultKey()
	{
		var sut = BuildSut(out _);
		const string password = "correct horse battery staple";
		sut.Initialize(MakeSecure(password));

		var key = sut.Unlock(MakeSecure(password));

		Assert.NotNull(key);
		Assert.Equal(32, key.Length);
	}

	[Fact]
	public void Unlock_WrongPassword_ReturnsNull()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("correct"));

		Assert.Null(sut.Unlock(MakeSecure("wrong")));
	}

	[Fact]
	public void Unlock_LegacyVault_MigratesHeaderAndCredentialCiphertext()
	{
		var sut = BuildSut(out var store);
		SeedLegacyVault(store, "legacy-password", withWrappedLegacyKey: false);

		var key = sut.Unlock(MakeSecure("legacy-password"))!;
		var header = store.Get()!;
		var credential = store.GetAllCredentials().Single();
		var envelope = new CredentialEnvelopeService(new AesGcmEncryptionService());
		var plaintext = envelope.Decrypt(credential, key, header.FormatVersion);
		var metadata = envelope.DecryptMetadata(credential, key, header.FormatVersion);

		Assert.Equal(VaultHeaderFormatVersion.Current, header.FormatVersion);
		Assert.False(header.UsesLegacyKeyMaterial);
		Assert.Empty(header.LegacyPasswordHash);
		Assert.False(header.RequiresStorageCompaction);
		Assert.Equal(CredentialSecretFormatVersion.Current, credential.SecretFormatVersion);
		Assert.Equal(CredentialMetadataFormatVersion.Current, credential.MetadataFormatVersion);
		Assert.NotEmpty(credential.CredentialUuid);
		Assert.NotEmpty(credential.EncryptedMetadata);
		Assert.Equal(string.Empty, credential.Title);
		Assert.Equal("legacy-secret-1", System.Text.Encoding.UTF8.GetString(plaintext));
		Assert.Equal("Legacy One", metadata.Title);
		Assert.Equal("legacy-user", metadata.Username);
		Assert.Equal(CredentialType.ApiKey, metadata.CredentialType);
	}

	[Fact]
	public void Unlock_MixedLegacyAndCurrentRecords_MigratesOnlyLegacyEntries()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("vault-password"));
		var currentVaultKey = sut.Unlock(MakeSecure("vault-password"))!;
		var legacyRecord = AddLegacyCredential(store, currentVaultKey, "legacy-secret", "Legacy");
		var currentRecord = AddCurrentCredential(store, currentVaultKey, "current-secret", "Current");
		CryptographicOperations.ZeroMemory(currentVaultKey);

		var reopenedKey = sut.Unlock(MakeSecure("vault-password"))!;
		var header = store.Get()!;
		var records = store.GetAllCredentials();
		var migratedLegacy = records.Single(r => r.Id == legacyRecord.Id);
		var unchangedCurrent = records.Single(r => r.Id == currentRecord.Id);
		var envelope = new CredentialEnvelopeService(new AesGcmEncryptionService());

		Assert.Equal(CredentialSecretFormatVersion.Current, migratedLegacy.SecretFormatVersion);
		Assert.Equal(CredentialMetadataFormatVersion.Current, migratedLegacy.MetadataFormatVersion);
		Assert.NotEmpty(migratedLegacy.CredentialUuid);
		Assert.Equal(currentRecord.CredentialUuid, unchangedCurrent.CredentialUuid);
		Assert.Equal(currentRecord.EncryptedPassword, unchangedCurrent.EncryptedPassword);
		Assert.Equal(currentRecord.EncryptedMetadata, unchangedCurrent.EncryptedMetadata);
		Assert.Equal("legacy-secret", System.Text.Encoding.UTF8.GetString(envelope.Decrypt(migratedLegacy, reopenedKey, header.FormatVersion)));
		Assert.Equal("Legacy", envelope.DecryptMetadata(migratedLegacy, reopenedKey, header.FormatVersion).Title);
		Assert.Equal(1, store.CompactStorageCallCount);
		Assert.False(store.Get()!.RequiresStorageCompaction);
	}

	[Fact]
	public void Unlock_WhenMigrationApplyFails_LeavesVaultStateRecoverable()
	{
		var sut = BuildSut(out var store);
		SeedLegacyVault(store, "legacy-password", withWrappedLegacyKey: false);
		var beforeHeader = store.Get()!;
		var beforeCredential = store.GetAllCredentials().Single();
		store.ThrowOnApplyMigration = true;

		Assert.Throws<InvalidOperationException>(() => sut.Unlock(MakeSecure("legacy-password")));
		Assert.Equal(beforeHeader.FormatVersion, store.Get()!.FormatVersion);
		Assert.Equal(beforeCredential.EncryptedPassword, store.GetAllCredentials().Single().EncryptedPassword);

		store.ThrowOnApplyMigration = false;
		var key = sut.Unlock(MakeSecure("legacy-password"));
		Assert.NotNull(key);
		Assert.Equal(VaultHeaderFormatVersion.Current, store.Get()!.FormatVersion);
	}

	[Fact]
	public void Unlock_WhenCompactionFails_KeepsPendingFlagForRetry()
	{
		var sut = BuildSut(out var store);
		SeedLegacyVault(store, "legacy-password", withWrappedLegacyKey: false);
		store.ThrowOnCompactStorage = true;

		Assert.Throws<InvalidOperationException>(() => sut.Unlock(MakeSecure("legacy-password")));
		Assert.True(store.Get()!.RequiresStorageCompaction);
		Assert.Equal(1, store.CompactStorageCallCount);

		store.ThrowOnCompactStorage = false;
		var key = sut.Unlock(MakeSecure("legacy-password"));
		Assert.NotNull(key);
		Assert.False(store.Get()!.RequiresStorageCompaction);
		Assert.Equal(2, store.CompactStorageCallCount);
	}

	[Fact]
	public void ChangePassword_OnLegacyVault_MigratesThenMakesOldPasswordUseless()
	{
		var sut = BuildSut(out var store);
		SeedLegacyVault(store, "old-password", withWrappedLegacyKey: true);

		sut.ChangePassword(MakeSecure("old-password"), MakeSecure("new-password"));

		Assert.Null(sut.Unlock(MakeSecure("old-password")));
		var newKey = sut.Unlock(MakeSecure("new-password"));
		Assert.NotNull(newKey);
		Assert.Equal(VaultHeaderFormatVersion.Current, store.Get()!.FormatVersion);
		Assert.False(store.Get()!.UsesLegacyKeyMaterial);
	}

	[Fact]
	public void ChangePassword_AfterMigration_RewrapsWithoutReintroducingLegacyState()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("initial"));
		var migratedKey = sut.Unlock(MakeSecure("initial"))!;
		AddCurrentCredential(store, migratedKey, "current-secret", "Current");
		CryptographicOperations.ZeroMemory(migratedKey);

		sut.ChangePassword(MakeSecure("initial"), MakeSecure("updated"));

		Assert.Null(sut.Unlock(MakeSecure("initial")));
		Assert.NotNull(sut.Unlock(MakeSecure("updated")));
		Assert.False(store.Get()!.UsesLegacyKeyMaterial);
	}

	[Fact]
	public void Unlock_TamperedHeaderIdentifier_Throws()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("pw"));
		var header = store.Get()!;
		header.KdfIdentifier = "PBKDF2-SHA1";
		store.Update(header);

		Assert.Throws<InvalidOperationException>(() => sut.Unlock(MakeSecure("pw")));
	}

	[Fact]
	public void Unlock_TamperedWrappedVaultKeyStructure_Throws()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("pw"));
		var header = store.Get()!;
		header.WrappedVaultKey = [0x01, 0x02, 0x03];
		store.Update(header);

		Assert.Throws<InvalidOperationException>(() => sut.Unlock(MakeSecure("pw")));
	}

	[Fact]
	public void Initialize_CalledTwice_Throws()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("first"));
		Assert.Throws<InvalidOperationException>(() => sut.Initialize(MakeSecure("second")));
	}

	[Fact]
	public void ChangePassword_BeforeInitialize_Throws()
	{
		var sut = BuildSut(out _);
		Assert.Throws<InvalidOperationException>(() => sut.ChangePassword(MakeSecure("old"), MakeSecure("new")));
	}

	[Fact]
	public void Initialize_NullPassword_Throws()
	{
		var sut = BuildSut(out _);
		Assert.Throws<ArgumentNullException>(() => sut.Initialize(null!));
	}

	[Fact]
	public void Unlock_NullPassword_Throws()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("pw"));
		Assert.Throws<ArgumentNullException>(() => sut.Unlock(null!));
	}
}
