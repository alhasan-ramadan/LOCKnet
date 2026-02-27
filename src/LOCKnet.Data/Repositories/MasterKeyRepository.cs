using LOCKnet.Core.DataAbstractions;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

/// <summary>
/// SQLite-Implementierung von <see cref="IMasterKeyRepository"/>.
/// Erzwingt, dass genau ein Master-Key in der Datenbank existiert (Id = 1).
/// </summary>
public class MasterKeyRepository : RepositoryBase, IMasterKeyRepository
{
	public MasterKeyRepository(string connectionString) : base(connectionString) { }

	#region IMasterKeyRepository

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">Master-Key existiert bereits.</exception>
	public void Create(MasterKeyRecord key)
	{
		if (Get() != null)
			throw new InvalidOperationException("MasterKey already exists.");

		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            INSERT INTO MasterKey (Id, PasswordHash, Salt)
            VALUES (1, $hash, $salt);";
		cmd.Parameters.AddWithValue("$hash", key.PasswordHash);
		cmd.Parameters.AddWithValue("$salt", key.Salt);
		cmd.ExecuteNonQuery();
	}

	/// <inheritdoc/>
	public MasterKeyRecord? Get()
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT PasswordHash, Salt, CreatedAt, UpdatedAt FROM MasterKey WHERE Id = 1 LIMIT 1;";

		using var reader = cmd.ExecuteReader();
		if (!reader.Read())
			return null;

		return new MasterKeyRecord
		{
			PasswordHash = (byte[])reader["PasswordHash"],
			Salt = (byte[])reader["Salt"],
			CreatedAt = reader.GetDateTime(2),
			UpdatedAt = reader.GetDateTime(3),
		};
	}

	/// <inheritdoc/>
	public void Update(MasterKeyRecord key)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            UPDATE MasterKey
            SET PasswordHash = $hash,
                Salt = $salt,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = 1;";
		cmd.Parameters.AddWithValue("$hash", key.PasswordHash);
		cmd.Parameters.AddWithValue("$salt", key.Salt);
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
