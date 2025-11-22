using LOCKnet.Data.Models;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

public class MasterKeyRepository : RepositoryBase
{
	public MasterKeyRepository(string connectionString) : base(connectionString) { }

	public void Set(MasterKey key)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            INSERT INTO MasterKey (PasswordHash, Salt)
            VALUES ($hash, $salt);";

		cmd.Parameters.AddWithValue("$hash", key.PasswordHash);
		cmd.Parameters.AddWithValue("$salt", key.Salt);
		cmd.ExecuteNonQuery();
	}

	public MasterKey? Get()
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT * FROM MasterKey LIMIT 1";

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
}
