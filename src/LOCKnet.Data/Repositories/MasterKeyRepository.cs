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
            INSERT INTO MasterKey (Id, PasswordHash, FormatVersion, KdfIdentifier, KdfParameters, Salt, WrappedVaultKey, UsesLegacyKeyMaterial, RequiresStorageCompaction)
            VALUES (1, $hash, $formatVersion, $kdfIdentifier, $kdfParameters, $salt, $wrappedVaultKey, $usesLegacyKeyMaterial, $requiresStorageCompaction);";
		cmd.Parameters.AddWithValue("$hash", header.LegacyPasswordHash);
		cmd.Parameters.AddWithValue("$formatVersion", header.FormatVersion);
		cmd.Parameters.AddWithValue("$kdfIdentifier", header.KdfIdentifier);
		cmd.Parameters.AddWithValue("$kdfParameters", header.KdfParameters.Serialize());
		cmd.Parameters.AddWithValue("$salt", header.Salt);
		cmd.Parameters.AddWithValue("$wrappedVaultKey", (object?)header.WrappedVaultKey ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$usesLegacyKeyMaterial", header.UsesLegacyKeyMaterial ? 1 : 0);
		cmd.Parameters.AddWithValue("$requiresStorageCompaction", header.RequiresStorageCompaction ? 1 : 0);
		cmd.ExecuteNonQuery();
	}

	/// <inheritdoc/>
	public VaultHeader? Get()
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT PasswordHash, FormatVersion, KdfIdentifier, KdfParameters, Salt, WrappedVaultKey, UsesLegacyKeyMaterial, RequiresStorageCompaction, CreatedAt, UpdatedAt FROM MasterKey WHERE Id = 1 LIMIT 1;";

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
			CreatedAt = reader.GetDateTime(8),
			UpdatedAt = reader.GetDateTime(9),
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
