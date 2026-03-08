using System.Text.Json;

namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Persistierter Vault-Header.
/// Er enthaelt die Informationen, die benoetigt werden, um aus dem Master-Passwort
/// einen KEK abzuleiten und damit den eigentlichen Vault-Key zu entpacken.
/// </summary>
public sealed class VaultHeader
{
	/// <summary>Aktuelle Versionsnummer des Header-Formats.</summary>
	public int FormatVersion { get; set; } = VaultHeaderFormatVersion.WrappedVaultKeyV1;

	/// <summary>Bezeichner der verwendeten KDF, z.B. <c>PBKDF2-SHA256</c>.</summary>
	public string KdfIdentifier { get; set; } = string.Empty;

	/// <summary>Persistierte Parameter fuer die KDF.</summary>
	public VaultKdfParameters KdfParameters { get; set; } = new();

	/// <summary>Salt fuer die KEK-Ableitung.</summary>
	public byte[] Salt { get; set; } = [];

	/// <summary>
	/// AES-GCM-verpackter Vault-Key.
	/// Der Inhalt wird mit dem aus dem Master-Passwort abgeleiteten KEK entpackt.
	/// </summary>
	public byte[] WrappedVaultKey { get; set; } = [];

	/// <summary>
	/// Kompatibilitaets-Hash fuer Legacy-Migrationen vorhandener Datenbanken.
	/// Neue Unlocks verlassen sich primaer auf <see cref="WrappedVaultKey"/>.
	/// </summary>
	public byte[] LegacyPasswordHash { get; set; } = [];

	/// <summary>
	/// Gibt an, ob der aktuell verpackte VaultKey noch aus einem Legacy-KDF-Schluessel stammt
	/// und deshalb vor kryptografisch abgeschlossener Passwortrotation ersetzt werden muss.
	/// </summary>
	public bool UsesLegacyKeyMaterial { get; set; }

	/// <summary>
	/// Markiert, dass nach einer Migration oder Plausibilitaetsverletzung noch eine SQLite-Kompaktierung aussteht.
	/// </summary>
	public bool RequiresStorageCompaction { get; set; }

	/// <summary>UTC-Zeitstempel des letzten Kompaktierungsversuchs.</summary>
	public DateTime? LastStorageCompactionAttemptUtc { get; set; }

	/// <summary>Klassifizierter Fehler des letzten Kompaktierungsversuchs.</summary>
	public StorageCompactionFailureKind LastStorageCompactionFailureKind { get; set; }

	/// <summary>Technische Kurzinfo des letzten Kompaktierungsversuchs.</summary>
	public string? LastStorageCompactionError { get; set; }

	/// <summary>UTC-Zeitstempel der Erstellung.</summary>
	public DateTime CreatedAt { get; set; }

	/// <summary>UTC-Zeitstempel der letzten Aenderung.</summary>
	public DateTime UpdatedAt { get; set; }

	/// <summary>Serialisiert den Header in JSON, z.B. fuer Tests oder Exportformate.</summary>
	public string Serialize()
		=> JsonSerializer.Serialize(this);

	/// <summary>Deserialisiert einen JSON-String in einen Vault-Header.</summary>
	/// <param name="json">Der serialisierte JSON-String.</param>
	public static VaultHeader Deserialize(string json)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(json);
		return JsonSerializer.Deserialize<VaultHeader>(json)
			?? throw new InvalidOperationException("Vault-Header konnte nicht deserialisiert werden.");
	}
}
