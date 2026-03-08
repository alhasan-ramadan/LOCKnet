using LOCKnet.Core.DataAbstractions;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

/// <summary>
/// SQLite-Implementierung von <see cref="IMasterKeyRepository"/>.
/// Persistiert einen einzelnen Vault-Header in der bestehenden MasterKey-Tabelle (Id = 1).
/// </summary>
public class MasterKeyRepository : RepositoryBase, IMasterKeyRepository
{
	/// <summary>Initialisiert eine neue Instanz von <see cref="MasterKeyRepository"/>.</summary>
	/// <param name="connectionString">Der vollständige SQLite-Connection-String.</param>
	public MasterKeyRepository(string connectionString) : base(connectionString) { }

	/// <summary>Initialisiert eine neue Instanz von <see cref="MasterKeyRepository"/>.</summary>
	/// <param name="connectionFactory">Factory fuer Storage-spezifische SQLite-Verbindungen.</param>
	public MasterKeyRepository(ISqliteConnectionFactory connectionFactory) : base(connectionFactory) { }

	#region IMasterKeyRepository

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">Vault-Header existiert bereits.</exception>
	public void Create(VaultHeader header)
	{
		if (Get() != null)
			throw new InvalidOperationException("Vault header already exists.");

		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
	            INSERT INTO MasterKey (Id, PasswordHash, FormatVersion, KdfIdentifier, KdfParameters, Salt, WrappedVaultKey, UsesLegacyKeyMaterial, RequiresStorageCompaction, LastStorageCompactionAttemptUtc, LastStorageCompactionFailureKind, LastStorageCompactionError, StorageMigrationState, StorageMigrationTargetMode, LastStorageMigrationAttemptUtc, LastStorageMigrationError)
	            VALUES (1, $hash, $formatVersion, $kdfIdentifier, $kdfParameters, $salt, $wrappedVaultKey, $usesLegacyKeyMaterial, $requiresStorageCompaction, $lastStorageCompactionAttemptUtc, $lastStorageCompactionFailureKind, $lastStorageCompactionError, $storageMigrationState, $storageMigrationTargetMode, $lastStorageMigrationAttemptUtc, $lastStorageMigrationError);";
		cmd.Parameters.AddWithValue("$hash", header.LegacyPasswordHash);
		cmd.Parameters.AddWithValue("$formatVersion", header.FormatVersion);
		cmd.Parameters.AddWithValue("$kdfIdentifier", header.KdfIdentifier);
		cmd.Parameters.AddWithValue("$kdfParameters", header.KdfParameters.Serialize());
		cmd.Parameters.AddWithValue("$salt", header.Salt);
		cmd.Parameters.AddWithValue("$wrappedVaultKey", (object?)header.WrappedVaultKey ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$usesLegacyKeyMaterial", header.UsesLegacyKeyMaterial ? 1 : 0);
		cmd.Parameters.AddWithValue("$requiresStorageCompaction", header.RequiresStorageCompaction ? 1 : 0);
		cmd.Parameters.AddWithValue("$lastStorageCompactionAttemptUtc", (object?)header.LastStorageCompactionAttemptUtc?.ToString("O") ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$lastStorageCompactionFailureKind", (int)header.LastStorageCompactionFailureKind);
		cmd.Parameters.AddWithValue("$lastStorageCompactionError", (object?)header.LastStorageCompactionError ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$storageMigrationState", (int)header.StorageMigrationState);
		cmd.Parameters.AddWithValue("$storageMigrationTargetMode", (int)header.StorageMigrationTargetMode);
		cmd.Parameters.AddWithValue("$lastStorageMigrationAttemptUtc", (object?)header.LastStorageMigrationAttemptUtc?.ToString("O") ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$lastStorageMigrationError", (object?)header.LastStorageMigrationError ?? DBNull.Value);
		cmd.ExecuteNonQuery();
	}

	/// <inheritdoc/>
	public VaultHeader? Get()
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT PasswordHash, FormatVersion, KdfIdentifier, KdfParameters, Salt, WrappedVaultKey, UsesLegacyKeyMaterial, RequiresStorageCompaction, LastStorageCompactionAttemptUtc, LastStorageCompactionFailureKind, LastStorageCompactionError, StorageMigrationState, StorageMigrationTargetMode, LastStorageMigrationAttemptUtc, LastStorageMigrationError, CreatedAt, UpdatedAt FROM MasterKey WHERE Id = 1 LIMIT 1;";

		using var reader = cmd.ExecuteReader();
		if (!reader.Read())
			return null;

		return new VaultHeader
		{
			LegacyPasswordHash = reader.IsDBNull(0) ? [] : (byte[])reader[0],
			FormatVersion = reader.IsDBNull(1) ? 1 : reader.GetInt32(1),
			KdfIdentifier = reader.IsDBNull(2) ? "PBKDF2-SHA256" : reader.GetString(2),
			KdfParameters = reader.IsDBNull(3)
				? new VaultKdfParameters()
				: VaultKdfParameters.Deserialize(reader.GetString(3)),
			Salt = (byte[])reader[4],
			WrappedVaultKey = reader.IsDBNull(5) ? [] : (byte[])reader[5],
			UsesLegacyKeyMaterial = !reader.IsDBNull(6) && reader.GetInt32(6) != 0,
			RequiresStorageCompaction = !reader.IsDBNull(7) && reader.GetInt32(7) != 0,
			LastStorageCompactionAttemptUtc = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind),
			LastStorageCompactionFailureKind = reader.IsDBNull(9) ? StorageCompactionFailureKind.None : (StorageCompactionFailureKind)reader.GetInt32(9),
			LastStorageCompactionError = reader.IsDBNull(10) ? null : reader.GetString(10),
			StorageMigrationState = reader.IsDBNull(11) ? VaultStorageMigrationState.None : (VaultStorageMigrationState)reader.GetInt32(11),
			StorageMigrationTargetMode = reader.IsDBNull(12) ? VaultStorageMigrationTargetMode.None : (VaultStorageMigrationTargetMode)reader.GetInt32(12),
			LastStorageMigrationAttemptUtc = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13), null, System.Globalization.DateTimeStyles.RoundtripKind),
			LastStorageMigrationError = reader.IsDBNull(14) ? null : reader.GetString(14),
			CreatedAt = reader.GetDateTime(15),
			UpdatedAt = reader.GetDateTime(16),
		};
	}

