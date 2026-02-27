namespace LOCKnet.Core.Services;

/// <summary>
/// Bewertet die Staerke eines Passworts und liefert Score sowie Darstellungsmetadaten.
/// </summary>
public interface IPasswordStrengthService
{
	/// <summary>
	/// Berechnet die Staerke fuer das uebergebene Passwort.
	/// </summary>
	/// <param name="password">Das zu bewertende Passwort.</param>
	/// <returns>Staerkeobjekt mit Score, Label und Farbcode.</returns>
	PasswordStrength Evaluate(string password);
}

/// <summary>
/// Ergebnis einer Passwortstaerkeanalyse.
/// </summary>
/// <param name="Score">Staerkescore im Bereich 0 bis 4.</param>
/// <param name="Label">Menschlich lesbares Staerke-Label.</param>
/// <param name="Color">Hex-Farbwert fuer die UI-Darstellung.</param>
public record PasswordStrength(int Score, string Label, string Color);
