using System.Security.Cryptography;

namespace LOCKnet.Core.Crypto;

/// <summary>
/// PBKDF2-Implementierung von <see cref="IKeyDerivationService"/>.
/// Nutzt HMAC-SHA256, 600.000 Iterationen (OWASP-Empfehlung 2023).
/// </summary>
public sealed class Pbkdf2KeyDerivationService : IKeyDerivationService
{
    /// <summary>Anzahl der PBKDF2-Iterationen. OWASP empfiehlt ≥600.000 für HMAC-SHA256.</summary>
    private const int Iterations = 600_000;

    /// <summary>Ausgabelänge des abgeleiteten Schlüssels in Bytes (256 Bit für AES-256).</summary>
    private const int KeyLengthBytes = 32;

    /// <summary>Ausgabelänge des Passwort-Hashes in Bytes.</summary>
    private const int HashLengthBytes = 32;

    /// <inheritdoc/>
    public byte[] GenerateSalt(int length = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 16, nameof(length));
        return RandomNumberGenerator.GetBytes(length);
    }

    /// <inheritdoc/>
    public byte[] DeriveKey(byte[] password, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeyLengthBytes);
    }

    /// <inheritdoc/>
    public byte[] ComputePasswordHash(byte[] password, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        // Separater Durchlauf mit anderem Kontext-Byte, damit
        // DeriveKey-Ausgabe und PasswordHash nie identisch sind.
        var saltWithContext = new byte[salt.Length + 1];
        salt.CopyTo(saltWithContext, 0);
        saltWithContext[^1] = 0x01; // Kontext: "password verification"

        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltWithContext,
            Iterations,
            HashAlgorithmName.SHA256,
            HashLengthBytes);
    }

    /// <inheritdoc/>
    public bool VerifyPassword(byte[] password, byte[] salt, byte[] storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(storedHash);

        var computed = ComputePasswordHash(password, salt);
        return CryptographicOperations.FixedTimeEquals(computed, storedHash);
    }
}
