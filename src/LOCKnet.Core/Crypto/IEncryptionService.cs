namespace LOCKnet.Core.Crypto;

/// <summary>
/// Verschlüsselt und entschlüsselt Daten mit AES-256-GCM.
/// GCM bietet authenticated encryption — Manipulation der Ciphertext
/// wird beim Entschlüsseln erkannt und als Exception gemeldet.
/// </summary>
public interface IEncryptionService
{
    /// <summary>
    /// Verschlüsselt Klartext-Bytes mit dem angegebenen Schlüssel.
    /// Generiert intern einen zufälligen Nonce (IV).
    /// </summary>
    /// <param name="plaintext">Die zu verschlüsselnden Bytes.</param>
    /// <param name="key">AES-256-Schlüssel (genau 32 Bytes).</param>
    /// <returns>
    /// Verschlüsseltes Paket: <c>[Nonce (12 Bytes)][Tag (16 Bytes)][Ciphertext]</c>.
    /// Das Format ist selbstbeschreibend — kein separates Speichern von Nonce/Tag nötig.
    /// </returns>
    /// <exception cref="ArgumentException">Key ist nicht 32 Bytes.</exception>
    byte[] Encrypt(byte[] plaintext, byte[] key);

    /// <summary>
    /// Entschlüsselt ein verschlüsseltes Paket aus <see cref="Encrypt"/>.
    /// Verifiziert den GCM-Authentifizierungs-Tag — wirft Exception bei Manipulation.
    /// </summary>
    /// <param name="cipherPacket">Das Paket aus <see cref="Encrypt"/>.</param>
    /// <param name="key">AES-256-Schlüssel (genau 32 Bytes).</param>
    /// <returns>Die entschlüsselten Klartext-Bytes.</returns>
    /// <exception cref="ArgumentException">Key ist nicht 32 Bytes oder Paket zu kurz.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Authentifizierung fehlgeschlagen — Daten wurden manipuliert oder falscher Key.
    /// </exception>
    byte[] Decrypt(byte[] cipherPacket, byte[] key);
}
