using System.Security;

namespace LOCKnet.Core.Crypto;

/// <summary>
/// Kapselt den sicheren Umgang mit <see cref="SecureString"/>.
/// Konvertierungen in Byte-Arrays müssen nach Verwendung explizit gelöscht werden.
/// </summary>
public interface ISecureStringService
{
	/// <summary>
	/// Konvertiert einen <see cref="SecureString"/> in ein Byte-Array (UTF-8).
	/// Das zurückgegebene Array muss nach Verwendung mit <see cref="ZeroMemory"/> gelöscht werden.
	/// </summary>
	/// <param name="secureString">Der zu konvertierende SecureString.</param>
	/// <returns>UTF-8-Bytes des Passworts. Caller ist für das Nullen verantwortlich.</returns>
	byte[] ToByteArray(SecureString secureString);

	/// <summary>
	/// Überschreibt alle Bytes im Array mit Nullen.
	/// Sollte in einem <c>finally</c>-Block aufgerufen werden.
	/// </summary>
	/// <param name="data">Das zu löschende Array.</param>
	void ZeroMemory(byte[] data);

	/// <summary>
	/// Erstellt einen <see cref="SecureString"/> aus einem Byte-Array.
	/// Das übergebene Array wird danach automatisch genullt.
	/// </summary>
	/// <param name="data">UTF-8-Bytes des Passworts. Werden nach Konvertierung genullt.</param>
	SecureString FromByteArray(byte[] data);
}
