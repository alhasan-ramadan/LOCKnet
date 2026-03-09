using LOCKnet.Core.DataAbstractions;
using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;

namespace LOCKnet.Data.Repositories;

/// <summary>
/// SQLite-Implementierung von <see cref="IVaultMigrationRepository"/> fuer atomare Header- und Credential-Migrationen.
/// </summary>
public sealed class VaultMigrationRepository : RepositoryBase, IVaultMigrationRepository
{
	private readonly StorageRewriteHooks? _rewriteHooks;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="VaultMigrationRepository"/>.
	/// </summary>
	public VaultMigrationRepository(string connectionString) : this(connectionString, null)
	{
	}

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="VaultMigrationRepository"/>.
	/// </summary>
	/// <param name="connectionFactory">Factory fuer Storage-spezifische SQLite-Verbindungen.</param>
	public VaultMigrationRepository(ISqliteConnectionFactory connectionFactory) : this(connectionFactory, null)
	{
	}

	internal VaultMigrationRepository(string connectionString, StorageRewriteHooks? rewriteHooks) : base(connectionString)
	{
		_rewriteHooks = rewriteHooks;
	}

	internal VaultMigrationRepository(ISqliteConnectionFactory connectionFactory, StorageRewriteHooks? rewriteHooks) : base(connectionFactory)
	{
		_rewriteHooks = rewriteHooks;
	}

	/// <inheritdoc/>
	public IReadOnlyList<CredentialRecord> GetAllCredentials()
	{
		var list = new List<CredentialRecord>();
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT Id, Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion, URL, Notes, CreatedAt, UpdatedAt, IconKey, CredentialType FROM Credentials ORDER BY Id;";

		using var reader = cmd.ExecuteReader();
		while (reader.Read())
			list.Add(MapCredential(reader));

		return list;
	}

	/// <inheritdoc/>
	public void ApplyMigration(VaultHeader header, IReadOnlyList<CredentialRecord> credentials)
	{
		ArgumentNullException.ThrowIfNull(header);
		ArgumentNullException.ThrowIfNull(credentials);

		foreach (var credential in credentials)
			StoredCredentialGuard.ValidateForPersistence(credential);

		using var conn = GetConnection();
		ConfigureMigrationConnection(conn);

		var began = false;
		try
		{
			using (var begin = conn.CreateCommand())
			{
				begin.CommandText = "BEGIN EXCLUSIVE;";
				begin.ExecuteNonQuery();
				began = true;
			}

			foreach (var credential in credentials)
			{
				using var updateCredential = conn.CreateCommand();
				updateCredential.CommandText = @"
                    UPDATE Credentials
                    SET Title = $title,
                        Username = $username,
                        EncryptedPassword = $password,
                        EncryptedMetadata = $encryptedMetadata,
                        CredentialUuid = $credentialUuid,
                        SecretFormatVersion = $secretFormatVersion,
                        MetadataFormatVersion = $metadataFormatVersion,
                        URL = $url,
                        Notes = $notes,
                        IconKey = $iconKey,
                        CredentialType = $credentialType,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE Id = $id;";
				updateCredential.Parameters.AddWithValue("$id", credential.Id);
				updateCredential.Parameters.AddWithValue("$title", credential.Title);
				updateCredential.Parameters.AddWithValue("$username", (object?)credential.Username ?? DBNull.Value);
				updateCredential.Parameters.AddWithValue("$password", credential.EncryptedPassword);
				updateCredential.Parameters.AddWithValue("$encryptedMetadata", (object?)credential.EncryptedMetadata ?? DBNull.Value);
				updateCredential.Parameters.AddWithValue("$credentialUuid", credential.CredentialUuid);
				updateCredential.Parameters.AddWithValue("$secretFormatVersion", credential.SecretFormatVersion);
				updateCredential.Parameters.AddWithValue("$metadataFormatVersion", credential.MetadataFormatVersion);
				updateCredential.Parameters.AddWithValue("$url", (object?)credential.Url ?? DBNull.Value);
				updateCredential.Parameters.AddWithValue("$notes", (object?)credential.Notes ?? DBNull.Value);
				updateCredential.Parameters.AddWithValue("$iconKey", (object?)credential.IconKey ?? DBNull.Value);
				updateCredential.Parameters.AddWithValue("$credentialType", (int)credential.CredentialType);
				updateCredential.ExecuteNonQuery();
			}

			using (var updateHeader = conn.CreateCommand())
			{
				updateHeader.CommandText = @"
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
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE Id = 1;";
				updateHeader.Parameters.AddWithValue("$hash", header.LegacyPasswordHash);
				updateHeader.Parameters.AddWithValue("$formatVersion", header.FormatVersion);
				updateHeader.Parameters.AddWithValue("$kdfIdentifier", header.KdfIdentifier);
				updateHeader.Parameters.AddWithValue("$kdfParameters", header.KdfParameters.Serialize());
				updateHeader.Parameters.AddWithValue("$salt", header.Salt);
				updateHeader.Parameters.AddWithValue("$wrappedVaultKey", header.WrappedVaultKey);
				updateHeader.Parameters.AddWithValue("$usesLegacyKeyMaterial", header.UsesLegacyKeyMaterial ? 1 : 0);
				updateHeader.Parameters.AddWithValue("$requiresStorageCompaction", header.RequiresStorageCompaction ? 1 : 0);
				updateHeader.Parameters.AddWithValue("$lastStorageCompactionAttemptUtc", (object?)header.LastStorageCompactionAttemptUtc?.ToString("O") ?? DBNull.Value);
				updateHeader.Parameters.AddWithValue("$lastStorageCompactionFailureKind", (int)header.LastStorageCompactionFailureKind);
				updateHeader.Parameters.AddWithValue("$lastStorageCompactionError", (object?)header.LastStorageCompactionError ?? DBNull.Value);
				updateHeader.ExecuteNonQuery();
			}

			using var commit = conn.CreateCommand();
			commit.CommandText = "COMMIT;";
			commit.ExecuteNonQuery();
			began = false;
		}
		catch
		{
			if (began)
			{
				try
				{
					using var rollback = conn.CreateCommand();
					rollback.CommandText = "ROLLBACK;";
					rollback.ExecuteNonQuery();
				}
				catch (SqliteException)
				{
				}
			}

			throw;
		}
	}

