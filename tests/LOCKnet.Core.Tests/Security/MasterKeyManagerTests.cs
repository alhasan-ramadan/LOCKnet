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
	public StorageCompactionFailureKind CompactStorageFailureKind { get; set; } = StorageCompactionFailureKind.BusyOrLocked;
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

	public StorageCompactionInfo CompactStorage()
	{
		CompactStorageCallCount++;
		if (ThrowOnCompactStorage)
		{
			return new StorageCompactionInfo
			{
				IsPending = true,
				FailureKind = CompactStorageFailureKind,
				UserMessage = "Simulierter Kompaktierungsfehler.",
				LastError = "Simulierter Kompaktierungsfehler."
			};
		}

		return new StorageCompactionInfo
		{
			IsPending = false,
			FailureKind = StorageCompactionFailureKind.None,
			UserMessage = "Speicherbereinigung abgeschlossen."
		};
	}

	public bool HasPendingStorageArtifacts() => false;

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
		LastStorageCompactionAttemptUtc = header.LastStorageCompactionAttemptUtc,
		LastStorageCompactionFailureKind = header.LastStorageCompactionFailureKind,
		LastStorageCompactionError = header.LastStorageCompactionError,
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
		=> BuildSut(out store, out _);

	private static MasterKeyManager BuildSut(out InMemoryVaultStore store, out SessionManager sessionManager)
	{
		store = new InMemoryVaultStore();
		var encryption = new AesGcmEncryptionService();
		sessionManager = new SessionManager();
		return new MasterKeyManager(
			new Pbkdf2KeyDerivationService(),
			store,
			store,
			encryption,
			new CredentialEnvelopeService(encryption),
			sessionManager,
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

		var unlock = sut.Unlock(MakeSecure(password));

		Assert.NotNull(unlock);
		Assert.Equal(32, unlock.VaultKey.Length);
		Assert.False(unlock.StorageCompaction.IsPending);
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

		var unlock = sut.Unlock(MakeSecure("legacy-password"))!;
		var key = unlock.VaultKey;
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
		Assert.False(unlock.StorageCompaction.IsPending);
	}

	[Fact]
	public void Unlock_MixedLegacyAndCurrentRecords_MigratesOnlyLegacyEntries()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("vault-password"));
		var currentUnlock = sut.Unlock(MakeSecure("vault-password"))!;
		var currentVaultKey = currentUnlock.VaultKey;
		var legacyRecord = AddLegacyCredential(store, currentVaultKey, "legacy-secret", "Legacy");
		var currentRecord = AddCurrentCredential(store, currentVaultKey, "current-secret", "Current");
		CryptographicOperations.ZeroMemory(currentVaultKey);

		var reopenedUnlock = sut.Unlock(MakeSecure("vault-password"))!;
		var reopenedKey = reopenedUnlock.VaultKey;
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
		Assert.False(reopenedUnlock.StorageCompaction.IsPending);
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
		var unlock = sut.Unlock(MakeSecure("legacy-password"));
		Assert.NotNull(unlock);
		Assert.False(unlock.StorageCompaction.IsPending);
		Assert.Equal(VaultHeaderFormatVersion.Current, store.Get()!.FormatVersion);
	}

	[Fact]
	public void Unlock_WhenCompactionFails_AllowsControlledDegradedModeAndKeepsPendingFlagForRetry()
	{
		var sut = BuildSut(out var store, out var sessionManager);
		SeedLegacyVault(store, "legacy-password", withWrappedLegacyKey: false);
		store.ThrowOnCompactStorage = true;

		var unlock = sut.Unlock(MakeSecure("legacy-password"));
		Assert.NotNull(unlock);
		Assert.True(unlock.StorageCompaction.IsPending);
		Assert.Equal(StorageCompactionFailureKind.BusyOrLocked, unlock.StorageCompaction.FailureKind);
		Assert.True(store.Get()!.RequiresStorageCompaction);
		Assert.NotNull(store.Get()!.LastStorageCompactionAttemptUtc);
		Assert.Equal(StorageCompactionFailureKind.BusyOrLocked, store.Get()!.LastStorageCompactionFailureKind);
		Assert.Equal("Simulierter Kompaktierungsfehler.", store.Get()!.LastStorageCompactionError);
		Assert.Equal(1, store.CompactStorageCallCount);

		store.ThrowOnCompactStorage = false;
		var retryUnlock = sut.Unlock(MakeSecure("legacy-password"));
		Assert.NotNull(retryUnlock);
		Assert.True(retryUnlock.StorageCompaction.IsPending);
		Assert.True(retryUnlock.StorageCompaction.AutoRetryDeferred);
		Assert.Equal(1, store.CompactStorageCallCount);
		sessionManager.Open(retryUnlock.VaultKey.ToArray());

		var manualRetry = sut.RetryPendingStorageCompaction();
		Assert.False(manualRetry.IsPending);
		Assert.False(store.Get()!.RequiresStorageCompaction);
		Assert.Equal(2, store.CompactStorageCallCount);
		Assert.Null(store.Get()!.LastStorageCompactionAttemptUtc);
		Assert.Equal(StorageCompactionFailureKind.None, store.Get()!.LastStorageCompactionFailureKind);
		Assert.Null(store.Get()!.LastStorageCompactionError);
	}

	[Fact]
	public void RetryPendingStorageCompaction_WhenFailureRepeats_KeepsPendingStateAndUpdatesFailureMetadata()
	{
		var sut = BuildSut(out var store, out var sessionManager);
		sut.Initialize(MakeSecure("vault-password"));
		var unlock = sut.Unlock(MakeSecure("vault-password"))!;
		sessionManager.Open(unlock.VaultKey.ToArray());
		var header = store.Get()!;
		header.RequiresStorageCompaction = true;
		store.Update(header);
		store.ThrowOnCompactStorage = true;
		store.CompactStorageFailureKind = StorageCompactionFailureKind.Io;

		var firstRetry = sut.RetryPendingStorageCompaction();

		Assert.True(firstRetry.IsPending);
		Assert.Equal(StorageCompactionFailureKind.Io, firstRetry.FailureKind);
		Assert.True(store.Get()!.RequiresStorageCompaction);
		Assert.NotNull(store.Get()!.LastStorageCompactionAttemptUtc);
		Assert.Equal(StorageCompactionFailureKind.Io, store.Get()!.LastStorageCompactionFailureKind);
		Assert.Equal("Simulierter Kompaktierungsfehler.", store.Get()!.LastStorageCompactionError);
		Assert.Equal(1, store.CompactStorageCallCount);

		var secondRetry = sut.RetryPendingStorageCompaction();

		Assert.True(secondRetry.IsPending);
		Assert.Equal(StorageCompactionFailureKind.Io, secondRetry.FailureKind);
		Assert.Equal(2, store.CompactStorageCallCount);
	}

	[Fact]
	public void ChangePassword_OnLegacyVault_MigratesThenMakesOldPasswordUseless()
	{
		var sut = BuildSut(out var store);
		SeedLegacyVault(store, "old-password", withWrappedLegacyKey: true);

		sut.ChangePassword(MakeSecure("old-password"), MakeSecure("new-password"));

		Assert.Null(sut.Unlock(MakeSecure("old-password")));
		var newUnlock = sut.Unlock(MakeSecure("new-password"));
		Assert.NotNull(newUnlock);
		Assert.False(newUnlock.StorageCompaction.IsPending);
		Assert.Equal(VaultHeaderFormatVersion.Current, store.Get()!.FormatVersion);
		Assert.False(store.Get()!.UsesLegacyKeyMaterial);
	}

	[Fact]
	public void ChangePassword_AfterMigration_RewrapsWithoutReintroducingLegacyState()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("initial"));
		var migratedUnlock = sut.Unlock(MakeSecure("initial"))!;
		var migratedKey = migratedUnlock.VaultKey;
		AddCurrentCredential(store, migratedKey, "current-secret", "Current");
		CryptographicOperations.ZeroMemory(migratedKey);

		sut.ChangePassword(MakeSecure("initial"), MakeSecure("updated"));

		Assert.Null(sut.Unlock(MakeSecure("initial")));
		var updatedUnlock = sut.Unlock(MakeSecure("updated"));
		Assert.NotNull(updatedUnlock);
		Assert.False(updatedUnlock.StorageCompaction.IsPending);
		Assert.False(store.Get()!.UsesLegacyKeyMaterial);
	}

	[Fact]
	public void ChangePassword_WithPendingCompaction_PreservesPendingStateMetadataAndLaterCleanupCanClearIt()
	{
		var sut = BuildSut(out var store, out var sessionManager);
		sut.Initialize(MakeSecure("initial"));
		var initialUnlock = sut.Unlock(MakeSecure("initial"))!;
		sessionManager.Open(initialUnlock.VaultKey.ToArray());
		var header = store.Get()!;
		var lastAttemptUtc = DateTime.UtcNow;
		header.RequiresStorageCompaction = true;
		header.LastStorageCompactionAttemptUtc = lastAttemptUtc;
		header.LastStorageCompactionFailureKind = StorageCompactionFailureKind.BusyOrLocked;
		header.LastStorageCompactionError = "locked";
		store.Update(header);

		sut.ChangePassword(MakeSecure("initial"), MakeSecure("updated"));

		Assert.Null(sut.Unlock(MakeSecure("initial")));
		var rotatedHeader = store.Get()!;
		Assert.True(rotatedHeader.RequiresStorageCompaction);
		Assert.Equal(lastAttemptUtc, rotatedHeader.LastStorageCompactionAttemptUtc);
		Assert.Equal(StorageCompactionFailureKind.BusyOrLocked, rotatedHeader.LastStorageCompactionFailureKind);
		Assert.Equal("locked", rotatedHeader.LastStorageCompactionError);

		var degradedUnlock = sut.Unlock(MakeSecure("updated"));
		Assert.NotNull(degradedUnlock);
		Assert.True(degradedUnlock.StorageCompaction.IsPending);
		Assert.True(degradedUnlock.StorageCompaction.AutoRetryDeferred);
		Assert.Equal(StorageCompactionFailureKind.BusyOrLocked, degradedUnlock.StorageCompaction.FailureKind);
		Assert.Equal("locked", degradedUnlock.StorageCompaction.LastError);
		Assert.Equal(lastAttemptUtc, degradedUnlock.StorageCompaction.LastAttemptUtc);
		Assert.Equal(0, store.CompactStorageCallCount);
		sessionManager.Open(degradedUnlock.VaultKey.ToArray());

		var cleanup = sut.RetryPendingStorageCompaction();
		Assert.False(cleanup.IsPending);
		Assert.False(store.Get()!.RequiresStorageCompaction);
		Assert.Null(store.Get()!.LastStorageCompactionAttemptUtc);
		Assert.Equal(StorageCompactionFailureKind.None, store.Get()!.LastStorageCompactionFailureKind);
		Assert.Null(store.Get()!.LastStorageCompactionError);
		Assert.Equal(1, store.CompactStorageCallCount);
	}

	[Fact]
	public void GetStorageCompactionInfo_WithPendingStateWithoutAttempt_DoesNotThrow()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("vault-password"));
		var header = store.Get()!;
		header.RequiresStorageCompaction = true;
		header.LastStorageCompactionAttemptUtc = null;
		header.LastStorageCompactionFailureKind = StorageCompactionFailureKind.None;
		header.LastStorageCompactionError = null;
		store.Update(header);

		var info = sut.GetStorageCompactionInfo();

		Assert.True(info.IsPending);
		Assert.False(info.AutoRetryDeferred);
		Assert.Null(info.LastAttemptUtc);
		Assert.Null(info.NextAutomaticRetryUtc);
		Assert.Equal(StorageCompactionFailureKind.None, info.FailureKind);
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

	[Fact]
	public void ChangePassword_WithWrongCurrentPassword_ThrowsUnauthorizedAccessException()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("correct-current"));

		Assert.Throws<UnauthorizedAccessException>(() => sut.ChangePassword(MakeSecure("wrong-current"), MakeSecure("new-pass")));
	}

	[Fact]
	public void Unlock_LegacyVault_WrongPassword_ReturnsNull()
	{
		var sut = BuildSut(out var store);
		SeedLegacyVault(store, "legacy-pass", withWrappedLegacyKey: false);

		var unlock = sut.Unlock(MakeSecure("wrong-pass"));

		Assert.Null(unlock);
	}

	[Fact]
	public void RetryPendingStorageCompaction_WhenSessionLocked_ReturnsBusyState()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("pw"));
		var header = store.Get()!;
		header.RequiresStorageCompaction = true;
		store.Update(header);

		var info = sut.RetryPendingStorageCompaction();

		Assert.True(info.IsPending);
		Assert.Equal(StorageCompactionFailureKind.BusyOrLocked, info.FailureKind);
		Assert.Contains("gesperrt", info.UserMessage);
	}

	[Fact]
	public void Unlock_WithSaltLengthMismatch_Throws()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("pw"));
		var header = store.Get()!;
		header.Salt = [0x01, 0x02, 0x03];
		store.Update(header);

		Assert.Throws<InvalidOperationException>(() => sut.Unlock(MakeSecure("pw")));
	}

	[Fact]
	public void Unlock_WithUnsupportedHeaderVersion_Throws()
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("pw"));
		var header = store.Get()!;
		header.FormatVersion = 999;
		store.Update(header);

		Assert.Throws<InvalidOperationException>(() => sut.Unlock(MakeSecure("pw")));
	}

	[Fact]
	public void Unlock_WithLegacyHeaderContainingWrappedKey_Throws()
	{
		var sut = BuildSut(out var store);
		SeedLegacyVault(store, "legacy", withWrappedLegacyKey: false);
		var header = store.Get()!;
		header.WrappedVaultKey = Enumerable.Repeat((byte)0xAB, 60).ToArray();
		store.Update(header);

		Assert.Throws<InvalidOperationException>(() => sut.Unlock(MakeSecure("legacy")));
	}

	[Fact]
	public void Unlock_WithLegacyHeaderWithoutPasswordHash_Throws()
	{
		var sut = BuildSut(out var store);
		SeedLegacyVault(store, "legacy", withWrappedLegacyKey: false);
		var header = store.Get()!;
		header.LegacyPasswordHash = [];
		store.Update(header);

		Assert.Throws<InvalidOperationException>(() => sut.Unlock(MakeSecure("legacy")));
	}

	[Theory]
	[InlineData(StorageCompactionFailureKind.BusyOrLocked, "gesperrt")]
	[InlineData(StorageCompactionFailureKind.InsufficientSpace, "freier Speicherplatz")]
	[InlineData(StorageCompactionFailureKind.Io, "I/O-Fehler")]
	[InlineData(StorageCompactionFailureKind.Corruption, "inkonsistent")]
	[InlineData(StorageCompactionFailureKind.Unknown, "konnte nicht abgeschlossen")]
	public void GetStorageCompactionInfo_WhenPending_MapsFailureKindToUserMessage(StorageCompactionFailureKind failureKind, string expectedSnippet)
	{
		var sut = BuildSut(out var store);
		sut.Initialize(MakeSecure("pw"));
		var header = store.Get()!;
		header.RequiresStorageCompaction = true;
		header.LastStorageCompactionFailureKind = failureKind;
		header.LastStorageCompactionAttemptUtc = DateTime.UtcNow.AddMinutes(-1);
		store.Update(header);

		var info = sut.GetStorageCompactionInfo();

		Assert.True(info.IsPending);
		Assert.Equal(failureKind, info.FailureKind);
		Assert.Contains(expectedSnippet, info.UserMessage);
	}
}
