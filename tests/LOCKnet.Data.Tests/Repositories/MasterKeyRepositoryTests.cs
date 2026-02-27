using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests.Repositories;

/// <summary>
/// Tests for <see cref="MasterKeyRepository"/> using an in-memory SQLite database.
/// </summary>
public class MasterKeyRepositoryTests : IDisposable
{
	private readonly SqliteConnection _keepAlive;
	private readonly MasterKeyRepository _sut;

	public MasterKeyRepositoryTests()
	{
		var dbName = $"mk_{Guid.NewGuid():N}";
		var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

		_keepAlive = new SqliteConnection(connectionString);
		_keepAlive.Open();

		new Database(connectionString, true).Initialize();

		_sut = new MasterKeyRepository(connectionString);
	}

	public void Dispose() => _keepAlive.Dispose();

	// ── Helper ────────────────────────────────────────────────────────────────

	private static MasterKeyRecord MakeKey(byte seed = 0xAB) =>
		new()
		{
			PasswordHash = Enumerable.Repeat(seed, 32).ToArray(),
			Salt = Enumerable.Repeat((byte)(seed + 1), 32).ToArray(),
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		};

	// ── Get on empty DB ───────────────────────────────────────────────────────

	[Fact]
	public void Get_EmptyDatabase_ReturnsNull()
	{
		var result = _sut.Get();

		Assert.Null(result);
	}

	// ── Create / Get ──────────────────────────────────────────────────────────

	[Fact]
	public void Create_ThenGet_RoundTripsHashAndSalt()
	{
		var key = MakeKey();

		_sut.Create(key);

		var retrieved = _sut.Get();
		Assert.NotNull(retrieved);
		Assert.Equal(key.PasswordHash, retrieved.PasswordHash);
		Assert.Equal(key.Salt, retrieved.Salt);
	}

	[Fact]
	public void Create_CalledTwice_ThrowsInvalidOperationException()
	{
		_sut.Create(MakeKey());

		Assert.Throws<InvalidOperationException>(() => _sut.Create(MakeKey(0xCC)));
	}

	[Fact]
	public void Create_AfterDelete_Succeeds()
	{
		_sut.Create(MakeKey());
		_sut.Delete();

		var ex = Record.Exception(() => _sut.Create(MakeKey(0xBB)));

		Assert.Null(ex);
	}

	// ── Update ────────────────────────────────────────────────────────────────

	[Fact]
	public void Update_PersistsNewHashAndSalt()
	{
		_sut.Create(MakeKey(0x01));

		var updatedKey = new MasterKeyRecord
		{
			PasswordHash = Enumerable.Repeat((byte)0xF0, 32).ToArray(),
			Salt = Enumerable.Repeat((byte)0xF1, 32).ToArray(),
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		};
		_sut.Update(updatedKey);

		var retrieved = _sut.Get();
		Assert.NotNull(retrieved);
		Assert.Equal(updatedKey.PasswordHash, retrieved.PasswordHash);
		Assert.Equal(updatedKey.Salt, retrieved.Salt);
	}

	[Fact]
	public void Update_ChangesHashButNotCreatedAt()
	{
		var original = MakeKey();
		_sut.Create(original);
		var originalCreated = _sut.Get()!.CreatedAt;

		_sut.Update(new MasterKeyRecord
		{
			PasswordHash = Enumerable.Repeat((byte)0xFF, 32).ToArray(),
			Salt = Enumerable.Repeat((byte)0xEE, 32).ToArray(),
			CreatedAt = originalCreated,
			UpdatedAt = DateTime.UtcNow,
		});

		var retrieved = _sut.Get();
		Assert.NotNull(retrieved);
		// CreatedAt should be preserved (repository passes it through)
		Assert.Equal(originalCreated, retrieved.CreatedAt);
	}

	// ── Delete ────────────────────────────────────────────────────────────────

	[Fact]
	public void Delete_AfterCreate_GetReturnsNull()
	{
		_sut.Create(MakeKey());

		_sut.Delete();

		Assert.Null(_sut.Get());
	}

	[Fact]
	public void Delete_WhenEmpty_IsNoOp()
	{
		var ex = Record.Exception(() => _sut.Delete());

		Assert.Null(ex);
	}

	// ── Timestamps ────────────────────────────────────────────────────────────

	[Fact]
	public void Create_TimestampsArePopulatedByDatabase()
	{
		_sut.Create(MakeKey());

		var record = _sut.Get();

		Assert.NotNull(record);
		Assert.NotEqual(default, record.CreatedAt);
		Assert.NotEqual(default, record.UpdatedAt);
	}
}
