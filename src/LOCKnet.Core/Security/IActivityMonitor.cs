namespace LOCKnet.Core.Security;

/// <summary>
/// Überwacht die Benutzeraktivität und löst nach einem konfigurierbaren
/// Inaktivitäts-Timeout automatisch einen Lock aus.
/// </summary>
public interface IActivityMonitor : IDisposable
{
	/// <summary>
	/// Timeout-Dauer nach der letzten Aktivität. Standard: 60 Sekunden.
	/// </summary>
	TimeSpan Timeout { get; set; }

	/// <summary>
	/// Gibt an, ob der Monitor aktuell läuft.
	/// </summary>
	bool IsRunning { get; }

	/// <summary>
	/// Zeitpunkt der letzten registrierten Aktivität.
	/// </summary>
	DateTimeOffset LastActivity { get; }

	/// <summary>
	/// Startet die Aktivitätsüberwachung.
	/// </summary>
	void Start();

	/// <summary>
	/// Stoppt die Aktivitätsüberwachung (kein Auto-Lock mehr möglich).
	/// </summary>
	void Stop();

	/// <summary>
	/// Registriert eine Benutzeraktivität — setzt den Timeout-Timer zurück.
	/// Muss bei jeder UI-Interaktion aufgerufen werden.
	/// </summary>
	void RecordActivity();
}
