using LOCKnet.App;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests;

public sealed class AppServicesStartupTests : IDisposable
{
	private readonly string _databasePath;

	public AppServicesStartupTests()
	{
		_databasePath = Path.Combine(Path.GetTempPath(), $"locknet-appservices-{Guid.NewGuid():N}.db");
	}

	public void Dispose()
	{
		if (File.Exists(_databasePath))
		{
			try
			{
				File.Delete(_databasePath);
			}
			catch (IOException)
			{
			}
		}
	}

	[Fact]
	public void Initialize_UsesPlainStorageBootstrapAndCreatesSchema()
	{
		AppServices.Initialize(_databasePath);

		Assert.Equal(VaultStorageMode.PlainSqlite, AppServices.Current.StorageDescriptor.Mode);
		Assert.False(AppServices.Current.StorageDescriptor.RequiresKeyAtOpen);
		Assert.True(TableExists("Credentials"));
		Assert.True(TableExists("MasterKey"));
	}

	private bool TableExists(string tableName)
	{
		using var connection = new SqliteConnection($"Data Source={_databasePath}");
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
		command.Parameters.AddWithValue("$name", tableName);
		return Convert.ToInt64(command.ExecuteScalar()!) > 0;
	}
}
