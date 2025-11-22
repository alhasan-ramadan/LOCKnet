using LOCKnet.Data.Models;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Repositories;

public class CredentialsRepository : RepositoryBase
{
	public CredentialsRepository(string connectionString) : base(connectionString) { }

	#region CRUD 
	
	/// <summary>
	/// Create
	/// </summary>
	/// <param name="credential"></param>
	public void Add(Credential credential)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            INSERT INTO Credentials (Title, Username, EncryptedPassword, URL, Notes)
            VALUES ($title, $username, $password, $url, $notes);";

		cmd.Parameters.AddWithValue("$title", credential.Title);
		cmd.Parameters.AddWithValue("$username", credential.Username ?? "");
		cmd.Parameters.AddWithValue("$password", credential.EncryptedPassword);
		cmd.Parameters.AddWithValue("$url", credential.URL ?? "");
		cmd.Parameters.AddWithValue("$notes", credential.Notes ?? "");

		cmd.ExecuteNonQuery();
	}

	/// <summary>
	/// Read
	/// </summary>
	/// <returns></returns>
	public List<Credential> GetAll()
	{
		var list = new List<Credential>();
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT * FROM Credentials";

		using var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			list.Add(new Credential
			{
				Id = reader.GetInt32(0),
				Title = reader.GetString(1),
				Username = reader.GetString(2),
				EncryptedPassword = (byte[])reader["EncryptedPassword"],
				URL = reader.GetString(4),
				Notes = reader.GetString(5),
				CreatedAt = reader.GetDateTime(6),
				UpdatedAt = reader.GetDateTime(7)
			});
		}

		return list;
	}
	
	/// <summary>
	/// Update
	/// </summary>
	/// <param name="credential"></param>
	public void Update(Credential credential)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
        UPDATE Credentials
        SET Title = $title,
            Username = $username,
            EncryptedPassword = $password,
            URL = $url,
            Notes = $notes,
            UpdatedAt = CURRENT_TIMESTAMP
        WHERE Id = $id;";

		cmd.Parameters.AddWithValue("$id", credential.Id);
		cmd.Parameters.AddWithValue("$title", credential.Title);
		cmd.Parameters.AddWithValue("$username", credential.Username ?? "");
		cmd.Parameters.AddWithValue("$password", credential.EncryptedPassword);
		cmd.Parameters.AddWithValue("$url", credential.URL ?? "");
		cmd.Parameters.AddWithValue("$notes", credential.Notes ?? "");

		cmd.ExecuteNonQuery();
	}

	
	/// <summary>
	/// Delete
	/// </summary>
	/// <param name="credential"></param>
	public void Remove(int id)
	{
		using var conn = GetConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "DELETE FROM Credentials WHERE Id = $id;";
		cmd.Parameters.AddWithValue("$id", id);
		cmd.ExecuteNonQuery();
	}

	#endregion
}
