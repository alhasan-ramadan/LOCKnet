using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using LOCKnet.Core.Services;
using System.Security;
using System.Security.Cryptography;

namespace LOCKnet.Core.Tests.Services;

// ── Minimal in-memory fake für ICredentialRepository ─────────────────────────

sealed class InMemoryCredentialRepo : ICredentialRepository
{
	private readonly List<CredentialRecord> _store = [];
	private int _nextId = 1;

	public void Add(CredentialRecord credential)
	{
		credential.Id = _nextId++;
		_store.Add(credential);
	}

	public IReadOnlyList<CredentialRecord> GetAll() => _store.AsReadOnly();

	public CredentialRecord? GetById(int id) => _store.FirstOrDefault(c => c.Id == id);

	public void Update(CredentialRecord credential)
	{
		var idx = _store.FindIndex(c => c.Id == credential.Id);
		if (idx >= 0) _store[idx] = credential;
	}

	public void Remove(int id) => _store.RemoveAll(c => c.Id == id);
}

sealed class FixedMasterKeyRepo : IMasterKeyRepository
{
	private VaultHeader? _header = new()
	{
		FormatVersion = VaultHeaderFormatVersion.Current,
		KdfIdentifier = "PBKDF2-SHA256",
		KdfParameters = new VaultKdfParameters
		{
			HashAlgorithm = "SHA256",
			Iterations = 600_000,
			KeyLengthBytes = 32,
			SaltLengthBytes = 32,
		},
		Salt = new byte[32],
		WrappedVaultKey = new byte[60],
		LegacyPasswordHash = [],
		UsesLegacyKeyMaterial = false,
		CreatedAt = DateTime.UtcNow,
		UpdatedAt = DateTime.UtcNow,
	};

	public void Create(VaultHeader header) => _header = header;

	public VaultHeader? Get() => _header;

	public void Update(VaultHeader header) => _header = header;

	public void Delete() => _header = null;
}

// ─────────────────────────────────────────────────────────────────────────────

public class CredentialServiceTests
{
	private static SecureString MakeSecure(string value)
	{
		var s = new SecureString();
		foreach (var c in value) s.AppendChar(c);
		s.MakeReadOnly();
		return s;
	}

	private static (CredentialService sut, SessionManager session, InMemoryCredentialRepo repo) BuildSut()
	{
		var repo = new InMemoryCredentialRepo();
		var masterKeyRepo = new FixedMasterKeyRepo();
		var session = new SessionManager();
		var encryption = new AesGcmEncryptionService();
		var sut = new CredentialService(
			repo,
			masterKeyRepo,
			encryption,
			new CredentialEnvelopeService(encryption),
			session,
			new SecureStringService());
		return (sut, session, repo);
	}

	private static void OpenSession(SessionManager session)
	{
		// Deterministischer Test-Key (32 Bytes)
		var key = new byte[32];
		for (var i = 0; i < 32; i++) key[i] = (byte)(i + 1);
		session.Open(key);
	}

	// ── Session guard ─────────────────────────────────────────────────────────

