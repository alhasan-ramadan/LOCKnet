using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data;

internal sealed class SqlCipherEncryptedVaultMigrationExporter : IEncryptedVaultMigrationExporter
{
	private readonly string _password;
	private readonly Func<SqlCipherRuntimeProbeResult>? _runtimeProbeOverride;

	internal SqlCipherEncryptedVaultMigrationExporter(string password)
		: this(password, null)
	{
	}

	internal SqlCipherEncryptedVaultMigrationExporter(string password, Func<SqlCipherRuntimeProbeResult>? runtimeProbeOverride)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(password);
		_password = password;
		_runtimeProbeOverride = runtimeProbeOverride;
	}

	public VaultStorageMigrationTargetMode TargetMode => VaultStorageMigrationTargetMode.EncryptedSqlite;

	internal SqlCipherRuntimeProbeResult ProbeRuntime()
	{
		if (_runtimeProbeOverride is not null)
			return _runtimeProbeOverride();

		var providerPath = GetConfiguredProviderPath();
		var productionCrediblePath = providerPath is SqlCipherProviderPackagingPath.OfficialZetetic;

		try
		{
#if USE_OFFICIAL_SQLCIPHER_PATH
			if (providerPath is SqlCipherProviderPackagingPath.BundleZeteticWithProviderSqlcipher)
				SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlcipher());
#endif
		}
		catch (Exception ex)
		{
			return new SqlCipherRuntimeProbeResult(
				false,
				null,
				null,
				providerPath,
				productionCrediblePath,
				SqlCipherExporterFailureKind.NativeProviderLoadFailure,
				$"SQLCipher provider activation failed: {GetInnermostMessage(ex)}");
		}

		SQLitePCL.Batteries_V2.Init();

		try
		{
			using var connection = new SqliteConnection("Data Source=:memory:");
			connection.Open();
			using var versionCommand = connection.CreateCommand();
			versionCommand.CommandText = "PRAGMA cipher_version;";
			var cipherVersion = Convert.ToString(versionCommand.ExecuteScalar()) ?? string.Empty;
			if (string.IsNullOrWhiteSpace(cipherVersion))
				return new SqlCipherRuntimeProbeResult(false, null, connection.ServerVersion, providerPath, productionCrediblePath, SqlCipherExporterFailureKind.CipherSupportUnavailable, "SQLCipher PRAGMA cipher_version lieferte keinen Wert.");

			return new SqlCipherRuntimeProbeResult(true, cipherVersion, connection.ServerVersion, providerPath, productionCrediblePath, SqlCipherExporterFailureKind.None, null);
		}
		catch (Exception ex)
		{
			var message = GetInnermostMessage(ex);
			return new SqlCipherRuntimeProbeResult(false, null, null, providerPath, productionCrediblePath, SqlCipherExporterFailureKind.NativeProviderLoadFailure, message);
		}
	}

	public void ExportPlaintextVault(string sourceConnectionString, string destinationPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceConnectionString);
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

		EnsureRuntimeAvailable();

		var sourcePath = StorageRewriteArtifacts.TryResolveDatabasePath(sourceConnectionString)
			?? throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.MigrationExportFailure, "SQLCipher-Export erwartet eine dateibasierte Plain-Vault als Quelle.");

		if (File.Exists(destinationPath) && !StorageRewriteArtifacts.TryDeleteFile(destinationPath))
			throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.MigrationExportFailure, "Vorhandenes Zielartefakt konnte vor dem SQLCipher-Export nicht entfernt werden.");

		try
		{
			var destinationFactory = new SqlCipherConnectionFactory(destinationPath, _password);
			new Database(destinationFactory).Initialize();

			using var sourceConnection = new SqliteConnection(sourceConnectionString);
			sourceConnection.Open();
			RepositoryBase.ConfigureConnection(sourceConnection);

			using var destinationConnection = destinationFactory.OpenConnection();
			using var transaction = destinationConnection.BeginTransaction();

			CopyMasterKey(sourceConnection, destinationConnection, transaction);
			CopyCredentials(sourceConnection, destinationConnection, transaction);
			CopySettings(sourceConnection, destinationConnection, transaction);

			transaction.Commit();
		}
		catch (SqlCipherEncryptedVaultMigrationException)
		{
			throw;
		}
		catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
		{
			throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.MigrationExportFailure, GetInnermostMessage(ex), ex);
		}
	}

	public void ValidateExportedVault(string destinationPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
		EnsureRuntimeAvailable();

		if (!File.Exists(destinationPath))
			throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.InvalidTarget, "SQLCipher-Zielartefakt fehlt nach dem Export.");

		var openResult = TryOpenEncryptedVault(destinationPath, _password);
		if (!openResult.Success)
			throw new SqlCipherEncryptedVaultMigrationException(openResult.FailureKind, openResult.Message ?? "SQLCipher-Zielartefakt konnte nicht geoeffnet werden.");

		try
		{
			using var connection = OpenEncryptedConnection(destinationPath, _password, SqliteOpenMode.ReadOnly);
			RequireCipherVersion(connection);
			RequireQuickCheck(connection);
			RequireTable(connection, "MasterKey");
			RequireTable(connection, "Credentials");
			RequireTable(connection, "Settings");

			using var masterKeyCount = connection.CreateCommand();
			masterKeyCount.CommandText = "SELECT COUNT(*) FROM MasterKey;";
			if (Convert.ToInt64(masterKeyCount.ExecuteScalar() ?? 0L) != 1)
				throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.ValidationFailure, "SQLCipher-Zielvault enthaelt nicht genau einen MasterKey-Datensatz.");
		}
		catch (SqlCipherEncryptedVaultMigrationException)
		{
			throw;
		}
		catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
		{
			throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.OperationalFailure, GetInnermostMessage(ex), ex);
		}
	}

	public void PersistMigratedHeader(string databasePath, VaultHeader header)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
		ArgumentNullException.ThrowIfNull(header);
		EnsureRuntimeAvailable();

		try
		{
			using var connection = OpenEncryptedConnection(databasePath, _password, SqliteOpenMode.ReadWrite);
			using var command = connection.CreateCommand();
			command.CommandText = @"
				UPDATE MasterKey
				SET PasswordHash = $hash,
					FormatVersion = $formatVersion,
					KdfIdentifier = $kdfIdentifier,
					KdfParameters = $kdfParameters,
					Salt = $salt,
					WrappedVaultKey = $wrappedVaultKey,
					UsesLegacyKeyMaterial = $usesLegacyKeyMaterial,
					RequiresStorageCompaction = $requiresStorageCompaction,
					LastStorageCompactionAttemptUtc = $lastStorageCompactionAttemptUtc,
					LastStorageCompactionFailureKind = $lastStorageCompactionFailureKind,
					LastStorageCompactionError = $lastStorageCompactionError,
					StorageMigrationState = $storageMigrationState,
					StorageMigrationTargetMode = $storageMigrationTargetMode,
					LastStorageMigrationAttemptUtc = $lastStorageMigrationAttemptUtc,
					LastStorageMigrationError = $lastStorageMigrationError,
					UpdatedAt = CURRENT_TIMESTAMP
				WHERE Id = 1;";
			command.Parameters.AddWithValue("$hash", header.LegacyPasswordHash);
			command.Parameters.AddWithValue("$formatVersion", header.FormatVersion);
			command.Parameters.AddWithValue("$kdfIdentifier", header.KdfIdentifier);
			command.Parameters.AddWithValue("$kdfParameters", header.KdfParameters.Serialize());
			command.Parameters.AddWithValue("$salt", header.Salt);
			command.Parameters.AddWithValue("$wrappedVaultKey", (object?)header.WrappedVaultKey ?? DBNull.Value);
			command.Parameters.AddWithValue("$usesLegacyKeyMaterial", header.UsesLegacyKeyMaterial ? 1 : 0);
			command.Parameters.AddWithValue("$requiresStorageCompaction", header.RequiresStorageCompaction ? 1 : 0);
			command.Parameters.AddWithValue("$lastStorageCompactionAttemptUtc", (object?)header.LastStorageCompactionAttemptUtc?.ToString("O") ?? DBNull.Value);
			command.Parameters.AddWithValue("$lastStorageCompactionFailureKind", (int)header.LastStorageCompactionFailureKind);
			command.Parameters.AddWithValue("$lastStorageCompactionError", (object?)header.LastStorageCompactionError ?? DBNull.Value);
			command.Parameters.AddWithValue("$storageMigrationState", (int)header.StorageMigrationState);
			command.Parameters.AddWithValue("$storageMigrationTargetMode", (int)header.StorageMigrationTargetMode);
			command.Parameters.AddWithValue("$lastStorageMigrationAttemptUtc", (object?)header.LastStorageMigrationAttemptUtc?.ToString("O") ?? DBNull.Value);
			command.Parameters.AddWithValue("$lastStorageMigrationError", (object?)header.LastStorageMigrationError ?? DBNull.Value);
			command.ExecuteNonQuery();
		}
		catch (SqlCipherEncryptedVaultMigrationException)
		{
			throw;
		}
		catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
		{
			throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.OperationalFailure, GetInnermostMessage(ex), ex);
		}
	}

	internal SqlCipherVaultOpenResult TryOpenEncryptedVault(string destinationPath, string password)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(password);

		var runtime = ProbeRuntime();
		if (!runtime.IsAvailable)
			return new SqlCipherVaultOpenResult(false, runtime.CipherVersion, runtime.ProviderPath, runtime.IsProductionCrediblePath, runtime.FailureKind, runtime.Message);

		if (!File.Exists(destinationPath))
			return new SqlCipherVaultOpenResult(false, runtime.CipherVersion, runtime.ProviderPath, runtime.IsProductionCrediblePath, SqlCipherExporterFailureKind.InvalidTarget, "SQLCipher-Zieldatei wurde nicht gefunden.");

		try
		{
			using var connection = OpenEncryptedConnection(destinationPath, password, SqliteOpenMode.ReadOnly);
			var cipherVersion = RequireCipherVersion(connection);
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
			_ = Convert.ToInt64(command.ExecuteScalar() ?? 0L);
			return new SqlCipherVaultOpenResult(true, cipherVersion, runtime.ProviderPath, runtime.IsProductionCrediblePath, SqlCipherExporterFailureKind.None, null);
		}
		catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
		{
			var kind = ClassifyOpenFailure(ex);
			return new SqlCipherVaultOpenResult(false, runtime.CipherVersion, runtime.ProviderPath, runtime.IsProductionCrediblePath, kind, GetInnermostMessage(ex));
		}
	}

	private void EnsureRuntimeAvailable()
	{
		var runtime = ProbeRuntime();
		if (!runtime.IsAvailable)
			throw new SqlCipherEncryptedVaultMigrationException(runtime.FailureKind, runtime.Message ?? "SQLCipher-Laufzeit konnte nicht initialisiert werden.");
	}

	private static SqliteConnection OpenEncryptedConnection(string databasePath, string password, SqliteOpenMode mode)
	{
		var builder = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = mode,
			Password = password,
		};

		var connection = new SqliteConnection(builder.ToString());
		connection.Open();
		if (mode != SqliteOpenMode.ReadOnly)
			RepositoryBase.ConfigureConnection(connection);
		return connection;
	}

	private static string RequireCipherVersion(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "PRAGMA cipher_version;";
		var cipherVersion = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(cipherVersion))
			throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.NativeProviderLoadFailure, "PRAGMA cipher_version meldet kein SQLCipher-Library-Ergebnis.");

		return cipherVersion;
	}

	private static void RequireQuickCheck(SqliteConnection connection)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "PRAGMA quick_check(1);";
		var result = Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
		if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
			throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.ValidationFailure, "SQLCipher quick_check hat kein gueltiges Ergebnis geliefert.");
	}

	private static void RequireTable(SqliteConnection connection, string tableName)
	{
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
		command.Parameters.AddWithValue("$name", tableName);
		if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) <= 0)
			throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.ValidationFailure, $"SQLCipher-Zielvault enthaelt die Tabelle '{tableName}' nicht.");
	}

	private static void CopyMasterKey(SqliteConnection sourceConnection, SqliteConnection destinationConnection, SqliteTransaction transaction)
	{
		using var sourceCommand = sourceConnection.CreateCommand();
		sourceCommand.CommandText = @"
			SELECT Id, PasswordHash, FormatVersion, KdfIdentifier, KdfParameters, Salt, WrappedVaultKey, UsesLegacyKeyMaterial,
			       RequiresStorageCompaction, LastStorageCompactionAttemptUtc, LastStorageCompactionFailureKind, LastStorageCompactionError,
			       StorageMigrationState, StorageMigrationTargetMode, LastStorageMigrationAttemptUtc, LastStorageMigrationError, CreatedAt, UpdatedAt
			FROM MasterKey;";
		using var reader = sourceCommand.ExecuteReader();
		while (reader.Read())
		{
			using var insert = destinationConnection.CreateCommand();
			insert.Transaction = transaction;
			insert.CommandText = @"
				INSERT INTO MasterKey (Id, PasswordHash, FormatVersion, KdfIdentifier, KdfParameters, Salt, WrappedVaultKey, UsesLegacyKeyMaterial,
					RequiresStorageCompaction, LastStorageCompactionAttemptUtc, LastStorageCompactionFailureKind, LastStorageCompactionError,
					StorageMigrationState, StorageMigrationTargetMode, LastStorageMigrationAttemptUtc, LastStorageMigrationError, CreatedAt, UpdatedAt)
				VALUES ($id, $passwordHash, $formatVersion, $kdfIdentifier, $kdfParameters, $salt, $wrappedVaultKey, $usesLegacyKeyMaterial,
					$requiresStorageCompaction, $lastStorageCompactionAttemptUtc, $lastStorageCompactionFailureKind, $lastStorageCompactionError,
					$storageMigrationState, $storageMigrationTargetMode, $lastStorageMigrationAttemptUtc, $lastStorageMigrationError, $createdAt, $updatedAt);";
			insert.Parameters.AddWithValue("$id", reader.GetInt64(0));
			insert.Parameters.AddWithValue("$passwordHash", reader.IsDBNull(1) ? [] : (byte[])reader[1]);
			insert.Parameters.AddWithValue("$formatVersion", reader.GetInt32(2));
			insert.Parameters.AddWithValue("$kdfIdentifier", reader.GetString(3));
			insert.Parameters.AddWithValue("$kdfParameters", reader.GetString(4));
			insert.Parameters.AddWithValue("$salt", (byte[])reader[5]);
			insert.Parameters.AddWithValue("$wrappedVaultKey", reader.IsDBNull(6) ? DBNull.Value : reader[6]);
			insert.Parameters.AddWithValue("$usesLegacyKeyMaterial", reader.GetInt32(7));
			insert.Parameters.AddWithValue("$requiresStorageCompaction", reader.GetInt32(8));
			insert.Parameters.AddWithValue("$lastStorageCompactionAttemptUtc", reader.IsDBNull(9) ? DBNull.Value : reader.GetString(9));
			insert.Parameters.AddWithValue("$lastStorageCompactionFailureKind", reader.GetInt32(10));
			insert.Parameters.AddWithValue("$lastStorageCompactionError", reader.IsDBNull(11) ? DBNull.Value : reader.GetString(11));
			insert.Parameters.AddWithValue("$storageMigrationState", reader.GetInt32(12));
			insert.Parameters.AddWithValue("$storageMigrationTargetMode", reader.GetInt32(13));
			insert.Parameters.AddWithValue("$lastStorageMigrationAttemptUtc", reader.IsDBNull(14) ? DBNull.Value : reader.GetString(14));
			insert.Parameters.AddWithValue("$lastStorageMigrationError", reader.IsDBNull(15) ? DBNull.Value : reader.GetString(15));
			insert.Parameters.AddWithValue("$createdAt", reader.GetDateTime(16));
			insert.Parameters.AddWithValue("$updatedAt", reader.GetDateTime(17));
			insert.ExecuteNonQuery();
		}
	}

	private static void CopyCredentials(SqliteConnection sourceConnection, SqliteConnection destinationConnection, SqliteTransaction transaction)
	{
		using var sourceCommand = sourceConnection.CreateCommand();
		sourceCommand.CommandText = @"
			SELECT Id, Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion,
			       URL, Notes, CreatedAt, UpdatedAt, IconKey, CredentialType
			FROM Credentials;";
		using var reader = sourceCommand.ExecuteReader();
		while (reader.Read())
		{
			using var insert = destinationConnection.CreateCommand();
			insert.Transaction = transaction;
			insert.CommandText = @"
				INSERT INTO Credentials (Id, Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion,
					URL, Notes, CreatedAt, UpdatedAt, IconKey, CredentialType)
				VALUES ($id, $title, $username, $encryptedPassword, $encryptedMetadata, $credentialUuid, $secretFormatVersion, $metadataFormatVersion,
					$url, $notes, $createdAt, $updatedAt, $iconKey, $credentialType);";
			insert.Parameters.AddWithValue("$id", reader.GetInt64(0));
			insert.Parameters.AddWithValue("$title", reader.GetString(1));
			insert.Parameters.AddWithValue("$username", reader.IsDBNull(2) ? DBNull.Value : reader.GetString(2));
			insert.Parameters.AddWithValue("$encryptedPassword", (byte[])reader[3]);
			insert.Parameters.AddWithValue("$encryptedMetadata", reader.IsDBNull(4) ? DBNull.Value : reader[4]);
			insert.Parameters.AddWithValue("$credentialUuid", reader.GetString(5));
			insert.Parameters.AddWithValue("$secretFormatVersion", reader.GetInt32(6));
			insert.Parameters.AddWithValue("$metadataFormatVersion", reader.GetInt32(7));
			insert.Parameters.AddWithValue("$url", reader.IsDBNull(8) ? DBNull.Value : reader.GetString(8));
			insert.Parameters.AddWithValue("$notes", reader.IsDBNull(9) ? DBNull.Value : reader.GetString(9));
			insert.Parameters.AddWithValue("$createdAt", reader.GetDateTime(10));
			insert.Parameters.AddWithValue("$updatedAt", reader.GetDateTime(11));
			insert.Parameters.AddWithValue("$iconKey", reader.IsDBNull(12) ? DBNull.Value : reader.GetString(12));
			insert.Parameters.AddWithValue("$credentialType", reader.GetInt32(13));
			insert.ExecuteNonQuery();
		}
	}

	private static void CopySettings(SqliteConnection sourceConnection, SqliteConnection destinationConnection, SqliteTransaction transaction)
	{
		using var sourceCommand = sourceConnection.CreateCommand();
		sourceCommand.CommandText = "SELECT Id, Key, Value, CreatedAt, UpdatedAt FROM Settings;";
		using var reader = sourceCommand.ExecuteReader();
		while (reader.Read())
		{
			using var insert = destinationConnection.CreateCommand();
			insert.Transaction = transaction;
			insert.CommandText = @"
				INSERT INTO Settings (Id, Key, Value, CreatedAt, UpdatedAt)
				VALUES ($id, $key, $value, $createdAt, $updatedAt);";
			insert.Parameters.AddWithValue("$id", reader.GetInt64(0));
			insert.Parameters.AddWithValue("$key", reader.GetString(1));
			insert.Parameters.AddWithValue("$value", reader.GetString(2));
			insert.Parameters.AddWithValue("$createdAt", reader.GetDateTime(3));
			insert.Parameters.AddWithValue("$updatedAt", reader.GetDateTime(4));
			insert.ExecuteNonQuery();
		}
	}

	private static SqlCipherExporterFailureKind ClassifyOpenFailure(Exception exception)
	{
		if (exception is SqliteException sqliteException)
		{
			if (sqliteException.SqliteErrorCode == 26)
				return SqlCipherExporterFailureKind.WrongKey;

			if (sqliteException.Message.Contains("file is not a database", StringComparison.OrdinalIgnoreCase) ||
				sqliteException.Message.Contains("not a database", StringComparison.OrdinalIgnoreCase))
			{
				return SqlCipherExporterFailureKind.WrongKey;
			}

			return SqlCipherExporterFailureKind.InvalidTarget;
		}

		if (exception.InnerException is not null)
			return ClassifyOpenFailure(exception.InnerException);

		return SqlCipherExporterFailureKind.OperationalFailure;
	}

	private static string GetInnermostMessage(Exception exception)
	{
		var current = exception;
		while (current.InnerException is not null)
			current = current.InnerException;

		return current.Message;
	}

	private static SqlCipherProviderPackagingPath GetConfiguredProviderPath()
	{
#if USE_OFFICIAL_SQLCIPHER_PATH
		return SqlCipherProviderPackagingPath.BundleZeteticWithProviderSqlcipher;
#else
		return SqlCipherProviderPackagingPath.LegacyBundleESqlCipher;
#endif
	}

	private sealed class SqlCipherConnectionFactory : ISqliteConnectionFactory
	{
		private readonly string _databasePath;
		private readonly string _password;

		public SqlCipherConnectionFactory(string databasePath, string password)
		{
			_databasePath = Path.GetFullPath(databasePath);
			_password = password;
			Storage = new VaultStorageDescriptor(VaultStorageMode.EncryptedSqlite, $"Data Source={_databasePath}", _databasePath, requiresKeyAtOpen: true);
		}

		public VaultStorageDescriptor Storage { get; }

		public SqliteConnection OpenConnection()
			=> OpenEncryptedConnection(_databasePath, _password, SqliteOpenMode.ReadWriteCreate);
	}
}

