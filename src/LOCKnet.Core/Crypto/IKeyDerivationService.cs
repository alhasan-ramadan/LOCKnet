using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.Core.Crypto;

/// <summary>
/// Leitet kryptografische Schlüssel aus dem Master-Passwort ab.
/// Verantwortlich für Salt-Generierung, Schlüsselableitung und
/// Passwort-Hash-Verifikation.
/// </summary>
public interface IKeyDerivationService
{
	/// <summary>Eindeutiger Bezeichner der KDF-Implementierung.</summary>
	string Identifier { get; }

	/// <summary>Liefert die Standardparameter fuer neue Vault-Header.</summary>
	VaultKdfParameters GetDefaultParameters();

	/// <summary>
	/// Generiert einen kryptografisch sicheren zufälligen Salt.
	/// </summary>
	/// <param name="length">Länge des Salts in Bytes. Standard: 32.</param>
	byte[] GenerateSalt(int length = 32);

	/// <summary>
	/// Leitet einen AES-256-Schlüssel (32 Bytes) aus dem Master-Passwort ab.
	/// </summary>
	/// <param name="password">Das Master-Passwort als Byte-Array.</param>
	/// <param name="salt">Der Salt aus <see cref="GenerateSalt"/>.</param>
	/// <returns>32-Byte AES-Schlüssel.</returns>
	byte[] DeriveKey(byte[] password, byte[] salt);

	/// <summary>
	/// Leitet einen AES-256-Schluessel mit expliziten KDF-Parametern ab.
	/// </summary>
	/// <param name="password">Das Master-Passwort als Byte-Array.</param>
	/// <param name="salt">Der persistierte Salt.</param>
	/// <param name="parameters">Die zu verwendenden KDF-Parameter.</param>
	byte[] DeriveKey(byte[] password, byte[] salt, VaultKdfParameters parameters);

	/// <summary>
	/// Erstellt einen gespeicherten Passwort-Hash für die Datenbank.
	/// Dieser Hash dient zur späteren Verifikation, nicht zur Schlüsselableitung.
	/// </summary>
	/// <param name="password">Das Master-Passwort als Byte-Array.</param>
	/// <param name="salt">Der Salt aus <see cref="GenerateSalt"/>.</param>
	/// <returns>Hash-Bytes zum Speichern fuer Kompatibilitaets- und Migrationszwecke.</returns>
	byte[] ComputePasswordHash(byte[] password, byte[] salt);

	/// <summary>
	/// Erstellt einen gespeicherten Passwort-Hash mit expliziten KDF-Parametern.
	/// </summary>
	byte[] ComputePasswordHash(byte[] password, byte[] salt, VaultKdfParameters parameters);

	/// <summary>
	/// Prüft, ob das eingegebene Passwort zum gespeicherten Hash passt.
	/// Läuft in konstanter Zeit (timing-safe).
	/// </summary>
	/// <param name="password">Das zu prüfende Passwort als Byte-Array.</param>
	/// <param name="salt">Der gespeicherte Salt.</param>
	/// <param name="storedHash">Der gespeicherte Hash.</param>
	bool VerifyPassword(byte[] password, byte[] salt, byte[] storedHash);

	/// <summary>
	/// Prueft einen gespeicherten Passwort-Hash mit expliziten KDF-Parametern.
	/// </summary>
	bool VerifyPassword(byte[] password, byte[] salt, byte[] storedHash, VaultKdfParameters parameters);
}
