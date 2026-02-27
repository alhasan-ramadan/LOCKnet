namespace LOCKnet.Core.Services;

/// <summary>
/// Erzeugt kryptographisch sichere Passwoerter auf Basis konfigurierbarer Optionen.
/// </summary>
public interface IPasswordGeneratorService
{
	/// <summary>
	/// Generiert ein neues Passwort auf Basis von <paramref name="options"/>.
	/// </summary>
	/// <param name="options">Optionen fuer Laenge und erlaubte Zeichengruppen.</param>
	/// <returns>Ein zufaellig erzeugtes Passwort.</returns>
	string Generate(PasswordGeneratorOptions options);
}

/// <summary>
/// Optionen fuer die Passwortgenerierung.
/// </summary>
public record PasswordGeneratorOptions
{
	/// <summary>Gewuenschte Passwortlaenge.</summary>
	public int Length { get; init; } = 16;

	/// <summary>Grossbuchstaben verwenden.</summary>
	public bool UseUppercase { get; init; } = true;

	/// <summary>Kleinbuchstaben verwenden.</summary>
	public bool UseLowercase { get; init; } = true;

	/// <summary>Ziffern verwenden.</summary>
	public bool UseDigits { get; init; } = true;

	/// <summary>Sonderzeichen verwenden.</summary>
	public bool UseSpecial { get; init; } = true;
}
