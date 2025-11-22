using LOCKnet.Data.Models;
using Microsoft.Data.Sqlite;
using System;

namespace LOCKnet.Data.Repositories;

/// <summary>
/// Repository for managing the single MasterKey in the database.
/// Only one MasterKey is allowed.
/// </summary>
public class MasterKeyRepository : RepositoryBase
{
	/// <summary>
	/// Initializes a new instance of <see cref="MasterKeyRepository"/>.
	/// </summary>
	/// <param name="connectionString">SQLite connection string.</param>
	public MasterKeyRepository(string connectionString) : base(connectionString) { }

	#region CRUD

	/// <summary>
	/// Creates the MasterKey if it does not exist yet.
	/// </summary>
	/// <param name="key">The MasterKey to insert.</param>
	public void Create(MasterKey key)
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

	/// <summary>
	/// Reads the single MasterKey from the database.
	/// </summary>
	/// <returns>The MasterKey if it exists, otherwise null.</returns>
	public MasterKey? Get()
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT * FROM MasterKey WHERE Id = 1 LIMIT 1;";

		using var reader = cmd.ExecuteReader();
		if (reader.Read())
		{
			return new MasterKey
			{
				Id = reader.GetInt32(0),
				PasswordHash = (byte[])reader["PasswordHash"],
				Salt = (byte[])reader["Salt"],
				CreatedAt = reader.GetDateTime(3),
				UpdatedAt = reader.GetDateTime(4)
			};
		}

		return null;
	}

	/// <summary>
	/// Updates the existing MasterKey with new values.
	/// </summary>
	/// <param name="key">The new MasterKey values.</param>
	public void Update(MasterKey key)
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

	/// <summary>
	/// Deletes the single MasterKey, effectively resetting it.
	/// </summary>
	public void Delete()
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "DELETE FROM MasterKey WHERE Id = 1;";
		cmd.ExecuteNonQuery();
	}

	#endregion
}
