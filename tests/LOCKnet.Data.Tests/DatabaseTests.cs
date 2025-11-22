using LOCKnet.Data;
using Xunit;

namespace LOCKnet.Data.Tests;

public class DatabaseTests
{
	[Fact]
	public void TestDatabaseIsNotNullAfterInitialization()
	{
		var db = new Database("credentials.db");
		db.Initialize();
		Assert.NotNull(db);
	}
}
