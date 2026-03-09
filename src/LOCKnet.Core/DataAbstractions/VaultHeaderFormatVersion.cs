namespace LOCKnet.Core.DataAbstractions;

/// <summary>
/// Versionskennungen fuer das VaultHeader-Format.
/// </summary>
public static class VaultHeaderFormatVersion
{
	/// <summary>Vor Wrapped-VaultKey und vor record-spezifischer Envelope-Migration.</summary>
	public const int Legacy = 0;

	/// <summary>Erste Wrapped-VaultKey-Einfuehrung ohne expliziten Migrationsstatus.</summary>
	public const int WrappedVaultKeyV1 = 1;

	/// <summary>Aktuelles Header-Format mit Migrationsstatus fuer Credential-Secrets.</summary>
	public const int WrappedVaultKeyV2 = 2;

	/// <summary>Aktuell unterstuetzte Header-Version fuer neue Writes.</summary>
	public const int Current = WrappedVaultKeyV2;
}
