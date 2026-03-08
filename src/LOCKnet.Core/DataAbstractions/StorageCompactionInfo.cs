namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Beschreibt den aktuellen Zustand der Storage-Kompaktierung fuer den Vault.
/// </summary>
public sealed class StorageCompactionInfo
{
	/// <summary>Gibt an, ob fuer den Vault noch eine Kompaktierung aussteht.</summary>
	public bool IsPending { get; init; }

	/// <summary>Gibt an, ob ein automatischer Retry aktuell zurueckgestellt ist.</summary>
	public bool AutoRetryDeferred { get; init; }

	/// <summary>UTC-Zeitpunkt des letzten Kompaktierungsversuchs, sofern vorhanden.</summary>
	public DateTime? LastAttemptUtc { get; init; }

	/// <summary>Fruehester UTC-Zeitpunkt fuer den naechsten automatischen Retry.</summary>
	public DateTime? NextAutomaticRetryUtc { get; init; }

	/// <summary>Klassifizierter Fehler des letzten Kompaktierungsversuchs.</summary>
	public StorageCompactionFailureKind FailureKind { get; init; }

	/// <summary>Kompakte, benutzergeeignete Statusmeldung fuer die UI.</summary>
	public string UserMessage { get; init; } = string.Empty;

	/// <summary>Technische Kurzinfo des letzten Fehlers fuer Diagnostik und Tests.</summary>
	public string? LastError { get; init; }
}
