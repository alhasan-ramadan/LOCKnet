using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;
using System.Reflection;

namespace LOCKnet.Data.Tests;

public sealed class SqlCipherEncryptedVaultMigrationExporterTests : IDisposable
{
	private const string Password = "LOCKnet-Spike-Key-01!";
	private readonly string _tempDirectory;
	private readonly string _plainDatabasePath;
	private readonly string _exportedDatabasePath;
	private readonly PlainSqliteConnectionFactory _plainFactory;
	private readonly MasterKeyRepository _masterKeyRepository;
	private readonly CredentialsRepository _credentialsRepository;
	private readonly SettingsRepository _settingsRepository;

	public SqlCipherEncryptedVaultMigrationExporterTests()
	{
		_tempDirectory = Path.Combine(Path.GetTempPath(), $"locknet-sqlcipher-spike-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDirectory);
		_plainDatabasePath = Path.Combine(_tempDirectory, "plain.db");
		_exportedDatabasePath = Path.Combine(_tempDirectory, "encrypted.db");
		_plainFactory = new PlainSqliteConnectionFactory(_plainDatabasePath);
		new Database(_plainFactory).Initialize();
		_masterKeyRepository = new MasterKeyRepository(_plainFactory);
		_credentialsRepository = new CredentialsRepository(_plainFactory);
		_settingsRepository = new SettingsRepository(_plainFactory);
		SeedPlainVault();
	}

	public void Dispose()
	{
		TryDelete(_plainDatabasePath);
		TryDelete(_exportedDatabasePath);
		TryDelete(PlainToEncryptedVaultMigrationArtifacts.GetEncryptedTempPath(_plainDatabasePath));
		TryDelete(PlainToEncryptedVaultMigrationArtifacts.GetPlainBackupPath(_plainDatabasePath));
		if (Directory.Exists(_tempDirectory))
		{
			try
			{
				Directory.Delete(_tempDirectory, recursive: true);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void ProbeRuntime_LoadsSqlCipherProvider()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);

		var runtime = exporter.ProbeRuntime();

		Assert.True(runtime.IsAvailable, runtime.Message);
		Assert.Equal(SqlCipherExporterFailureKind.None, runtime.FailureKind);
		Assert.True(
			runtime.ProviderPath is SqlCipherProviderPackagingPath.LegacyBundleESqlCipher or SqlCipherProviderPackagingPath.BundleZeteticWithProviderSqlcipher,
			$"Unexpected provider path: {runtime.ProviderPath}");
		Assert.Equal(runtime.ProviderPath == SqlCipherProviderPackagingPath.OfficialZetetic, runtime.IsProductionCrediblePath);
		Assert.False(string.IsNullOrWhiteSpace(runtime.CipherVersion));
	}

	[Fact]
	public void ProbeRuntime_WhenProviderCannotLoad_IsClassifiedExplicitly()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(
			Password,
			() => new SqlCipherRuntimeProbeResult(
				false,
				null,
				null,
				SqlCipherProviderPackagingPath.LegacyBundleESqlCipher,
				false,
				SqlCipherExporterFailureKind.NativeProviderLoadFailure,
				"provider missing"));

		var runtime = exporter.ProbeRuntime();

		Assert.False(runtime.IsAvailable);
		Assert.Equal(SqlCipherExporterFailureKind.NativeProviderLoadFailure, runtime.FailureKind);
		Assert.Equal("provider missing", runtime.Message);
	}

	[Fact]
	public void TryOpenEncryptedVault_WhenRuntimeProbeFails_ReturnsProviderLoadFailure()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(
			Password,
			() => new SqlCipherRuntimeProbeResult(
				false,
				null,
				null,
				SqlCipherProviderPackagingPath.LegacyBundleESqlCipher,
				false,
				SqlCipherExporterFailureKind.NativeProviderLoadFailure,
				"runtime init failed"));

		var result = exporter.TryOpenEncryptedVault(_exportedDatabasePath, Password);

		Assert.False(result.Success);
		Assert.Equal(SqlCipherExporterFailureKind.NativeProviderLoadFailure, result.FailureKind);
	}