	/// <inheritdoc/>
	public bool HasPendingStorageArtifacts() => StorageRewriteArtifacts.HasPendingArtifacts(_databasePath);

	/// <inheritdoc/>
	public StorageCompactionInfo CompactStorage()
	{
		try
		{
			if (_databasePath is null)
			{
				return new StorageCompactionInfo
				{
					IsPending = true,
					FailureKind = StorageCompactionFailureKind.Unknown,
					UserMessage = "Speicherbereinigung noch offen: Fuer diese SQLite-Verbindung steht kein dateibasierter Rewrite zur Verfuegung.",
					LastError = "Dateibasierter Rewrite ist fuer nicht-dateibasierte SQLite-Verbindungen nicht verfuegbar."
				};
			}

			var primaryPath = _databasePath;
			var tempPath = StorageRewriteArtifacts.GetTempPath(primaryPath);
			var backupPath = StorageRewriteArtifacts.GetBackupPath(primaryPath);

			var artifactFinalization = TryFinalizeExistingArtifacts(primaryPath, tempPath, backupPath);
			if (artifactFinalization is not null)
				return artifactFinalization;

			if (File.Exists(tempPath) && !StorageRewriteArtifacts.TryDeleteFile(tempPath))
			{
				return new StorageCompactionInfo
				{
					IsPending = true,
					FailureKind = StorageCompactionFailureKind.BusyOrLocked,
					UserMessage = "Speicherbereinigung noch offen: Ein altes Rewrite-Artefakt konnte nicht entfernt werden.",
					LastError = $"Rewrite-Tempdatei konnte nicht entfernt werden: {tempPath}"
				};
			}

			if (File.Exists(backupPath) && !StorageRewriteArtifacts.TryDeleteFile(backupPath))
			{
				return new StorageCompactionInfo
				{
					IsPending = true,
					FailureKind = StorageCompactionFailureKind.BusyOrLocked,
					UserMessage = "Speicherbereinigung noch offen: Eine alte Rewrite-Sicherung blockiert einen neuen Bereinigungsversuch.",
					LastError = $"Rewrite-Sicherung konnte nicht entfernt werden: {backupPath}"
				};
			}

			_rewriteHooks?.BeforeVacuumInto?.Invoke(tempPath);
			BuildRewriteCandidate(tempPath);
			VerifyRewriteCandidate(tempPath);
			_rewriteHooks?.AfterVacuumInto?.Invoke(tempPath);

			StorageRewriteArtifacts.ReplacePrimaryDatabase(tempPath, primaryPath, backupPath);
			_rewriteHooks?.AfterReplace?.Invoke(primaryPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? backupPath : null);

			if (File.Exists(backupPath) && !StorageRewriteArtifacts.TryDeleteFile(backupPath))
			{
				return new StorageCompactionInfo
				{
					IsPending = true,
					FailureKind = StorageCompactionFailureKind.BusyOrLocked,
					UserMessage = "Speicherbereinigung noch offen: Die alte Vault-Datei konnte nach dem Rewrite noch nicht entfernt werden.",
					LastError = $"Rewrite-Sicherung konnte nach dem Austausch nicht entfernt werden: {backupPath}"
				};
			}

			if (File.Exists(tempPath) && !StorageRewriteArtifacts.TryDeleteFile(tempPath))
			{
				return new StorageCompactionInfo
				{
					IsPending = true,
					FailureKind = StorageCompactionFailureKind.BusyOrLocked,
					UserMessage = "Speicherbereinigung noch offen: Das temporare Rewrite-Artefakt konnte nach dem Austausch nicht entfernt werden.",
					LastError = $"Rewrite-Tempdatei konnte nach dem Austausch nicht entfernt werden: {tempPath}"
				};
			}

			return new StorageCompactionInfo
			{
				IsPending = false,
				FailureKind = StorageCompactionFailureKind.None,
				UserMessage = "Speicherbereinigung durch Rewrite abgeschlossen.",
			};
		}
		catch (SqliteException ex)
		{
			var (failureKind, userMessage) = MapCompactionFailure(ex);
			return new StorageCompactionInfo
			{
				IsPending = true,
				FailureKind = failureKind,
				UserMessage = userMessage,
				LastError = ex.Message,
			};
		}
		catch (InvalidOperationException ex)
		{
			return new StorageCompactionInfo
			{
				IsPending = true,
				FailureKind = StorageCompactionFailureKind.Corruption,
				UserMessage = "Speicherbereinigung noch offen: Die neu geschriebene Vault-Datei ist inkonsistent. Backup pruefen.",
				LastError = ex.Message,
			};
		}
		catch (IOException ex)
		{
			return new StorageCompactionInfo
			{
				IsPending = true,
				FailureKind = StorageCompactionFailureKind.Io,
				UserMessage = "Speicherbereinigung noch offen: Die Vault-Datei konnte nicht sicher neu geschrieben oder ersetzt werden.",
				LastError = ex.Message,
			};
		}
		catch (UnauthorizedAccessException ex)
		{
			return new StorageCompactionInfo
			{
				IsPending = true,
				FailureKind = StorageCompactionFailureKind.BusyOrLocked,
				UserMessage = "Speicherbereinigung noch offen: Die Vault-Datei ist noch gesperrt oder nicht schreibbar.",
				LastError = ex.Message,
			};
		}
	}

