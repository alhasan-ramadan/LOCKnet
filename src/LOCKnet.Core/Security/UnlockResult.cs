using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.Core.Security;

/// <summary>
/// Ergebnis eines erfolgreichen Unlock-Vorgangs inklusive eventueller Storage-Hygiene-Warnungen.
/// </summary>
public sealed class UnlockResult
{
	/// <summary>Der aktive 32-Byte-VaultKey fuer die Session.</summary>
	public required byte[] VaultKey { get; init; }

	/// <summary>Status der Storage-Kompaktierung nach dem Unlock.</summary>
	public required StorageCompactionInfo StorageCompaction { get; init; }
}