internal enum SqlCipherExporterFailureKind
{
	None = 0,
	NativeProviderLoadFailure = 1,
	CipherSupportUnavailable = 2,
	WrongKey = 3,
	InvalidTarget = 4,
	MigrationExportFailure = 5,
	ValidationFailure = 6,
	OperationalFailure = 7,
}

internal enum SqlCipherProviderPackagingPath
{
	LegacyBundleESqlCipher = 0,
	BundleZeteticWithProviderSqlcipher = 1,
	OfficialZetetic = 2,
}

internal sealed class SqlCipherEncryptedVaultMigrationException : InvalidOperationException
{
	internal SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind failureKind, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		FailureKind = failureKind;
	}

	internal SqlCipherExporterFailureKind FailureKind { get; }
}

internal sealed record SqlCipherRuntimeProbeResult(
	bool IsAvailable,
	string? CipherVersion,
	string? SqliteVersion,
	SqlCipherProviderPackagingPath ProviderPath,
	bool IsProductionCrediblePath,
	SqlCipherExporterFailureKind FailureKind,
	string? Message);

internal sealed record SqlCipherVaultOpenResult(
	bool Success,
	string? CipherVersion,
	SqlCipherProviderPackagingPath ProviderPath,
	bool IsProductionCrediblePath,
	SqlCipherExporterFailureKind FailureKind,
	string? Message);
