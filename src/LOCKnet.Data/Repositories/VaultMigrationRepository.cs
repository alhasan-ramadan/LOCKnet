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
		cmd.CommandText = "SELECT Id, Title, Username, EncryptedPassword, CredentialUuid, SecretFormatVersion, URL, Notes, CreatedAt, UpdatedAt, IconKey, CredentialType FROM Credentials ORDER BY Id;";

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
                    SET EncryptedPassword = $password,
                        CredentialUuid = $credentialUuid,
                        SecretFormatVersion = $secretFormatVersion,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE Id = $id;";
				updateCredential.Parameters.AddWithValue("$id", credential.Id);
				updateCredential.Parameters.AddWithValue("$password", credential.EncryptedPassword);
				updateCredential.Parameters.AddWithValue("$credentialUuid", credential.CredentialUuid);
				updateCredential.Parameters.AddWithValue("$secretFormatVersion", credential.SecretFormatVersion);
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
		CredentialUuid = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
		SecretFormatVersion = reader.IsDBNull(5) ? CredentialSecretFormatVersion.Legacy : reader.GetInt32(5),
		Url = reader.IsDBNull(6) ? null : reader.GetString(6),
		Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
		CreatedAt = reader.GetDateTime(8),
		UpdatedAt = reader.GetDateTime(9),
		IconKey = reader.IsDBNull(10) ? null : reader.GetString(10),
		CredentialType = reader.IsDBNull(11) ? CredentialType.Password : (CredentialType)reader.GetInt32(11),
	};
}
