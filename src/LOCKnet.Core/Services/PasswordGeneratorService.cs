using System.Security.Cryptography;
using System.Text;

namespace LOCKnet.Core.Services;

/// <summary>
/// Standardimplementierung fuer <see cref="IPasswordGeneratorService"/>.
/// </summary>
public sealed class PasswordGeneratorService : IPasswordGeneratorService
{
	private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
	private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
	private const string DigitChars = "0123456789";
	private const string SpecialChars = "!@#$%^&*()-_=+[]{}|;:,.<>?/";

	/// <inheritdoc/>
	public string Generate(PasswordGeneratorOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var length = options.Length > 0 ? options.Length : 16;
		var charsetBuilder = new StringBuilder();

		if (options.UseUppercase)
		{
			charsetBuilder.Append(UppercaseChars);
		}

		if (options.UseLowercase)
		{
			charsetBuilder.Append(LowercaseChars);
		}

		if (options.UseDigits)
		{
			charsetBuilder.Append(DigitChars);
		}

		if (options.UseSpecial)
		{
			charsetBuilder.Append(SpecialChars);
		}

		if (charsetBuilder.Length == 0)
		{
			charsetBuilder.Append(LowercaseChars);
		}

		var charset = charsetBuilder.ToString();
		var randomBytes = RandomNumberGenerator.GetBytes(length);
		var password = new char[length];

		for (var i = 0; i < length; i++)
		{
			password[i] = charset[randomBytes[i] % charset.Length];
		}

		return new string(password);
	}
}
