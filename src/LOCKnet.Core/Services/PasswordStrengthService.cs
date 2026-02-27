using System.Text.RegularExpressions;

namespace LOCKnet.Core.Services;

/// <summary>
/// Standardimplementierung fuer <see cref="IPasswordStrengthService"/>.
/// </summary>
public sealed partial class PasswordStrengthService : IPasswordStrengthService
{
	private const string LabelVeryWeak = "Sehr schwach";
	private const string LabelWeak = "Schwach";
	private const string LabelMedium = "Mittel";
	private const string LabelStrong = "Stark";
	private const string LabelVeryStrong = "Sehr stark";

	/// <inheritdoc/>
	public PasswordStrength Evaluate(string password)
	{
		if (string.IsNullOrEmpty(password))
		{
			return new PasswordStrength(0, LabelVeryWeak, "#FF4757");
		}

		var hasUpper = password.Any(char.IsUpper);
		var hasLower = password.Any(char.IsLower);
		var hasDigit = password.Any(char.IsDigit);
		var hasSpecial = SpecialCharacterRegex().IsMatch(password);
		var variety = new[] { hasUpper, hasLower, hasDigit, hasSpecial }.Count(v => v);

		var points = 0;
		if (password.Length >= 8)
		{
			points++;
		}

		if (password.Length >= 12)
		{
			points++;
		}

		if (hasUpper)
		{
			points++;
		}

		if (hasLower)
		{
			points++;
		}

		if (hasDigit)
		{
			points++;
		}

		if (hasSpecial)
		{
			points++;
		}

		if (variety >= 3)
		{
			points++;
		}

		if (variety == 4)
		{
			points++;
		}

		var score = points switch
		{
			<= 1 => 0,
			<= 3 => 1,
			<= 5 => 2,
			<= 7 => 3,
			_ => 4
		};

		return score switch
		{
			0 => new PasswordStrength(0, LabelVeryWeak, "#FF4757"),
			1 => new PasswordStrength(1, LabelWeak, "#FF6B35"),
			2 => new PasswordStrength(2, LabelMedium, "#FFB347"),
			3 => new PasswordStrength(3, LabelStrong, "#2ED573B3"),
			_ => new PasswordStrength(4, LabelVeryStrong, "#2ED573")
		};
	}

	[GeneratedRegex("[^a-zA-Z0-9]")]
	private static partial Regex SpecialCharacterRegex();
}
