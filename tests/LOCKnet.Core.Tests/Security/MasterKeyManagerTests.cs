using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using System.Security;

namespace LOCKnet.Core.Tests.Security;

// ── Minimal in-memory fake für IMasterKeyRepository ───────────────────────────

sealed class InMemoryMasterKeyRepo : IMasterKeyRepository
{
	private VaultHeader? _stored;

	public void Create(VaultHeader header)
	{
		if (_stored is not null)
			throw new InvalidOperationException("Vault-Header existiert bereits.");
		_stored = header;
	}

	public VaultHeader? Get() => _stored;

	public void Update(VaultHeader header) => _stored = header;

	public void Delete() => _stored = null;
}

// ─────────────────────────────────────────────────────────────────────────────

public class MasterKeyManagerTests
{
	private static SecureString MakeSecure(string value)
	{
		var s = new SecureString();
		foreach (var c in value) s.AppendChar(c);
		s.MakeReadOnly();
		return s;
	}

	private static MasterKeyManager BuildSut(out InMemoryMasterKeyRepo repo)
	{
		repo = new InMemoryMasterKeyRepo();
		return new MasterKeyManager(
			new Pbkdf2KeyDerivationService(),
			repo,
			new AesGcmEncryptionService(),
			new SecureStringService());
	}

	// ── IsInitialized ─────────────────────────────────────────────────────────

	[Fact]
	public void IsInitialized_BeforeSetup_ReturnsFalse()
	{
		var sut = BuildSut(out _);
		Assert.False(sut.IsInitialized);
	}

