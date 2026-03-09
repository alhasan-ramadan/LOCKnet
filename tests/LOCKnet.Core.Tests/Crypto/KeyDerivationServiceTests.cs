using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.Core.Tests.Crypto;

public class KeyDerivationServiceTests
{
	private readonly IKeyDerivationService _sut = new Pbkdf2KeyDerivationService();

	// ── GenerateSalt ──────────────────────────────────────────────────────────

	[Fact]
	public void GenerateSalt_DefaultLength_Returns32Bytes()
	{
		var salt = _sut.GenerateSalt();

		Assert.Equal(32, salt.Length);
	}

	[Theory]
	[InlineData(16)]
	[InlineData(32)]
	[InlineData(64)]
	public void GenerateSalt_SpecifiedLength_ReturnsCorrectLength(int length)
	{
		var salt = _sut.GenerateSalt(length);

		Assert.Equal(length, salt.Length);
	}

	[Fact]
	public void GenerateSalt_CalledTwice_ReturnsDifferentValues()
	{
		var salt1 = _sut.GenerateSalt();
		var salt2 = _sut.GenerateSalt();

		Assert.False(salt1.SequenceEqual(salt2), "Two independently generated salts should differ.");
	}

	[Fact]
	public void GenerateSalt_TooShort_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => _sut.GenerateSalt(15));
	}

	// ── DeriveKey ─────────────────────────────────────────────────────────────

	[Fact]
	public void DeriveKey_Returns32Bytes()
	{
		var password = "hunter2"u8.ToArray();
		var salt = _sut.GenerateSalt();

		var key = _sut.DeriveKey(password, salt);

		Assert.Equal(32, key.Length);
	}

	[Fact]
	public void DeriveKey_SameInputProducesSameKey()
	{
		var password = "samepassword"u8.ToArray();
		var salt = _sut.GenerateSalt();

		var key1 = _sut.DeriveKey(password, salt);
		var key2 = _sut.DeriveKey(password, salt);

		Assert.True(key1.SequenceEqual(key2));
	}

	[Fact]
	public void DeriveKey_DifferentSaltProducesDifferentKey()
	{
		var password = "samepassword"u8.ToArray();
		var salt1 = _sut.GenerateSalt();
		var salt2 = _sut.GenerateSalt();

		var key1 = _sut.DeriveKey(password, salt1);
		var key2 = _sut.DeriveKey(password, salt2);

		Assert.False(key1.SequenceEqual(key2));
	}

	[Fact]
	public void DeriveKey_DifferentPasswordProducesDifferentKey()
	{
		var salt = _sut.GenerateSalt();

		var key1 = _sut.DeriveKey("password1"u8.ToArray(), salt);
		var key2 = _sut.DeriveKey("password2"u8.ToArray(), salt);

		Assert.False(key1.SequenceEqual(key2));
	}

	[Fact]
	public void DeriveKey_InvalidPersistedParameters_Throws()
	{
		var password = "hunter2"u8.ToArray();
		var salt = _sut.GenerateSalt();
		var parameters = new VaultKdfParameters
		{
			HashAlgorithm = "SHA256",
			Iterations = 10,
			KeyLengthBytes = 32,
			SaltLengthBytes = 32,
		};

		Assert.Throws<InvalidOperationException>(() => _sut.DeriveKey(password, salt, parameters));
	}

	// ── ComputePasswordHash ───────────────────────────────────────────────────

	[Fact]
	public void ComputePasswordHash_IsDifferentFromDeriveKey()
	{
		var password = "masterkey"u8.ToArray();
		var salt = _sut.GenerateSalt();

		var encKey = _sut.DeriveKey(password, salt);
		var pwHash = _sut.ComputePasswordHash(password, salt);

		Assert.False(encKey.SequenceEqual(pwHash),
			"DeriveKey and ComputePasswordHash must never produce the same bytes.");
	}

	[Fact]
	public void ComputePasswordHash_SameInputProducesSameHash()
	{
		var password = "masterkey"u8.ToArray();
		var salt = _sut.GenerateSalt();

		var hash1 = _sut.ComputePasswordHash(password, salt);
		var hash2 = _sut.ComputePasswordHash(password, salt);

		Assert.True(hash1.SequenceEqual(hash2));
	}

	// ── VerifyPassword ────────────────────────────────────────────────────────

	[Fact]
	public void VerifyPassword_CorrectPassword_ReturnsTrue()
	{
		var password = "correct horse battery staple"u8.ToArray();
		var salt = _sut.GenerateSalt();
		var hash = _sut.ComputePasswordHash(password, salt);

		Assert.True(_sut.VerifyPassword(password, salt, hash));
	}

	[Fact]
	public void VerifyPassword_WrongPassword_ReturnsFalse()
	{
		var salt = _sut.GenerateSalt();
		var hash = _sut.ComputePasswordHash("correct"u8.ToArray(), salt);

		Assert.False(_sut.VerifyPassword("wrong"u8.ToArray(), salt, hash));
	}

	[Fact]
	public void VerifyPassword_WrongSalt_ReturnsFalse()
	{
		var password = "secret"u8.ToArray();
		var salt1 = _sut.GenerateSalt();
		var salt2 = _sut.GenerateSalt();
		var hash = _sut.ComputePasswordHash(password, salt1);

		Assert.False(_sut.VerifyPassword(password, salt2, hash));
	}
}
