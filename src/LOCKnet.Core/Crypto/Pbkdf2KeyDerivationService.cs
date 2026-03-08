using LOCKnet.Core.DataAbstractions;
using System.Security.Cryptography;

namespace LOCKnet.Core.Crypto;

/// <summary>
/// PBKDF2-Implementierung von <see cref="IKeyDerivationService"/>.
/// Nutzt HMAC-SHA256, 600.000 Iterationen (OWASP-Empfehlung 2023).
/// </summary>
public sealed class Pbkdf2KeyDerivationService : IKeyDerivationService
{
	private const string KdfIdentifier = "PBKDF2-SHA256";
	/// <summary>Anzahl der PBKDF2-Iterationen. OWASP empfiehlt ≥600.000 für HMAC-SHA256.</summary>
	private const int Iterations = 600_000;

	/// <summary>Ausgabelänge des abgeleiteten Schlüssels in Bytes (256 Bit für AES-256).</summary>
	private const int KeyLengthBytes = 32;

	/// <summary>Ausgabelänge des Passwort-Hashes in Bytes.</summary>
	private const int HashLengthBytes = 32;

	/// <inheritdoc/>
	public string Identifier => KdfIdentifier;

	/// <inheritdoc/>
	public VaultKdfParameters GetDefaultParameters() => new()
	{
		HashAlgorithm = "SHA256",
		Iterations = Iterations,
		KeyLengthBytes = KeyLengthBytes,
		SaltLengthBytes = 32,
	};

	/// <inheritdoc/>
	public void ValidateParameters(VaultKdfParameters parameters)
		=> ValidateParametersCore(parameters);

	/// <inheritdoc/>
	public byte[] GenerateSalt(int length = 32)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(length, 16, nameof(length));
		return RandomNumberGenerator.GetBytes(length);
	}

	/// <inheritdoc/>
	public byte[] DeriveKey(byte[] password, byte[] salt)
		=> DeriveKey(password, salt, GetDefaultParameters());

	/// <inheritdoc/>
	public byte[] DeriveKey(byte[] password, byte[] salt, VaultKdfParameters parameters)
	{
		ArgumentNullException.ThrowIfNull(password);
		ArgumentNullException.ThrowIfNull(salt);
		ArgumentNullException.ThrowIfNull(parameters);
		ValidateParametersCore(parameters);

		return Rfc2898DeriveBytes.Pbkdf2(
			password,
			salt,
			parameters.Iterations,
			ResolveHashAlgorithm(parameters.HashAlgorithm),
			parameters.KeyLengthBytes);
	}

	/// <inheritdoc/>
	public byte[] ComputePasswordHash(byte[] password, byte[] salt)
		=> ComputePasswordHash(password, salt, GetDefaultParameters());

	/// <inheritdoc/>
	public byte[] ComputePasswordHash(byte[] password, byte[] salt, VaultKdfParameters parameters)
	{
		ArgumentNullException.ThrowIfNull(password);
		ArgumentNullException.ThrowIfNull(salt);
		ArgumentNullException.ThrowIfNull(parameters);
		ValidateParametersCore(parameters);

		// Separater Durchlauf mit anderem Kontext-Byte, damit
		// DeriveKey-Ausgabe und PasswordHash nie identisch sind.
		var saltWithContext = new byte[salt.Length + 1];
		salt.CopyTo(saltWithContext, 0);
		saltWithContext[^1] = 0x01; // Kontext: "password verification"

		return Rfc2898DeriveBytes.Pbkdf2(
			password,
			saltWithContext,
			parameters.Iterations,
			ResolveHashAlgorithm(parameters.HashAlgorithm),
			HashLengthBytes);
	}

	/// <inheritdoc/>
	public bool VerifyPassword(byte[] password, byte[] salt, byte[] storedHash)
		=> VerifyPassword(password, salt, storedHash, GetDefaultParameters());

	/// <inheritdoc/>
	public bool VerifyPassword(byte[] password, byte[] salt, byte[] storedHash, VaultKdfParameters parameters)
	{
		ArgumentNullException.ThrowIfNull(password);
		ArgumentNullException.ThrowIfNull(salt);
		ArgumentNullException.ThrowIfNull(storedHash);

		var computed = ComputePasswordHash(password, salt, parameters);
		return CryptographicOperations.FixedTimeEquals(computed, storedHash);
	}

	private static HashAlgorithmName ResolveHashAlgorithm(string algorithm)
		=> algorithm.ToUpperInvariant() switch
		{
			"SHA256" => HashAlgorithmName.SHA256,
			_ => throw new NotSupportedException($"Nicht unterstuetzter PBKDF2-Hash-Algorithmus: {algorithm}"),
		};

	private static void ValidateParametersCore(VaultKdfParameters parameters)
	{
		if (parameters.Iterations is < 100_000 or > 5_000_000)
			throw new InvalidOperationException("Persistierte PBKDF2-Iterationen liegen ausserhalb des erlaubten Bereichs.");

		if (parameters.KeyLengthBytes != KeyLengthBytes)
			throw new InvalidOperationException($"Persistierte PBKDF2-Schluessellaenge muss {KeyLengthBytes} Bytes betragen.");

		if (parameters.SaltLengthBytes is < 16 or > 64)
			throw new InvalidOperationException("Persistierte Salt-Laenge liegt ausserhalb des erlaubten Bereichs.");

		_ = ResolveHashAlgorithm(parameters.HashAlgorithm);
	}
}
