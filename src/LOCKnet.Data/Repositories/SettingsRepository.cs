using LOCKnet.Data.Models;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

public class SettingsRepository : RepositoryBase
{
	public SettingsRepository(string connectionString) : base(connectionString) { }

	public void Set(Setting setting)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            INSERT INTO Settings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = $value;";

		cmd.Parameters.AddWithValue("$key", setting.Key);
		cmd.Parameters.AddWithValue("$value", setting.Value);

		cmd.ExecuteNonQuery();
	}

	public string? Get(string key)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
		cmd.Parameters.AddWithValue("$key", key);

		return cmd.ExecuteScalar() as string;
	}
}