	[Fact]
	public void IsInitialized_AfterInitialize_ReturnsTrue()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("secret"));
		Assert.True(sut.IsInitialized);
	}

	// ── Initialize ────────────────────────────────────────────────────────────

	[Fact]
	public void Initialize_PersistsVaultHeaderAndWrappedKey()
	{
		var sut = BuildSut(out var repo);
		sut.Initialize(MakeSecure("masterkey"));

		var record = repo.Get();
		Assert.NotNull(record);
		Assert.NotEmpty(record.Salt);
		Assert.Empty(record.LegacyPasswordHash);
		Assert.NotEmpty(record.WrappedVaultKey);
		Assert.Equal("PBKDF2-SHA256", record.KdfIdentifier);
	}

	[Fact]
	public void Unlock_LegacyHeaderMigration_ClearsLegacyPasswordHash()
	{
		var repo = new InMemoryMasterKeyRepo();
		var kdf = new Pbkdf2KeyDerivationService();
		var secure = new SecureStringService();
		var sut = new MasterKeyManager(kdf, repo, new AesGcmEncryptionService(), secure);
		var password = MakeSecure("legacy-password");
		var passwordBytes = secure.ToByteArray(password);
		var parameters = kdf.GetDefaultParameters();
		var salt = kdf.GenerateSalt(parameters.SaltLengthBytes);

		try
		{
			repo.Create(new VaultHeader
			{
				FormatVersion = 0,
				KdfIdentifier = kdf.Identifier,
				KdfParameters = parameters,
				Salt = salt,
				LegacyPasswordHash = kdf.ComputePasswordHash(passwordBytes, salt, parameters),
				WrappedVaultKey = [],
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow,
			});
		}
		finally
		{
			secure.ZeroMemory(passwordBytes);
		}

		var unlocked = sut.Unlock(password);
		var migrated = repo.Get()!;

		Assert.NotNull(unlocked);
		Assert.NotEmpty(migrated.WrappedVaultKey);
		Assert.Empty(migrated.LegacyPasswordHash);
	}

	[Fact]
	public void Initialize_CalledTwice_Throws()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("first"));

		Assert.Throws<InvalidOperationException>(() => sut.Initialize(MakeSecure("second")));
	}

	// ── Unlock ────────────────────────────────────────────────────────────────

	[Fact]
	public void Unlock_CorrectPassword_Returns32ByteKey()
	{
		var sut = BuildSut(out _);
		const string pw = "correct horse battery staple";
		sut.Initialize(MakeSecure(pw));

		var key = sut.Unlock(MakeSecure(pw));

		Assert.NotNull(key);
		Assert.Equal(32, key.Length);
	}

	[Fact]
	public void Unlock_WrongPassword_ReturnsNull()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("correct"));

		var key = sut.Unlock(MakeSecure("wrong"));

		Assert.Null(key);
	}

	[Fact]
	public void Unlock_BeforeInitialize_Throws()
	{
		var sut = BuildSut(out _);
		Assert.Throws<InvalidOperationException>(() => sut.Unlock(MakeSecure("anything")));
	}

	[Fact]
	public void Unlock_SamePasswordTwice_ReturnsSameKey()
	{
		var sut = BuildSut(out _);
		const string pw = "deterministic";
		sut.Initialize(MakeSecure(pw));

		var key1 = sut.Unlock(MakeSecure(pw))!;
		var key2 = sut.Unlock(MakeSecure(pw))!;

		Assert.Equal(key1, key2);
	}

	[Fact]
	public void Unlock_AfterInitialize_UsesWrappedVaultKeyRoundTrip()
	{
		var sut = BuildSut(out var repo);
		const string password = "wrapped-vault-key";
		sut.Initialize(MakeSecure(password));

		var header = repo.Get()!;
		Assert.NotEmpty(header.WrappedVaultKey);

		var key1 = sut.Unlock(MakeSecure(password))!;
		var key2 = sut.Unlock(MakeSecure(password))!;

		Assert.Equal(key1, key2);
	}

	// ── ChangePassword ────────────────────────────────────────────────────────

	[Fact]
	public void ChangePassword_CorrectCurrent_NewPasswordUnlocks()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("old"));
		sut.ChangePassword(MakeSecure("old"), MakeSecure("new"));

		var key = sut.Unlock(MakeSecure("new"));
		Assert.NotNull(key);
	}

	[Fact]
	public void ChangePassword_CorrectCurrent_OldPasswordNoLongerUnlocks()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("old"));
		sut.ChangePassword(MakeSecure("old"), MakeSecure("new"));

		var key = sut.Unlock(MakeSecure("old"));
		Assert.Null(key);
	}

	[Fact]
	public void ChangePassword_WrongCurrent_Throws()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("correct"));

		Assert.Throws<UnauthorizedAccessException>(
			() => sut.ChangePassword(MakeSecure("wrong"), MakeSecure("new")));
	}

	// ── ChangePassword edge cases ────────────────────────────────────────────

	[Fact]
	public void ChangePassword_BeforeInitialize_Throws()
	{
		var sut = BuildSut(out _);
		Assert.Throws<InvalidOperationException>(
			() => sut.ChangePassword(MakeSecure("old"), MakeSecure("new")));
	}

	[Fact]
	public void ChangePassword_GeneratesNewSalt()
	{
		var sut = BuildSut(out var repo);
		sut.Initialize(MakeSecure("initial"));
		var saltBefore = repo.Get()!.Salt;

		sut.ChangePassword(MakeSecure("initial"), MakeSecure("updated"));

		var saltAfter = repo.Get()!.Salt;
		Assert.False(saltBefore.SequenceEqual(saltAfter),
			"ChangePassword must generate a fresh salt.");
	}

	[Fact]
	public void ChangePassword_RewrapsExistingVaultKey()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("old-password"));

		var originalKey = sut.Unlock(MakeSecure("old-password"))!;
		sut.ChangePassword(MakeSecure("old-password"), MakeSecure("new-password"));
		var unlockedWithNewPassword = sut.Unlock(MakeSecure("new-password"))!;

		Assert.Equal(originalKey, unlockedWithNewPassword);
	}

	// ── Initialize null guard ───────────────────────────────────────────────

	[Fact]
	public void Initialize_NullPassword_Throws()
	{
		var sut = BuildSut(out _);
		Assert.Throws<ArgumentNullException>(() => sut.Initialize(null!));
	}

	// ── Unlock null guard ────────────────────────────────────────────────────

	[Fact]
	public void Unlock_NullPassword_Throws()
	{
		var sut = BuildSut(out _);
		sut.Initialize(MakeSecure("pw"));
		Assert.Throws<ArgumentNullException>(() => sut.Unlock(null!));
	}
}
