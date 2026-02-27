using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace LOCKnet.Core.Crypto;

/// <summary>
/// Implementierung von <see cref="ISecureStringService"/>.
/// Alle Konvertierungen pinnen den Speicher so kurz wie möglich.
/// </summary>
public sealed class SecureStringService : ISecureStringService
{
	/// <inheritdoc/>
	public byte[] ToByteArray(SecureString secureString)
	{
		ArgumentNullException.ThrowIfNull(secureString);
		if (secureString.Length == 0)
			return [];

		// SecureString intern als UTF-16 — wir holen den Pointer
		// und konvertieren direkt, ohne einen verwalteten String zu erzeugen.
		var ptr = IntPtr.Zero;
		try
		{
			ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);

			// Länge in UTF-16-Zeichen ist secureString.Length
			var utf16 = new char[secureString.Length];
			Marshal.Copy(ptr, utf16, 0, utf16.Length);

			// UTF-16 → UTF-8 (was die Krypto-Layer erwartet)
			var bytes = Encoding.UTF8.GetBytes(utf16);

			// char-Array sofort überschreiben
			Array.Clear(utf16, 0, utf16.Length);

			return bytes;
		}
		finally
		{
			if (ptr != IntPtr.Zero)
				Marshal.ZeroFreeGlobalAllocUnicode(ptr);
		}
	}

	/// <inheritdoc/>
	public void ZeroMemory(byte[] data)
	{
		if (data is { Length: > 0 })
			CryptographicOperations.ZeroMemory(data);
	}

	/// <inheritdoc/>
	public SecureString FromByteArray(byte[] data)
	{
		ArgumentNullException.ThrowIfNull(data);

		var secure = new SecureString();
		try
		{
			var chars = Encoding.UTF8.GetChars(data);
			try
			{
				foreach (var c in chars)
					secure.AppendChar(c);
			}
			finally
			{
				Array.Clear(chars, 0, chars.Length);
			}

			secure.MakeReadOnly();
			return secure;
		}
		catch
		{
			secure.Dispose();
			throw;
		}
		finally
		{
			ZeroMemory(data);
		}
	}
}
