namespace LOCKnet.Core.Services;

/// <summary>
/// Parser fuer typische Copy/Paste-Formate von Recovery-/Backup-Codes.
/// </summary>
public static class BackupCodeParser
{
	/// <summary>
	/// Zerlegt Rohtext in einzelne Codes.
	/// Unterstuetzt Zeilenumbrueche sowie komma-/semikolongetrennte Listen.
	/// </summary>
	/// <param name="raw">Rohtext aus Eingabe oder Zwischenablage.</param>
	/// <returns>Normalisierte Liste einzelner Codes in Einfuegereihenfolge ohne Duplikate.</returns>
	public static IReadOnlyList<string> Parse(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return [];

		var result = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace('\r', '\n')
			.Split('\n', StringSplitOptions.None);

		foreach (var line in lines)
		{
			if (string.IsNullOrWhiteSpace(line))
				continue;

			var parts = line.Contains(',') || line.Contains(';')
				? line.Split([',', ';'], StringSplitOptions.None)
				: [line];

			foreach (var part in parts)
			{
				var normalized = Normalize(part);
				if (normalized.Length == 0)
					continue;

				if (seen.Add(normalized))
					result.Add(normalized);
			}
		}

		return result;
	}

	private static string Normalize(string value)
	{
		var trimmed = value.Trim();
		if (trimmed.Length == 0)
			return string.Empty;

		return string.Join(' ', trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
	}
}
