using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using System.Security;

namespace LOCKnet.Core.Tests.Security;

// ── Minimal in-memory fake für IMasterKeyRepository ───────────────────────────

sealed class InMemoryMasterKeyRepo : IMasterKeyRepository
{
	private MasterKeyRecord? _stored;

	public void Create(MasterKeyRecord key)
	{
		if (_stored is not null)
			throw new InvalidOperationException("Master-Key existiert bereits.");
		_stored = key;
	}

	public MasterKeyRecord? Get() => _stored;

	public void Update(MasterKeyRecord key) => _stored = key;

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
	public void Initialize_PersistsSaltAndHash()
	{
		var sut = BuildSut(out var repo);
		sut.Initialize(MakeSecure("masterkey"));

		var record = repo.Get();
		Assert.NotNull(record);
		Assert.NotEmpty(record.Salt);
		Assert.NotEmpty(record.PasswordHash);
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
