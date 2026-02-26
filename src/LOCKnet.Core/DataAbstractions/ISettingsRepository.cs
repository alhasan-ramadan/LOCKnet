namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Verwaltet Konfigurationseinstellungen als Key-Value-Paare.
/// </summary>
public interface ISettingsRepository
{
    /// <summary>
    /// Schreibt einen Wert. Existiert der Schlüssel bereits, wird er überschrieben (Upsert).
    /// </summary>
    void Set(string key, string value);

    /// <summary>
    /// Liest den Wert zum angegebenen Schlüssel.
    /// </summary>
    /// <returns>Den gespeicherten Wert, oder <c>null</c> wenn der Schlüssel nicht existiert.</returns>
    string? Get(string key);

    /// <summary>
    /// Gibt alle gespeicherten Einstellungen zurück.
    /// </summary>
    IReadOnlyDictionary<string, string> GetAll();

    /// <summary>
    /// Löscht den Eintrag mit dem angegebenen Schlüssel.
    /// Kein Fehler wenn der Schlüssel nicht existiert.
    /// </summary>
    void Remove(string key);
}
