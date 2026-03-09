namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Kategorisiert fehlgeschlagene SQLite-Kompaktierungen fuer Recovery- und UX-Entscheidungen.
/// </summary>
public enum StorageCompactionFailureKind
{
	/// <summary>Kein Fehler.</summary>
	None = 0,
	/// <summary>Die Datenbank oder Datei ist momentan gesperrt bzw. belegt.</summary>
	BusyOrLocked = 1,
	/// <summary>Der Datentraeger hat zu wenig freien Platz fuer die Kompaktierung.</summary>
	InsufficientSpace = 2,
	/// <summary>Allgemeiner I/O-Fehler waehrend der SQLite-Operation.</summary>
	Io = 3,
	/// <summary>Hinweis auf DB-Korruption oder kein gueltiges SQLite-Format.</summary>
	Corruption = 4,
	/// <summary>Nicht eindeutig zuordenbarer Fehler.</summary>
	Unknown = 5,
}
