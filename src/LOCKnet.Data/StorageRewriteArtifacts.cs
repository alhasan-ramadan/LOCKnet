using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;

namespace LOCKnet.Data;

internal static class StorageRewriteArtifacts
{
	internal const string RewriteTempSuffix = ".rewrite.tmp";
	internal const string RewriteBackupSuffix = ".rewrite.bak";

	internal static string? TryResolveDatabasePath(string connectionString)
	{
		if (string.IsNullOrWhiteSpace(connectionString))
			return null;

		var builder = new SqliteConnectionStringBuilder(connectionString);
		if (builder.Mode == SqliteOpenMode.Memory)
			return null;

		if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
			return null;

		return Path.GetFullPath(builder.DataSource);
	}

	internal static string GetTempPath(string databasePath) => databasePath + RewriteTempSuffix;

	internal static string GetBackupPath(string databasePath) => databasePath + RewriteBackupSuffix;

	internal static bool HasPendingArtifacts(string? databasePath)
		=> databasePath is not null &&
			(File.Exists(GetTempPath(databasePath)) || File.Exists(GetBackupPath(databasePath)));

	internal static StartupRecoveryOutcome Recover(string? databasePath)
	{
		if (string.IsNullOrWhiteSpace(databasePath))
			return default;

		var primaryPath = Path.GetFullPath(databasePath);
		var tempPath = GetTempPath(primaryPath);
		var backupPath = GetBackupPath(primaryPath);
		var shouldClearPendingState = false;

		var mainExists = File.Exists(primaryPath);
		var backupExists = File.Exists(backupPath);
		var tempExists = File.Exists(tempPath);
		var mainValid = mainExists && IsUsableSqliteDatabase(primaryPath);
		var backupValid = backupExists && IsUsableSqliteDatabase(backupPath);
		var tempValid = tempExists && IsUsableSqliteDatabase(tempPath);

		if ((!mainExists || !mainValid) && backupValid)
		{
			RestoreFile(backupPath, primaryPath);
			mainExists = true;
			mainValid = true;
			backupExists = false;
			backupValid = false;
		}
		else if ((!mainExists || !mainValid) && !backupExists && tempValid)
		{
			PromoteFile(tempPath, primaryPath);
			mainExists = true;
			mainValid = true;
			tempExists = false;
			tempValid = false;
		}

		if (!mainExists && (backupExists || tempExists))
			throw new InvalidOperationException("Vault-Rewrite-Artefakte gefunden, aber keine verwendbare Hauptdatenbank konnte wiederhergestellt werden.");

		if (mainExists && !mainValid && (backupExists || tempExists))
			throw new InvalidOperationException("Vault-Datei ist nach einer unterbrochenen Rewrite-Bereinigung ungueltig. Backup pruefen.");

		if (mainValid && backupExists)
		{
			if (TryDeleteFile(backupPath))
				shouldClearPendingState = true;
		}

		if (mainValid && tempExists)
			TryDeleteFile(tempPath);

		return new StartupRecoveryOutcome(shouldClearPendingState);
	}

	internal static void ClearPendingState(SqliteConnection connection)
	{
		ArgumentNullException.ThrowIfNull(connection);

		using var command = connection.CreateCommand();
		command.CommandText = @"
                UPDATE MasterKey
                SET RequiresStorageCompaction = 0,
                    LastStorageCompactionAttemptUtc = NULL,
                    LastStorageCompactionFailureKind = 0,
                    LastStorageCompactionError = NULL,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = 1;";
		command.ExecuteNonQuery();
	}

	internal static bool IsUsableSqliteDatabase(string databasePath)
	{
		if (!File.Exists(databasePath))
			return false;

		try
		{
			var builder = new SqliteConnectionStringBuilder
			{
				DataSource = databasePath,
				Mode = SqliteOpenMode.ReadOnly,
			};

			using var connection = new SqliteConnection(builder.ToString());
			connection.Open();

			using var quickCheck = connection.CreateCommand();
			quickCheck.CommandText = "PRAGMA quick_check(1);";
			var quickCheckResult = Convert.ToString(quickCheck.ExecuteScalar()) ?? string.Empty;
			if (!string.Equals(quickCheckResult, "ok", StringComparison.OrdinalIgnoreCase))
				return false;

			using var tableCheck = connection.CreateCommand();
			tableCheck.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MasterKey';";
			return Convert.ToInt64(tableCheck.ExecuteScalar() ?? 0L) > 0;
		}
		catch (SqliteException)
		{
			return false;
		}
		catch (IOException)
		{
			return false;
		}
	}

	internal static void ReplacePrimaryDatabase(string tempPath, string primaryPath, string backupPath)
	{
		for (var attempt = 0; attempt < 120; attempt++)
		{
			try
			{
				SqliteConnection.ClearAllPools();
				Thread.Sleep(25);

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(primaryPath))
				{
					File.Replace(tempPath, primaryPath, backupPath, ignoreMetadataErrors: true);
				}
				else
				{
					File.Move(tempPath, primaryPath, overwrite: true);
				}

				return;
			}
			catch (IOException) when (attempt < 119)
			{
				Thread.Sleep(25);
			}
			catch (UnauthorizedAccessException) when (attempt < 119)
			{
				Thread.Sleep(25);
			}
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(primaryPath))
		{
			SqliteConnection.ClearAllPools();
			File.Replace(tempPath, primaryPath, backupPath, ignoreMetadataErrors: true);
			return;
		}

		SqliteConnection.ClearAllPools();
		File.Move(tempPath, primaryPath, overwrite: true);
	}

	private static void RestoreFile(string sourcePath, string destinationPath)
	{
		MoveFileWithRetry(sourcePath, destinationPath, deleteDestinationIfPresent: true);
	}

	private static void PromoteFile(string sourcePath, string destinationPath)
	{
		MoveFileWithRetry(sourcePath, destinationPath, deleteDestinationIfPresent: true);
	}

	internal static bool TryDeleteFile(string path)
	{
		for (var attempt = 0; attempt < 120; attempt++)
		{
			try
			{
				SqliteConnection.ClearAllPools();
				Thread.Sleep(25);

				if (File.Exists(path))
					File.Delete(path);

				if (!File.Exists(path))
					return true;
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}

			Thread.Sleep(25);
		}

		return !File.Exists(path);
	}

	private static void MoveFileWithRetry(string sourcePath, string destinationPath, bool deleteDestinationIfPresent)
	{
		for (var attempt = 0; attempt < 120; attempt++)
		{
			try
			{
				SqliteConnection.ClearAllPools();
				Thread.Sleep(25);

				if (deleteDestinationIfPresent && File.Exists(destinationPath) && !TryDeleteFile(destinationPath))
					throw new IOException($"Zieldatei konnte nicht entfernt werden: {destinationPath}");

				File.Move(sourcePath, destinationPath);
				return;
			}
			catch (IOException) when (attempt < 119)
			{
				Thread.Sleep(25);
			}
			catch (UnauthorizedAccessException) when (attempt < 119)
			{
				Thread.Sleep(25);
			}
		}

		if (deleteDestinationIfPresent && File.Exists(destinationPath) && !TryDeleteFile(destinationPath))
			throw new IOException($"Zieldatei konnte nicht entfernt werden: {destinationPath}");

		SqliteConnection.ClearAllPools();
		File.Move(sourcePath, destinationPath);
	}

	internal readonly record struct StartupRecoveryOutcome(bool ShouldClearPendingState);
}
