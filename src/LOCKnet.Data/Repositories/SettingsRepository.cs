using LOCKnet.Data.Models;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;

namespace LOCKnet.Data.Repositories;

public class SettingsRepository : RepositoryBase
{
	/// <summary>
	/// Initializes a new instance of <see cref="SettingsRepository"/>.
	/// </summary>
	/// <param name="connectionString">SQLite connection string.</param>
	public SettingsRepository(string connectionString) : base(connectionString) { }

	#region CRUD

	/// <summary>
	/// Create or update a setting.
	/// </summary>
	/// <param name="setting">The setting to insert or update.</param>
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

	/// <summary>
	/// Read a setting by key.
	/// </summary>
	/// <param name="key">The key of the setting.</param>
	/// <returns>The value of the setting, or null if not found.</returns>
	public string? Get(string key)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT Value FROM Settings WHERE Key = $key";
		cmd.Parameters.AddWithValue("$key", key);
		return cmd.ExecuteScalar() as string;
	}

	/// <summary>
	/// Read all settings.
	/// </summary>
	/// <returns>List of all settings in the database.</returns>
	public List<Setting> GetAll()
	{
		var list = new List<Setting>();
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT * FROM Settings";

		using var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			list.Add(new Setting
			{
				Id = reader.GetInt32(0),
				Key = reader.GetString(1),
				Value = reader.GetString(2),
				CreatedAt = reader.GetDateTime(3),
				UpdatedAt = reader.GetDateTime(4)
			});
		}
		return list;
	}

	/// <summary>
	/// Delete a setting by key.
	/// </summary>
	/// <param name="key">The key of the setting to delete.</param>
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
