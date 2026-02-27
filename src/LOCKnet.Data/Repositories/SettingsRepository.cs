using LOCKnet.Core.DataAbstractions;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

/// <summary>
/// SQLite-Implementierung von <see cref="ISettingsRepository"/>.
/// </summary>
public class SettingsRepository : RepositoryBase, ISettingsRepository
{
	public SettingsRepository(string connectionString) : base(connectionString) { }

	#region ISettingsRepository

	/// <inheritdoc/>
	public void Set(string key, string value)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            INSERT INTO Settings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = $value, UpdatedAt = CURRENT_TIMESTAMP;";
		cmd.Parameters.AddWithValue("$key", key);
		cmd.Parameters.AddWithValue("$value", value);
		cmd.ExecuteNonQuery();
	}

	/// <inheritdoc/>
	public string? Get(string key)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
		cmd.Parameters.AddWithValue("$key", key);
		return cmd.ExecuteScalar() as string;
	}

	/// <inheritdoc/>
	public IReadOnlyDictionary<string, string> GetAll()
	{
		var dict = new Dictionary<string, string>();
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT Key, Value FROM Settings";

		using var reader = cmd.ExecuteReader();
		while (reader.Read())
			dict[reader.GetString(0)] = reader.GetString(1);

		return dict;
	}

	/// <inheritdoc/>
	public void Remove(string key)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "DELETE FROM Settings WHERE Key = $key;";
		cmd.Parameters.AddWithValue("$key", key);
		cmd.ExecuteNonQuery();
	}

	#endregion
}
