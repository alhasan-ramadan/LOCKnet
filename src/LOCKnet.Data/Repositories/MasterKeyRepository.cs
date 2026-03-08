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
	            INSERT INTO MasterKey (Id, PasswordHash, FormatVersion, KdfIdentifier, KdfParameters, Salt, WrappedVaultKey)
	            VALUES (1, $hash, $formatVersion, $kdfIdentifier, $kdfParameters, $salt, $wrappedVaultKey);";
		cmd.Parameters.AddWithValue("$hash", header.LegacyPasswordHash);
		cmd.Parameters.AddWithValue("$formatVersion", header.FormatVersion);
		cmd.Parameters.AddWithValue("$kdfIdentifier", header.KdfIdentifier);
		cmd.Parameters.AddWithValue("$kdfParameters", header.KdfParameters.Serialize());
		cmd.Parameters.AddWithValue("$salt", header.Salt);
		cmd.Parameters.AddWithValue("$wrappedVaultKey", (object?)header.WrappedVaultKey ?? DBNull.Value);
		cmd.ExecuteNonQuery();
	}

	/// <inheritdoc/>
	public VaultHeader? Get()
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT PasswordHash, FormatVersion, KdfIdentifier, KdfParameters, Salt, WrappedVaultKey, CreatedAt, UpdatedAt FROM MasterKey WHERE Id = 1 LIMIT 1;";

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
			CreatedAt = reader.GetDateTime(6),
			UpdatedAt = reader.GetDateTime(7),
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
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = 1;";
		cmd.Parameters.AddWithValue("$hash", header.LegacyPasswordHash);
		cmd.Parameters.AddWithValue("$formatVersion", header.FormatVersion);
		cmd.Parameters.AddWithValue("$kdfIdentifier", header.KdfIdentifier);
		cmd.Parameters.AddWithValue("$kdfParameters", header.KdfParameters.Serialize());
		cmd.Parameters.AddWithValue("$salt", header.Salt);
		cmd.Parameters.AddWithValue("$wrappedVaultKey", (object?)header.WrappedVaultKey ?? DBNull.Value);
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
