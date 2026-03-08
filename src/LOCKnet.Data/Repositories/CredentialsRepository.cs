using LOCKnet.Core.DataAbstractions;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

/// <summary>
/// SQLite-Implementierung von <see cref="ICredentialRepository"/>.
/// </summary>
public class CredentialsRepository : RepositoryBase, ICredentialRepository
{
	/// <summary>Initialisiert eine neue Instanz von <see cref="CredentialsRepository"/>.</summary>
	/// <param name="connectionString">Der vollständige SQLite-Connection-String.</param>
	public CredentialsRepository(string connectionString) : base(connectionString) { }

	#region ICredentialRepository

	/// <inheritdoc/>
	public void Add(CredentialRecord credential)
	{
		StoredCredentialGuard.ValidateForPersistence(credential);

		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            INSERT INTO Credentials (Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion, URL, Notes, IconKey, CredentialType)
            VALUES ($title, $username, $password, $encryptedMetadata, $credentialUuid, $secretFormatVersion, $metadataFormatVersion, $url, $notes, $iconKey, $credentialType);";

		cmd.Parameters.AddWithValue("$title", credential.Title);
		cmd.Parameters.AddWithValue("$username", credential.Username ?? "");
		cmd.Parameters.AddWithValue("$password", credential.EncryptedPassword);
		cmd.Parameters.AddWithValue("$encryptedMetadata", (object?)credential.EncryptedMetadata ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$credentialUuid", credential.CredentialUuid);
		cmd.Parameters.AddWithValue("$secretFormatVersion", credential.SecretFormatVersion);
		cmd.Parameters.AddWithValue("$metadataFormatVersion", credential.MetadataFormatVersion);
		cmd.Parameters.AddWithValue("$url", credential.Url ?? "");
		cmd.Parameters.AddWithValue("$notes", credential.Notes ?? "");
		cmd.Parameters.AddWithValue("$iconKey", (object?)credential.IconKey ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$credentialType", (int)credential.CredentialType);

		cmd.ExecuteNonQuery();
	}

	/// <inheritdoc/>
	public IReadOnlyList<CredentialRecord> GetAll()
	{
		var list = new List<CredentialRecord>();
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT Id, Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion, URL, Notes, CreatedAt, UpdatedAt, IconKey, CredentialType FROM Credentials";

		using var reader = cmd.ExecuteReader();
		while (reader.Read())
			list.Add(MapRecord(reader));

		return list;
	}

	/// <inheritdoc/>
	public CredentialRecord? GetById(int id)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT Id, Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion, URL, Notes, CreatedAt, UpdatedAt, IconKey, CredentialType FROM Credentials WHERE Id = $id LIMIT 1;";
		cmd.Parameters.AddWithValue("$id", id);

		using var reader = cmd.ExecuteReader();
		return reader.Read() ? MapRecord(reader) : null;
	}

	/// <inheritdoc/>
	public void Update(CredentialRecord credential)
	{
		StoredCredentialGuard.ValidateForPersistence(credential);

		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
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

		cmd.Parameters.AddWithValue("$id", credential.Id);
		cmd.Parameters.AddWithValue("$title", credential.Title);
		cmd.Parameters.AddWithValue("$username", credential.Username ?? "");
		cmd.Parameters.AddWithValue("$password", credential.EncryptedPassword);
		cmd.Parameters.AddWithValue("$encryptedMetadata", (object?)credential.EncryptedMetadata ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$credentialUuid", credential.CredentialUuid);
		cmd.Parameters.AddWithValue("$secretFormatVersion", credential.SecretFormatVersion);
		cmd.Parameters.AddWithValue("$metadataFormatVersion", credential.MetadataFormatVersion);
		cmd.Parameters.AddWithValue("$url", credential.Url ?? "");
		cmd.Parameters.AddWithValue("$notes", credential.Notes ?? "");
		cmd.Parameters.AddWithValue("$iconKey", (object?)credential.IconKey ?? DBNull.Value);
		cmd.Parameters.AddWithValue("$credentialType", (int)credential.CredentialType);

		cmd.ExecuteNonQuery();
	}

	/// <inheritdoc/>
	public void Remove(int id)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "DELETE FROM Credentials WHERE Id = $id;";
		cmd.Parameters.AddWithValue("$id", id);
		cmd.ExecuteNonQuery();
	}

	#endregion

	private static CredentialRecord MapRecord(SqliteDataReader reader) => new()
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
