using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.Data;

internal static class PlainToEncryptedVaultMigrationArtifacts
{
	internal const string EncryptedTempSuffix = ".migration.encrypted.tmp";
	internal const string PlainBackupSuffix = ".migration.plain.bak";

	internal static string GetEncryptedTempPath(string databasePath) => databasePath + EncryptedTempSuffix;

	internal static string GetPlainBackupPath(string databasePath) => databasePath + PlainBackupSuffix;

	internal static bool HasPendingArtifacts(string? databasePath)
		=> databasePath is not null &&
			(File.Exists(GetEncryptedTempPath(databasePath)) || File.Exists(GetPlainBackupPath(databasePath)));

	internal static PlainToEncryptedVaultMigrationRecoveryDecision Decide(string? databasePath, VaultHeader header)
	{
		if (databasePath is null)
			return PlainToEncryptedVaultMigrationRecoveryDecision.None();

		var tempPath = GetEncryptedTempPath(databasePath);
		var backupPath = GetPlainBackupPath(databasePath);
		var tempExists = File.Exists(tempPath);
		var backupExists = File.Exists(backupPath);

		return header.StorageMigrationState switch
		{
			VaultStorageMigrationState.None => (tempExists || backupExists)
				? PlainToEncryptedVaultMigrationRecoveryDecision.Fail("Persistierte Storage-Migrationsartefakte gefunden, aber kein Migrationszustand ist markiert.", tempPath, backupPath)
				: PlainToEncryptedVaultMigrationRecoveryDecision.None(),
			VaultStorageMigrationState.InProgress => tempExists && !backupExists
				? PlainToEncryptedVaultMigrationRecoveryDecision.ResumeExport(tempPath, backupPath)
				: PlainToEncryptedVaultMigrationRecoveryDecision.Fail("Storage-Migration als in Arbeit markiert, aber die erwarteten Export-Artefakte sind inkonsistent.", tempPath, backupPath),
			VaultStorageMigrationState.FinalizationPending => backupExists && !tempExists
				? PlainToEncryptedVaultMigrationRecoveryDecision.FinalizeBackupCleanup(tempPath, backupPath)
				: PlainToEncryptedVaultMigrationRecoveryDecision.Fail("Storage-Migration wartet auf Finalisierung, aber die erwarteten Finalisierungsartefakte sind inkonsistent.", tempPath, backupPath),
			VaultStorageMigrationState.Failed => (tempExists || backupExists)
				? PlainToEncryptedVaultMigrationRecoveryDecision.Fail("Fehlgeschlagene Storage-Migration hat Artefakte hinterlassen. Die Plain-Vault darf nicht stillschweigend verworfen werden.", tempPath, backupPath)
				: PlainToEncryptedVaultMigrationRecoveryDecision.None(),
			_ => PlainToEncryptedVaultMigrationRecoveryDecision.Fail("Unbekannter Storage-Migrationszustand erkannt.", tempPath, backupPath),
		};
	}
}

internal enum PlainToEncryptedVaultMigrationRecoveryAction
{
	None = 0,
	ResumeExport = 1,
	FinalizeBackupCleanup = 2,
	Fail = 3,
}

internal sealed record PlainToEncryptedVaultMigrationRecoveryDecision(
	PlainToEncryptedVaultMigrationRecoveryAction Action,
	string? Message,
	string? EncryptedTempPath,
	string? PlainBackupPath)
{
	internal static PlainToEncryptedVaultMigrationRecoveryDecision None() => new(PlainToEncryptedVaultMigrationRecoveryAction.None, null, null, null);

	internal static PlainToEncryptedVaultMigrationRecoveryDecision ResumeExport(string tempPath, string backupPath)
		=> new(PlainToEncryptedVaultMigrationRecoveryAction.ResumeExport, "Storage-Migration muss mit dem vorhandenen encrypted Temp-Artefakt fortgesetzt werden.", tempPath, backupPath);

	internal static PlainToEncryptedVaultMigrationRecoveryDecision FinalizeBackupCleanup(string tempPath, string backupPath)
		=> new(PlainToEncryptedVaultMigrationRecoveryAction.FinalizeBackupCleanup, "Storage-Migration hat die Ziel-Vault erstellt; nur die alte Plain-Sicherung muss noch final entfernt werden.", tempPath, backupPath);

	internal static PlainToEncryptedVaultMigrationRecoveryDecision Fail(string message, string tempPath, string backupPath)
		=> new(PlainToEncryptedVaultMigrationRecoveryAction.Fail, message, tempPath, backupPath);
}