	/// <inheritdoc/>
	public void Update(VaultHeader header)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
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
		cmd.Parameters.AddWithValue("$hash", header.LegacyPasswordHash);
		cmd.Parameters.AddWithValue("$formatVersion", header.FormatVersion);
		cmd.Parameters.AddWithValue("$kdfIdentifier", header.KdfIdentifier);
		cmd.Parameters.AddWithValue("$kdfParameters", header.KdfParameters.Serialize());
		cmd.Parameters.AddWithValue("$salt", header.Salt);
		cmd.Parameters.AddWithValue("$wrappedVaultKey", (object?)header.WrappedVaultKey ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$usesLegacyKeyMaterial", header.UsesLegacyKeyMaterial ? 1 : 0);
		cmd.Parameters.AddWithValue("$requiresStorageCompaction", header.RequiresStorageCompaction ? 1 : 0);
		cmd.Parameters.AddWithValue("$lastStorageCompactionAttemptUtc", (object?)header.LastStorageCompactionAttemptUtc?.ToString("O") ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$lastStorageCompactionFailureKind", (int)header.LastStorageCompactionFailureKind);
		cmd.Parameters.AddWithValue("$lastStorageCompactionError", (object?)header.LastStorageCompactionError ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$storageMigrationState", (int)header.StorageMigrationState);
		cmd.Parameters.AddWithValue("$storageMigrationTargetMode", (int)header.StorageMigrationTargetMode);
		cmd.Parameters.AddWithValue("$lastStorageMigrationAttemptUtc", (object?)header.LastStorageMigrationAttemptUtc?.ToString("O") ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$lastStorageMigrationError", (object?)header.LastStorageMigrationError ?? DBNull.Value);
		cmd.ExecuteNonQuery();
	}

	/// <inheritdoc/>
	public void Delete()
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "DELETE FROM MasterKey WHERE Id = 1;";
		cmd.ExecuteNonQuery();
	}

	#endregion
}