	[Fact]
	public void ExportPlaintextVault_CreatesEncryptedTargetAndReopensWithCorrectKey()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);

		exporter.ExportPlaintextVault(_plainFactory.Storage.ConnectionString, _exportedDatabasePath);
		exporter.ValidateExportedVault(_exportedDatabasePath);
		var open = exporter.TryOpenEncryptedVault(_exportedDatabasePath, Password);

		Assert.True(open.Success, open.Message);
		Assert.False(HasPlainSqliteHeader(_exportedDatabasePath));
		Assert.Equal("classic", ReadSettingValue(_exportedDatabasePath, Password, "theme"));
	}

	[Fact]
	public void TryOpenEncryptedVault_WithWrongKey_ReturnsWrongKeyFailure()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);
		exporter.ExportPlaintextVault(_plainFactory.Storage.ConnectionString, _exportedDatabasePath);

		var open = exporter.TryOpenEncryptedVault(_exportedDatabasePath, "wrong-password");

		Assert.False(open.Success);
		Assert.Equal(SqlCipherExporterFailureKind.WrongKey, open.FailureKind);
		Assert.True(
			open.ProviderPath is SqlCipherProviderPackagingPath.LegacyBundleESqlCipher or SqlCipherProviderPackagingPath.BundleZeteticWithProviderSqlcipher,
			$"Unexpected provider path: {open.ProviderPath}");
	}

	[Fact]
	public void ValidateExportedVault_CorruptedTarget_ThrowsWrongKeyOrInvalidTarget()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);
		exporter.ExportPlaintextVault(_plainFactory.Storage.ConnectionString, _exportedDatabasePath);
		OverwriteFileWithRetries(_exportedDatabasePath, [0x01, 0x02, 0x03, 0x04]);

		var ex = Assert.Throws<SqlCipherEncryptedVaultMigrationException>(() => exporter.ValidateExportedVault(_exportedDatabasePath));

		Assert.True(
			ex.FailureKind == SqlCipherExporterFailureKind.InvalidTarget ||
			ex.FailureKind == SqlCipherExporterFailureKind.WrongKey,
			$"Unexpected failure kind: {ex.FailureKind}");
	}

	[Fact]
	public void CoordinatorExecute_WithRealExporter_HappyPath_ReplacesPrimaryAndKeepsPlainBackupWhenPresent()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_plainFactory);

		var result = coordinator.Execute(MakeRequest(), exporter);
		var open = exporter.TryOpenEncryptedVault(_plainDatabasePath, Password);
		var migrationState = ReadEncryptedMigrationState(_plainDatabasePath, Password);

		Assert.True(open.Success, open.Message);
		Assert.Equal(result.StorageMigrationState, migrationState.State);
		Assert.Equal(result.StorageMigrationTargetMode, migrationState.TargetMode);
		Assert.Equal(result.LastStorageMigrationError, migrationState.LastError);
		Assert.Equal(result.LastStorageMigrationAttemptUtc.HasValue, migrationState.LastAttemptUtc.HasValue);
		Assert.False(HasPlainSqliteHeader(_plainDatabasePath));
		Assert.Equal(File.Exists(result.PlainBackupPath), result.StorageMigrationState == VaultStorageMigrationState.FinalizationPending);
	}

	[Fact]
	public void CoordinatorFinalize_WithRealExporter_RemovesBackupAndClearsMigrationStateWhenBackupExists()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_plainFactory);
		var execute = coordinator.Execute(MakeRequest(), exporter);
		if (!File.Exists(execute.PlainBackupPath))
			return;

		var header = ReadEncryptedHeader(_plainDatabasePath, Password);
		var result = coordinator.FinalizeSuccessfulMigration(header, exporter);
		var cleared = ReadEncryptedMigrationState(_plainDatabasePath, Password);

		Assert.Equal(VaultStorageMigrationState.None, result.StorageMigrationState);
		Assert.Equal(VaultStorageMigrationState.None, cleared.State);
		Assert.Equal(VaultStorageMigrationTargetMode.None, cleared.TargetMode);
		Assert.False(File.Exists(execute.PlainBackupPath));
	}

	[Fact]
	public void CoordinatorExecute_WhenValidationFails_PreservesOriginalPlainVault()
	{
		var realExporter = new SqlCipherEncryptedVaultMigrationExporter(Password);
		var coordinator = new PlainToEncryptedVaultMigrationCoordinator(_plainFactory);
		var exporter = new ThrowingValidationExporter(realExporter);

		var result = coordinator.Execute(MakeRequest(), exporter);
		long masterKeyCount;
		using (var connection = new SqliteConnection($"Data Source={_plainDatabasePath}"))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT COUNT(*) FROM MasterKey;";
			masterKeyCount = Convert.ToInt64(command.ExecuteScalar() ?? 0L);
		}

		Assert.Equal(VaultStorageMigrationState.Failed, result.StorageMigrationState);
		Assert.NotNull(result.LastStorageMigrationError);
		Assert.Equal(1L, masterKeyCount);
		Assert.True(HasPlainSqliteHeader(_plainDatabasePath));
	}

	[Fact]
	public void ValidateExportedVault_WhenTargetFileMissing_ThrowsInvalidTarget()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);

		var ex = Assert.Throws<SqlCipherEncryptedVaultMigrationException>(() => exporter.ValidateExportedVault(_exportedDatabasePath));

		Assert.Equal(SqlCipherExporterFailureKind.InvalidTarget, ex.FailureKind);
	}

	[Fact]
	public void ValidateExportedVault_WhenMasterKeyRowMissing_ThrowsValidationFailure()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);
		exporter.ExportPlaintextVault(_plainFactory.Storage.ConnectionString, _exportedDatabasePath);

		using (var connection = OpenEncryptedConnectionReadWrite(_exportedDatabasePath, Password))
		{
			using var command = connection.CreateCommand();
			command.CommandText = "DELETE FROM MasterKey;";
			command.ExecuteNonQuery();
		}

		var ex = Assert.Throws<SqlCipherEncryptedVaultMigrationException>(() => exporter.ValidateExportedVault(_exportedDatabasePath));

		Assert.Equal(SqlCipherExporterFailureKind.ValidationFailure, ex.FailureKind);
		Assert.Contains("MasterKey", ex.Message);
	}

	[Fact]
	public void ValidateExportedVault_WhenRequiredTableMissing_ThrowsValidationFailure()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);
		exporter.ExportPlaintextVault(_plainFactory.Storage.ConnectionString, _exportedDatabasePath);

		using (var connection = OpenEncryptedConnectionReadWrite(_exportedDatabasePath, Password))
		{
			using var command = connection.CreateCommand();
			command.CommandText = "DROP TABLE Settings;";
			command.ExecuteNonQuery();
		}

		var ex = Assert.Throws<SqlCipherEncryptedVaultMigrationException>(() => exporter.ValidateExportedVault(_exportedDatabasePath));

		Assert.Equal(SqlCipherExporterFailureKind.ValidationFailure, ex.FailureKind);
		Assert.Contains("Settings", ex.Message);
	}

	[Fact]
	public void ExportPlaintextVault_WhenDestinationCannotBeCreated_ThrowsMigrationExportFailure()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);

		var ex = Assert.Throws<SqlCipherEncryptedVaultMigrationException>(() => exporter.ExportPlaintextVault(_plainFactory.Storage.ConnectionString, _tempDirectory));

		Assert.Equal(SqlCipherExporterFailureKind.MigrationExportFailure, ex.FailureKind);
	}

	[Fact]
	public void TryOpenEncryptedVault_WhenTargetFileMissing_ReturnsInvalidTarget()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(Password);

		var result = exporter.TryOpenEncryptedVault(_exportedDatabasePath, Password);

		Assert.False(result.Success);
		Assert.Equal(SqlCipherExporterFailureKind.InvalidTarget, result.FailureKind);
	}

	[Fact]
	public void ExportPlaintextVault_WhenRuntimeUnavailable_ThrowsProviderFailure()
	{
		var exporter = new SqlCipherEncryptedVaultMigrationExporter(
			Password,
			() => new SqlCipherRuntimeProbeResult(
				false,
				null,
				null,
				SqlCipherProviderPackagingPath.LegacyBundleESqlCipher,
				false,
				SqlCipherExporterFailureKind.NativeProviderLoadFailure,
				"runtime unavailable"));

		var ex = Assert.Throws<SqlCipherEncryptedVaultMigrationException>(() => exporter.ExportPlaintextVault(_plainFactory.Storage.ConnectionString, _exportedDatabasePath));

		Assert.Equal(SqlCipherExporterFailureKind.NativeProviderLoadFailure, ex.FailureKind);
	}

	[Fact]
	public void ClassifyOpenFailure_PrivateMethod_HandlesSqliteMessagesAndInnerExceptions()
	{
		var method = typeof(SqlCipherEncryptedVaultMigrationExporter).GetMethod("ClassifyOpenFailure", BindingFlags.NonPublic | BindingFlags.Static)!;

		var sqliteError26 = new SqliteException("file is not a database", 26);
		var wrongKeyKind = (SqlCipherExporterFailureKind)method.Invoke(null, [sqliteError26])!;
		Assert.Equal(SqlCipherExporterFailureKind.WrongKey, wrongKeyKind);

		var sqliteInvalid = new SqliteException("generic sqlite failure", 1);
		var invalidTargetKind = (SqlCipherExporterFailureKind)method.Invoke(null, [sqliteInvalid])!;
		Assert.Equal(SqlCipherExporterFailureKind.InvalidTarget, invalidTargetKind);

		var nested = new InvalidOperationException("outer", new SqliteException("not a database", 1));
		var nestedKind = (SqlCipherExporterFailureKind)method.Invoke(null, [nested])!;
		Assert.Equal(SqlCipherExporterFailureKind.WrongKey, nestedKind);

		var unknown = (SqlCipherExporterFailureKind)method.Invoke(null, [new Exception("other")])!;
		Assert.Equal(SqlCipherExporterFailureKind.OperationalFailure, unknown);
	}

	[Fact]
	public void RuntimeProbeAndOpenResult_RecordsExposeAllProperties()
	{
		var probe = new SqlCipherRuntimeProbeResult(
			true,
			"4.5.0",
			"3.45.1",
			SqlCipherProviderPackagingPath.OfficialZetetic,
			true,
			SqlCipherExporterFailureKind.None,
			null);
		Assert.Equal("3.45.1", probe.SqliteVersion);

		var open = new SqlCipherVaultOpenResult(
			false,
			null,
			SqlCipherProviderPackagingPath.LegacyBundleESqlCipher,
			false,
			SqlCipherExporterFailureKind.InvalidTarget,
			"missing");
		Assert.Equal(SqlCipherProviderPackagingPath.LegacyBundleESqlCipher, open.ProviderPath);
		Assert.False(open.IsProductionCrediblePath);
	}

	private PlainToEncryptedVaultMigrationRequest MakeRequest()
		=> new(
			_masterKeyRepository.Get()!,
			_credentialsRepository.GetAll(),
			VaultStorageMigrationTargetMode.EncryptedSqlite,
			SourceValidatedWithActiveVaultKey: true);

	private void SeedPlainVault()
	{
		if (_masterKeyRepository.Get() is null)
		{
			_masterKeyRepository.Create(new VaultHeader
			{
				FormatVersion = VaultHeaderFormatVersion.Current,
				KdfIdentifier = "PBKDF2-SHA256",
				KdfParameters = new VaultKdfParameters(),
				Salt = Enumerable.Repeat((byte)0xAA, 32).ToArray(),
				WrappedVaultKey = Enumerable.Repeat((byte)0xCC, 60).ToArray(),
				LegacyPasswordHash = [],
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow,
			});
		}

		if (_credentialsRepository.GetAll().Count == 0)
		{
			_credentialsRepository.Add(new CredentialRecord
			{
				Title = string.Empty,
				Username = null,
				EncryptedPassword = [0x11, 0x22, 0x33],
				EncryptedMetadata = [0x44, 0x55, 0x66],
				CredentialUuid = Guid.NewGuid().ToString("N"),
				SecretFormatVersion = CredentialSecretFormatVersion.Current,
				MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
				CredentialType = CredentialType.Password,
			});
		}

		_settingsRepository.Set("theme", "classic");
	}

	private static bool HasPlainSqliteHeader(string databasePath)
	{
		for (var attempt = 0; attempt < 120; attempt++)
		{
			try
			{
				SqliteConnection.ClearAllPools();
				Thread.Sleep(25);
				var buffer = File.ReadAllBytes(databasePath);
				var header = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(16, buffer.Length));
				return header.StartsWith("SQLite format 3", StringComparison.Ordinal);
			}
			catch (IOException) when (attempt < 119)
			{
				Thread.Sleep(25);
			}
		}

		SqliteConnection.ClearAllPools();
		var finalBuffer = File.ReadAllBytes(databasePath);
		var finalHeader = System.Text.Encoding.ASCII.GetString(finalBuffer, 0, Math.Min(16, finalBuffer.Length));
		return finalHeader.StartsWith("SQLite format 3", StringComparison.Ordinal);
	}

	private static string? ReadSettingValue(string databasePath, string password, string key)
	{
		using var connection = OpenEncryptedConnection(databasePath, password);
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
		command.Parameters.AddWithValue("$key", key);
		return command.ExecuteScalar() as string;
	}

	private static (VaultStorageMigrationState State, VaultStorageMigrationTargetMode TargetMode, DateTime? LastAttemptUtc, string? LastError) ReadEncryptedMigrationState(string databasePath, string password)
	{
		using var connection = OpenEncryptedConnection(databasePath, password);
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT StorageMigrationState, StorageMigrationTargetMode, LastStorageMigrationAttemptUtc, LastStorageMigrationError FROM MasterKey WHERE Id = 1;";
		using var reader = command.ExecuteReader();
		Assert.True(reader.Read());
		return (
			(VaultStorageMigrationState)reader.GetInt32(0),
			(VaultStorageMigrationTargetMode)reader.GetInt32(1),
			reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
			reader.IsDBNull(3) ? null : reader.GetString(3));
	}

	private static VaultHeader ReadEncryptedHeader(string databasePath, string password)
	{
		using var connection = OpenEncryptedConnection(databasePath, password);
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT PasswordHash, FormatVersion, KdfIdentifier, KdfParameters, Salt, WrappedVaultKey, UsesLegacyKeyMaterial, RequiresStorageCompaction, LastStorageCompactionAttemptUtc, LastStorageCompactionFailureKind, LastStorageCompactionError, StorageMigrationState, StorageMigrationTargetMode, LastStorageMigrationAttemptUtc, LastStorageMigrationError, CreatedAt, UpdatedAt FROM MasterKey WHERE Id = 1;";
		using var reader = command.ExecuteReader();
		Assert.True(reader.Read());
		return new VaultHeader
		{
			LegacyPasswordHash = reader.IsDBNull(0) ? [] : (byte[])reader[0],
			FormatVersion = reader.GetInt32(1),
			KdfIdentifier = reader.GetString(2),
			KdfParameters = VaultKdfParameters.Deserialize(reader.GetString(3)),
			Salt = (byte[])reader[4],
			WrappedVaultKey = reader.IsDBNull(5) ? [] : (byte[])reader[5],
			UsesLegacyKeyMaterial = reader.GetInt32(6) != 0,
			RequiresStorageCompaction = reader.GetInt32(7) != 0,
			LastStorageCompactionAttemptUtc = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind),
			LastStorageCompactionFailureKind = (StorageCompactionFailureKind)reader.GetInt32(9),
			LastStorageCompactionError = reader.IsDBNull(10) ? null : reader.GetString(10),
			StorageMigrationState = (VaultStorageMigrationState)reader.GetInt32(11),
			StorageMigrationTargetMode = (VaultStorageMigrationTargetMode)reader.GetInt32(12),
			LastStorageMigrationAttemptUtc = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13), null, System.Globalization.DateTimeStyles.RoundtripKind),
			LastStorageMigrationError = reader.IsDBNull(14) ? null : reader.GetString(14),
			CreatedAt = reader.GetDateTime(15),
			UpdatedAt = reader.GetDateTime(16),
		};
	}

	private static SqliteConnection OpenEncryptedConnection(string databasePath, string password)
	{
		var builder = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadOnly,
			Password = password,
		};

		var connection = new SqliteConnection(builder.ToString());
		connection.Open();
		return connection;
	}

	private static SqliteConnection OpenEncryptedConnectionReadWrite(string databasePath, string password)
	{
		var builder = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadWrite,
			Password = password,
		};

		var connection = new SqliteConnection(builder.ToString());
		connection.Open();
		return connection;
	}

	private static void TryDelete(string path)
	{
		if (File.Exists(path))
		{
			try
			{
				File.Delete(path);
			}
			catch (IOException)
			{
			}
		}
	}

	private static void OverwriteFileWithRetries(string path, ReadOnlySpan<byte> bytes)
	{
		for (var attempt = 0; attempt < 120; attempt++)
		{
			try
			{
				SqliteConnection.ClearAllPools();
				Thread.Sleep(25);
				File.WriteAllBytes(path, bytes);
				return;
			}
			catch (IOException) when (attempt < 119)
			{
				Thread.Sleep(25);
			}
		}

		SqliteConnection.ClearAllPools();
		File.WriteAllBytes(path, bytes);
	}

	private sealed class ThrowingValidationExporter : IEncryptedVaultMigrationExporter
	{
		private readonly SqlCipherEncryptedVaultMigrationExporter _inner;

		public ThrowingValidationExporter(SqlCipherEncryptedVaultMigrationExporter inner)
		{
			_inner = inner;
		}

		public VaultStorageMigrationTargetMode TargetMode => _inner.TargetMode;

		public void ExportPlaintextVault(string sourceConnectionString, string destinationPath)
			=> _inner.ExportPlaintextVault(sourceConnectionString, destinationPath);

		public void ValidateExportedVault(string destinationPath)
			=> throw new SqlCipherEncryptedVaultMigrationException(SqlCipherExporterFailureKind.ValidationFailure, "Simulierter SQLCipher-Validierungsfehler.");

		public void PersistMigratedHeader(string databasePath, VaultHeader header)
			=> _inner.PersistMigratedHeader(databasePath, header);
	}
}