	private static (StorageCompactionFailureKind failureKind, string userMessage) MapCompactionFailure(SqliteException ex)
		=> ex.SqliteErrorCode switch
		{
			5 or 6 => (StorageCompactionFailureKind.BusyOrLocked, "Speicherbereinigung noch offen: Die Vault-Datei ist gerade gesperrt. Andere Apps schliessen und erneut versuchen."),
			10 => (StorageCompactionFailureKind.Io, "Speicherbereinigung noch offen: Beim Rewrite der Vault-Datei ist ein I/O-Fehler aufgetreten."),
			11 or 26 => (StorageCompactionFailureKind.Corruption, "Speicherbereinigung noch offen: Die Datenbank meldet Integritaetsprobleme. Backup pruefen."),
			13 => (StorageCompactionFailureKind.InsufficientSpace, "Speicherbereinigung noch offen: Fuer den Rewrite ist nicht genug freier Speicherplatz verfuegbar."),
			_ => (StorageCompactionFailureKind.Unknown, "Speicherbereinigung noch offen: SQLite konnte den Rewrite nicht abschliessen."),
		};

	private StorageCompactionInfo? TryFinalizeExistingArtifacts(string primaryPath, string tempPath, string backupPath)
	{
		var mainValid = StorageRewriteArtifacts.IsUsableSqliteDatabase(primaryPath);

		if (File.Exists(backupPath))
		{
			if (!mainValid)
			{
				return new StorageCompactionInfo
				{
					IsPending = true,
					FailureKind = StorageCompactionFailureKind.Corruption,
					UserMessage = "Speicherbereinigung noch offen: Vorhandene Rewrite-Artefakte muessen beim Neustart wiederhergestellt werden.",
					LastError = "Rewrite-Sicherung vorhanden, aber die Hauptdatenbank ist momentan nicht gueltig."
				};
			}

			if (!StorageRewriteArtifacts.TryDeleteFile(backupPath))
			{
				return new StorageCompactionInfo
				{
					IsPending = true,
					FailureKind = StorageCompactionFailureKind.BusyOrLocked,
					UserMessage = "Speicherbereinigung noch offen: Die alte Rewrite-Sicherung konnte noch nicht entfernt werden.",
					LastError = $"Rewrite-Sicherung konnte nicht entfernt werden: {backupPath}"
				};
			}

			if (File.Exists(tempPath))
				StorageRewriteArtifacts.TryDeleteFile(tempPath);

			return new StorageCompactionInfo
			{
				IsPending = false,
				FailureKind = StorageCompactionFailureKind.None,
				UserMessage = "Speicherbereinigung abgeschlossen.",
			};
		}

		if (File.Exists(tempPath) && mainValid)
		{
			if (!StorageRewriteArtifacts.TryDeleteFile(tempPath))
			{
				return new StorageCompactionInfo
				{
					IsPending = true,
					FailureKind = StorageCompactionFailureKind.BusyOrLocked,
					UserMessage = "Speicherbereinigung noch offen: Ein unvollstaendiges Rewrite-Artefakt konnte noch nicht entfernt werden.",
					LastError = $"Rewrite-Tempdatei konnte nicht entfernt werden: {tempPath}"
				};
			}
		}

		return null;
	}

