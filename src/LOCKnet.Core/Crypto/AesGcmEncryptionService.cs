using System.Security.Cryptography;

namespace LOCKnet.Core.Crypto;

/// <summary>
/// AES-256-GCM-Implementierung von <see cref="IEncryptionService"/>.
/// 
/// Paket-Format: <c>[Nonce (12 Bytes)][Tag (16 Bytes)][Ciphertext (n Bytes)]</c>
/// </summary>
public sealed class AesGcmEncryptionService : IEncryptionService
{
    private const int KeyBytes = 32;      // AES-256
    private const int NonceBytes = 12;    // GCM-Standard-Nonce
    private const int TagBytes = 16;      // GCM-Authentifizierungs-Tag
    private const int HeaderBytes = NonceBytes + TagBytes; // 28 Bytes Overhead

    /// <inheritdoc/>
    public byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeyBytes)
            throw new ArgumentException($"Key muss genau {KeyBytes} Bytes lang sein.", nameof(key));

        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var tag = new byte[TagBytes];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Paket zusammensetzen: Nonce | Tag | Ciphertext
        var packet = new byte[HeaderBytes + ciphertext.Length];
        nonce.CopyTo(packet, 0);
        tag.CopyTo(packet, NonceBytes);
        ciphertext.CopyTo(packet, HeaderBytes);

        return packet;
    }

    /// <inheritdoc/>
    public byte[] Decrypt(byte[] cipherPacket, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(cipherPacket);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeyBytes)
            throw new ArgumentException($"Key muss genau {KeyBytes} Bytes lang sein.", nameof(key));

        if (cipherPacket.Length < HeaderBytes)
            throw new ArgumentException(
                $"Paket zu kurz — mindestens {HeaderBytes} Bytes erwartet.", nameof(cipherPacket));

        // Paket zerlegen
        var nonce = cipherPacket[..NonceBytes];
        var tag = cipherPacket[NonceBytes..HeaderBytes];
        var ciphertext = cipherPacket[HeaderBytes..];

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagBytes);
        // Wirft CryptographicException bei ungültigem Tag (Manipulation / falscher Key)
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }
}
