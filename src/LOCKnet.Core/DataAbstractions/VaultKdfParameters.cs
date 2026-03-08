using System.Text.Json;

namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Beschreibt die KDF-Konfiguration fuer einen Vault-Header.
/// Die Parameter werden serialisiert gespeichert, damit kuenftige Unlocks
/// denselben Ableitungsweg reproduzieren koennen.
/// </summary>
public sealed class VaultKdfParameters
{
	/// <summary>Name des zugrunde liegenden Hash-Algorithmus.</summary>
	public string HashAlgorithm { get; set; } = "SHA256";

	/// <summary>Anzahl der KDF-Iterationen.</summary>
	public int Iterations { get; set; }

	/// <summary>Ausgabegroesse des abgeleiteten Schluessels in Bytes.</summary>
	public int KeyLengthBytes { get; set; } = 32;

	/// <summary>Empfohlene Salt-Laenge in Bytes fuer neue Header.</summary>
	public int SaltLengthBytes { get; set; } = 32;

	/// <summary>Serialisiert die Parameter in JSON fuer die Persistenz.</summary>
	public string Serialize()
		=> JsonSerializer.Serialize(this);

	/// <summary>Deserialisiert einen JSON-String in KDF-Parameter.</summary>
	/// <param name="json">Der serialisierte JSON-String.</param>
	public static VaultKdfParameters Deserialize(string json)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(json);
		return JsonSerializer.Deserialize<VaultKdfParameters>(json)
			?? throw new InvalidOperationException("KDF-Parameter konnten nicht deserialisiert werden.");
	}
}
