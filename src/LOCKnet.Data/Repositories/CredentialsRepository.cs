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
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            INSERT INTO Credentials (Title, Username, EncryptedPassword, CredentialUuid, SecretFormatVersion, URL, Notes, IconKey, CredentialType)
            VALUES ($title, $username, $password, $credentialUuid, $secretFormatVersion, $url, $notes, $iconKey, $credentialType);";

		cmd.Parameters.AddWithValue("$title", credential.Title);
		cmd.Parameters.AddWithValue("$username", credential.Username ?? "");
		cmd.Parameters.AddWithValue("$password", credential.EncryptedPassword);
		cmd.Parameters.AddWithValue("$credentialUuid", credential.CredentialUuid);
		cmd.Parameters.AddWithValue("$secretFormatVersion", credential.SecretFormatVersion);
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
		cmd.CommandText = "SELECT Id, Title, Username, EncryptedPassword, CredentialUuid, SecretFormatVersion, URL, Notes, CreatedAt, UpdatedAt, IconKey, CredentialType FROM Credentials";

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
		cmd.CommandText = "SELECT Id, Title, Username, EncryptedPassword, CredentialUuid, SecretFormatVersion, URL, Notes, CreatedAt, UpdatedAt, IconKey, CredentialType FROM Credentials WHERE Id = $id LIMIT 1;";
		cmd.Parameters.AddWithValue("$id", id);

		using var reader = cmd.ExecuteReader();
		return reader.Read() ? MapRecord(reader) : null;
	}

	/// <inheritdoc/>
	public void Update(CredentialRecord credential)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            UPDATE Credentials
            SET Title = $title,
                Username = $username,
                EncryptedPassword = $password,
                CredentialUuid = $credentialUuid,
                SecretFormatVersion = $secretFormatVersion,
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
		cmd.Parameters.AddWithValue("$credentialUuid", credential.CredentialUuid);
		cmd.Parameters.AddWithValue("$secretFormatVersion", credential.SecretFormatVersion);
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
		EncryptedPassword = (byte[])reader["EncryptedPassword"],
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
