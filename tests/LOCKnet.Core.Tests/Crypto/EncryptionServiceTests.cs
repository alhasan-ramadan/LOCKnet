using LOCKnet.Core.Crypto;
using System.Security.Cryptography;

namespace LOCKnet.Core.Tests.Crypto;

public class EncryptionServiceTests
{
	private readonly IEncryptionService _sut = new AesGcmEncryptionService();

	private static byte[] MakeKey() => RandomNumberGenerator.GetBytes(32);

	// ── Round-trip ────────────────────────────────────────────────────────────

	[Fact]
	public void Encrypt_ThenDecrypt_ReturnsOriginalPlaintext()
	{
		var plaintext = "Hello, LOCKnet!"u8.ToArray();
		var key = MakeKey();

		var packet = _sut.Encrypt(plaintext, key);
		var recovered = _sut.Decrypt(packet, key);

		Assert.Equal(plaintext, recovered);
	}

	[Fact]
	public void Encrypt_EmptyPlaintext_RoundtripSucceeds()
	{
		var key = MakeKey();

		var packet = _sut.Encrypt([], key);
		var recovered = _sut.Decrypt(packet, key);

		Assert.Empty(recovered);
	}

	[Fact]
	public void Encrypt_LargeData_RoundtripSucceeds()
	{
		var plaintext = RandomNumberGenerator.GetBytes(1024 * 64);
		var key = MakeKey();

		var packet = _sut.Encrypt(plaintext, key);
		var recovered = _sut.Decrypt(packet, key);

		Assert.Equal(plaintext, recovered);
	}

	// ── Nonce uniqueness ──────────────────────────────────────────────────────

	[Fact]
	public void Encrypt_SamePlaintextTwice_ProducesDifferentPackets()
	{
		var plaintext = "secret"u8.ToArray();
		var key = MakeKey();

		var packet1 = _sut.Encrypt(plaintext, key);
		var packet2 = _sut.Encrypt(plaintext, key);

		Assert.False(packet1.SequenceEqual(packet2), "Each Encrypt call must use a fresh random nonce.");
	}

	// ── Packet structure ──────────────────────────────────────────────────────

	[Fact]
	public void Encrypt_PacketLength_IsNoncePlusTagPlusCiphertext()
	{
		const int nonceBytes = 12;
		const int tagBytes = 16;
		var plaintext = "test data"u8.ToArray();
		var key = MakeKey();

		var packet = _sut.Encrypt(plaintext, key);

		Assert.Equal(nonceBytes + tagBytes + plaintext.Length, packet.Length);
	}

	// ── Tamper detection (GCM authentication tag) ─────────────────────────────

	[Fact]
	public void Decrypt_TamperedCiphertext_Throws()
	{
		var plaintext = "sensitive data"u8.ToArray();
		var key = MakeKey();
		var packet = _sut.Encrypt(plaintext, key);

		// Flip a bit in the ciphertext area (after nonce + tag)
		packet[28] ^= 0xFF;

		Assert.Throws<AuthenticationTagMismatchException>(() => _sut.Decrypt(packet, key));
	}

	[Fact]
	public void Decrypt_TamperedTag_Throws()
	{
		var plaintext = "sensitive data"u8.ToArray();
		var key = MakeKey();
		var packet = _sut.Encrypt(plaintext, key);

		// Flip a bit in the tag area (bytes 12–27)
		packet[12] ^= 0xFF;

		Assert.Throws<AuthenticationTagMismatchException>(() => _sut.Decrypt(packet, key));
	}

	[Fact]
	public void Decrypt_TamperedNonce_Throws()
	{
		var plaintext = "sensitive data"u8.ToArray();
		var key = MakeKey();
		var packet = _sut.Encrypt(plaintext, key);

		// Flip a bit in the nonce area (bytes 0–11)
		packet[0] ^= 0xFF;

		Assert.Throws<AuthenticationTagMismatchException>(() => _sut.Decrypt(packet, key));
	}

	// ── Wrong key ─────────────────────────────────────────────────────────────

	[Fact]
	public void Decrypt_WrongKey_Throws()
	{
		var plaintext = "secret message"u8.ToArray();
		var key = MakeKey();
		var wrongKey = MakeKey();

		var packet = _sut.Encrypt(plaintext, key);

		Assert.Throws<AuthenticationTagMismatchException>(() => _sut.Decrypt(packet, wrongKey));
	}

	// ── Argument validation ───────────────────────────────────────────────────

	[Fact]
	public void Encrypt_ShortKey_Throws()
	{
		Assert.Throws<ArgumentException>(() => _sut.Encrypt("data"u8.ToArray(), new byte[16]));
	}

	[Fact]
	public void Decrypt_PacketTooShort_Throws()
	{
		Assert.Throws<ArgumentException>(() => _sut.Decrypt(new byte[10], MakeKey()));
	}

	[Fact]
	public void Encrypt_NullPlaintext_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => _sut.Encrypt(null!, MakeKey()));
	}

	[Fact]
	public void Decrypt_NullPacket_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => _sut.Decrypt(null!, MakeKey()));
	}
}
