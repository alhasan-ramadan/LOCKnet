using LOCKnet.Core.DataAbstractions;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

/// <summary>
/// SQLite-Implementierung von <see cref="IVaultMigrationRepository"/> fuer atomare Header- und Credential-Migrationen.
/// </summary>
public sealed class VaultMigrationRepository : RepositoryBase, IVaultMigrationRepository
{
	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="VaultMigrationRepository"/>.
	/// </summary>
	public VaultMigrationRepository(string connectionString) : base(connectionString)
	{
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
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE Id = 1;";
				updateHeader.Parameters.AddWithValue("$hash", header.LegacyPasswordHash);
				updateHeader.Parameters.AddWithValue("$formatVersion", header.FormatVersion);
				updateHeader.Parameters.AddWithValue("$kdfIdentifier", header.KdfIdentifier);
				updateHeader.Parameters.AddWithValue("$kdfParameters", header.KdfParameters.Serialize());
				updateHeader.Parameters.AddWithValue("$salt", header.Salt);
				updateHeader.Parameters.AddWithValue("$wrappedVaultKey", header.WrappedVaultKey);
				updateHeader.Parameters.AddWithValue("$usesLegacyKeyMaterial", header.UsesLegacyKeyMaterial ? 1 : 0);
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