	private void BuildRewriteCandidate(string tempPath)
	{
		using var conn = GetConnection();
		ConfigureMigrationConnection(conn);

		using (var checkpoint = conn.CreateCommand())
		{
			checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
			checkpoint.ExecuteNonQuery();
		}

		using var cmd = conn.CreateCommand();
		cmd.CommandText = $"VACUUM INTO {ToSqliteStringLiteral(tempPath)};";
		cmd.ExecuteNonQuery();
	}

	private static void VerifyRewriteCandidate(string tempPath)
	{
		if (!StorageRewriteArtifacts.IsUsableSqliteDatabase(tempPath))
			throw new InvalidOperationException("Rewrite-Zieldatei ist keine verwendbare SQLite-Datenbank.");

		var builder = new SqliteConnectionStringBuilder
		{
			DataSource = tempPath,
			Mode = SqliteOpenMode.ReadOnly,
		};

		using var connection = new SqliteConnection(builder.ToString());
		connection.Open();

		using var masterKeyCount = connection.CreateCommand();
		masterKeyCount.CommandText = "SELECT COUNT(*) FROM MasterKey;";
		if (Convert.ToInt64(masterKeyCount.ExecuteScalar() ?? 0L) != 1)
			throw new InvalidOperationException("Rewrite-Zieldatei enthaelt keinen konsistenten MasterKey-Header.");
	}

	private static void ConfigureMigrationConnection(SqliteConnection conn)
	{
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
                PRAGMA journal_mode = DELETE;
                PRAGMA synchronous = FULL;
                PRAGMA locking_mode = EXCLUSIVE;
                PRAGMA busy_timeout = 5000;";
		cmd.ExecuteNonQuery();
	}

	private static string ToSqliteStringLiteral(string value) => $"'{value.Replace("'", "''")}'";

	private static CredentialRecord MapCredential(SqliteDataReader reader) => new()
	{
		Id = reader.GetInt32(0),
		Title = reader.GetString(1),
		Username = reader.IsDBNull(2) ? null : reader.GetString(2),
		EncryptedPassword = (byte[])reader[3],
		EncryptedMetadata = reader.IsDBNull(4) ? [] : (byte[])reader[4],
		CredentialUuid = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
		SecretFormatVersion = reader.IsDBNull(6) ? CredentialSecretFormatVersion.Legacy : reader.GetInt32(6),
		MetadataFormatVersion = reader.IsDBNull(7) ? CredentialMetadataFormatVersion.Legacy : reader.GetInt32(7),
		Url = reader.IsDBNull(8) ? null : reader.GetString(8),
		Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
		CreatedAt = reader.GetDateTime(10),
		UpdatedAt = reader.GetDateTime(11),
		IconKey = reader.IsDBNull(12) ? null : reader.GetString(12),
		CredentialType = reader.IsDBNull(13) ? CredentialType.Password : (CredentialType)reader.GetInt32(13),
	};
}

internal sealed class StorageRewriteHooks
{
	public Action<string>? BeforeVacuumInto { get; init; }
	public Action<string>? AfterVacuumInto { get; init; }
	public Action<string, string?>? AfterReplace { get; init; }
}