	[Fact]
	public void Add_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(
			() => sut.Add("GitHub", "user", MakeSecure("pw")));
	}

	[Fact]
	public void GetAll_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(() => sut.GetAll());
	}

	[Fact]
	public void GetPassword_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(() => sut.GetPassword(1));
	}

	[Fact]
	public void Remove_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(() => sut.Remove(1));
	}

	// ── Add / GetAll ──────────────────────────────────────────────────────────

	[Fact]
	public void Add_ThenGetAll_ContainsEntry()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		sut.Add("GitHub", "alice", MakeSecure("secret"), "https://github.com");

		var all = sut.GetAll();
		Assert.Single(all);
		Assert.Equal("GitHub", all[0].Title);
		Assert.Equal("alice", all[0].Username);
		Assert.Equal("https://github.com", all[0].Url);
		Assert.Equal(CredentialSecretFormatVersion.Current, all[0].SecretFormatVersion);
		Assert.Equal(CredentialMetadataFormatVersion.Current, all[0].MetadataFormatVersion);
		Assert.NotEmpty(all[0].CredentialUuid);
	}

	[Fact]
	public void Add_PasswordIsStoredEncrypted()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("MyService", null, MakeSecure("plaintextpassword"));

		var record = repo.GetAll()[0];
		// Encrypted bytes must not equal plaintext bytes
		var plaintextBytes = System.Text.Encoding.UTF8.GetBytes("plaintextpassword");
		Assert.False(record.EncryptedPassword.SequenceEqual(plaintextBytes));
		Assert.Equal(CredentialSecretFormatVersion.Current, record.SecretFormatVersion);
		Assert.Equal(CredentialMetadataFormatVersion.Current, record.MetadataFormatVersion);
		Assert.NotEmpty(record.EncryptedMetadata);
		Assert.Equal(string.Empty, record.Title);
		Assert.Null(record.Username);
		Assert.NotEmpty(record.CredentialUuid);
	}

	[Fact]
	public void GetAll_DecryptsEncryptedMetadata()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("Console", "alice", MakeSecure("secret"), url: "https://example.com", notes: "note", iconKey: "github", credentialType: CredentialType.ApiKey);

		var stored = repo.GetAll()[0];
		Assert.NotEmpty(stored.EncryptedMetadata);
		Assert.Equal(string.Empty, stored.Title);

		var materialized = sut.GetAll()[0];
		Assert.Equal("Console", materialized.Title);
		Assert.Equal("alice", materialized.Username);
		Assert.Equal("https://example.com", materialized.Url);
		Assert.Equal("note", materialized.Notes);
		Assert.Equal("github", materialized.IconKey);
		Assert.Equal(CredentialType.ApiKey, materialized.CredentialType);
	}

	// ── GetPassword ───────────────────────────────────────────────────────────

	[Fact]
	public void GetPassword_ReturnsDecryptedPassword()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		const string original = "SuperSecretPassword!";
		sut.Add("TestSite", "bob", MakeSecure(original));
		var all = sut.GetAll();

		using var decrypted = sut.GetPassword(all[0].Id)!;
		var decryptedBytes = new SecureStringService().ToByteArray(decrypted);
		var decryptedText = System.Text.Encoding.UTF8.GetString(decryptedBytes);

		Assert.Equal(original, decryptedText);
		new SecureStringService().ZeroMemory(decryptedBytes);
	}

	[Fact]
	public void GetPassword_NonExistentId_ReturnsNull()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		Assert.Null(sut.GetPassword(999));
	}

	// ── Update ────────────────────────────────────────────────────────────────

	[Fact]
	public void Update_WithNewPassword_PasswordChanges()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		sut.Add("Site", "user", MakeSecure("old"));
		var id = sut.GetAll()[0].Id;

		sut.Update(id, "Site", "user", MakeSecure("new"));

		using var pw = sut.GetPassword(id)!;
		var bytes = new SecureStringService().ToByteArray(pw);
		Assert.Equal("new", System.Text.Encoding.UTF8.GetString(bytes));
		new SecureStringService().ZeroMemory(bytes);
	}

	[Fact]
	public void Update_WithoutNewPassword_PreservesEncryptedPassword()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("Site", "user", MakeSecure("original"));
		var id = sut.GetAll()[0].Id;
		var originalEncrypted = repo.GetAll()[0].EncryptedPassword;

		sut.Update(id, "Site Updated", "user", newPassword: null);

		var updated = repo.GetById(id)!;
		Assert.Equal(string.Empty, updated.Title);
		Assert.NotEmpty(updated.EncryptedMetadata);
		Assert.Equal(originalEncrypted, updated.EncryptedPassword);

		var materialized = sut.GetAll().Single(r => r.Id == id);
		Assert.Equal("Site Updated", materialized.Title);
	}

	[Fact]
	public void GetPassword_WhenCiphertextSwappedBetweenRecords_ThrowsCryptographicException()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("One", null, MakeSecure("secret-one"));
		sut.Add("Two", null, MakeSecure("secret-two"));

		var first = repo.GetAll()[0];
		var second = repo.GetAll()[1];
		var swappedFirst = new CredentialRecord
		{
			Id = first.Id,
			Title = first.Title,
			Username = first.Username,
			EncryptedPassword = second.EncryptedPassword,
			CredentialUuid = first.CredentialUuid,
			SecretFormatVersion = first.SecretFormatVersion,
			CredentialType = first.CredentialType,
			CreatedAt = first.CreatedAt,
			UpdatedAt = first.UpdatedAt,
		};
		repo.Update(swappedFirst);

		Assert.ThrowsAny<CryptographicException>(() => sut.GetPassword(first.Id));
	}

	[Fact]
	public void GetPassword_WithMalformedEnvelope_ThrowsInvalidOperationException()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("Broken", null, MakeSecure("secret"));
		var record = repo.GetAll()[0];
		record.EncryptedPassword = [0x01];
		record.SecretFormatVersion = CredentialSecretFormatVersion.Current;
		repo.Update(record);

		Assert.Throws<InvalidOperationException>(() => sut.GetPassword(record.Id));
	}

	[Fact]
	public void GetAll_WithMalformedMetadataEnvelope_ThrowsInvalidOperationException()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("BrokenMeta", null, MakeSecure("secret"));
		var record = repo.GetAll()[0];
		record.EncryptedMetadata = [0x01];
		record.MetadataFormatVersion = CredentialMetadataFormatVersion.Current;
		repo.Update(record);

		Assert.Throws<InvalidOperationException>(() => sut.GetAll());
	}

	[Fact]
	public void GetAll_WithPlaintextResidueOnCurrentRecord_ThrowsInvalidOperationException()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("Residue", null, MakeSecure("secret"));
		var record = repo.GetAll()[0];
		record.Title = "plaintext";
		repo.Update(record);

		Assert.Throws<InvalidOperationException>(() => sut.GetAll());
	}

	[Fact]
	public void Update_NonExistentId_Throws()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		Assert.Throws<InvalidOperationException>(
			() => sut.Update(999, "X", null, null));
	}

	// ── Remove ────────────────────────────────────────────────────────────────

	[Fact]
	public void Remove_ExistingEntry_IsGone()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		sut.Add("DeleteMe", null, MakeSecure("pw"));
		var id = sut.GetAll()[0].Id;

		sut.Remove(id);

		Assert.Empty(sut.GetAll());
	}

	// ── Session lock during operation ─────────────────────────────────────────

	[Fact]
	public void GetPassword_AfterSessionLocked_Throws()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);
		sut.Add("Site", "user", MakeSecure("pw"));
		var id = sut.GetAll()[0].Id;

		session.Lock();

		Assert.Throws<InvalidOperationException>(() => sut.GetPassword(id));
	}


	// ── Add title validation ────────────────────────────────────────────────────

	[Fact]
	public void Add_EmptyTitle_Throws()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);
		Assert.Throws<ArgumentException>(() => sut.Add("", null, MakeSecure("pw")));
	}

	[Fact]
	public void Add_WhitespaceTitleOnly_Throws()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);
		Assert.Throws<ArgumentException>(() => sut.Add("   ", null, MakeSecure("pw")));
	}

	// ── Update when locked ──────────────────────────────────────────────────────

	[Fact]
	public void Update_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(
			() => sut.Update(1, "Title", null, null));
	}

	// ── CredentialType ───────────────────────────────────────────────────────

	[Fact]
	public void Add_ApiKeyType_TypeIsStoredOnRecord()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("MyApi", null, MakeSecure("secret"), credentialType: CredentialType.ApiKey);

		var stored = repo.GetAll()[0];
		Assert.Equal(CredentialType.Password, stored.CredentialType);
		Assert.Equal(CredentialType.ApiKey, sut.GetAll()[0].CredentialType);
	}

	[Fact]
	public void Add_DefaultType_IsPassword()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("Site", null, MakeSecure("pw"));

		var record = repo.GetAll()[0];
		Assert.Equal(CredentialType.Password, record.CredentialType);
	}

	[Fact]
	public void Update_WithCredentialType_TypeIsPreserved()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("Site", null, MakeSecure("pw"));
		var id = sut.GetAll()[0].Id;

		sut.Update(id, "Site", null, newPassword: null, credentialType: CredentialType.ApiKey);

		var updated = repo.GetById(id)!;
		Assert.Equal(CredentialType.Password, updated.CredentialType);
		Assert.Equal(CredentialType.ApiKey, sut.GetAll()[0].CredentialType);
	}
}
