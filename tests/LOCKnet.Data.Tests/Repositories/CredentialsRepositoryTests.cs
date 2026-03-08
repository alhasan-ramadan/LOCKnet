using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests.Repositories;

/// <summary>
/// Tests for <see cref="CredentialsRepository"/> using an in-memory SQLite database.
/// Each test instance gets its own isolated named shared-cache database.
/// </summary>
public class CredentialsRepositoryTests : IDisposable
{
	private readonly SqliteConnection _keepAlive;
	private readonly CredentialsRepository _sut;

	public CredentialsRepositoryTests()
	{
		var dbName = $"cred_{Guid.NewGuid():N}";
		var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

		_keepAlive = new SqliteConnection(connectionString);
		_keepAlive.Open();

		new Database(connectionString, true).Initialize();

		_sut = new CredentialsRepository(connectionString);
	}

	public void Dispose() => _keepAlive.Dispose();

	// ── Helper ────────────────────────────────────────────────────────────────

	private static CredentialRecord MakeRecord(string title = "GitHub", string? username = "alice") =>
		new()
		{
			Title = title,
			Username = username,
			EncryptedPassword = [0x01, 0x02, 0x03],
			Url = "https://github.com",
			Notes = "test notes",
		};

	// ── Add / GetAll ───────────────────────────────────────────────────────────

	[Fact]
	public void Add_ThenGetAll_ContainsEntry()
	{
		_sut.Add(MakeRecord());

		var all = _sut.GetAll();

		Assert.Single(all);
		Assert.Equal("GitHub", all[0].Title);
	}

	[Fact]
	public void Add_MultipleEntries_AllReturnedByGetAll()
	{
		_sut.Add(MakeRecord("GitHub"));
		_sut.Add(MakeRecord("GitLab"));
		_sut.Add(MakeRecord("Bitbucket"));

		var all = _sut.GetAll();

		Assert.Equal(3, all.Count);
	}

	[Fact]
	public void Add_AssignsAutoIncrementId()
	{
		_sut.Add(MakeRecord("A"));
		_sut.Add(MakeRecord("B"));

		var all = _sut.GetAll();

		Assert.All(all, r => Assert.True(r.Id > 0));
		Assert.NotEqual(all[0].Id, all[1].Id);
	}

	[Fact]
	public void Add_EncryptedPasswordIsPersistedCorrectly()
	{
		var expectedBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
		var record = MakeRecord();
		record.EncryptedPassword = expectedBytes;

		_sut.Add(record);

		var stored = _sut.GetAll()[0];
		Assert.Equal(expectedBytes, stored.EncryptedPassword);
	}

	[Fact]
	public void Add_NullUsername_StoredAsNullOrEmpty()
	{
		_sut.Add(MakeRecord(username: null));

		var stored = _sut.GetAll()[0];
		Assert.True(stored.Username is null || stored.Username == string.Empty);
	}

	// ── GetById ────────────────────────────────────────────────────────────────

	[Fact]
	public void GetById_ExistingId_ReturnsRecord()
	{
		_sut.Add(MakeRecord("MyService"));
		var id = _sut.GetAll()[0].Id;

		var result = _sut.GetById(id);

		Assert.NotNull(result);
		Assert.Equal("MyService", result.Title);
	}

	[Fact]
	public void GetById_NonExistentId_ReturnsNull()
	{
		var result = _sut.GetById(9999);

		Assert.Null(result);
	}

	// ── Update ────────────────────────────────────────────────────────────────

	[Fact]
	public void Update_PersistsChangedTitle()
	{
		_sut.Add(MakeRecord("OldTitle"));
		var id = _sut.GetAll()[0].Id;

		_sut.Update(new CredentialRecord
		{
			Id = id,
			Title = "NewTitle",
			Username = "bob",
			EncryptedPassword = [0xFF],
			Url = null,
			Notes = null,
		});

		var updated = _sut.GetById(id);
		Assert.NotNull(updated);
		Assert.Equal("NewTitle", updated.Title);
	}

	[Fact]
	public void Update_PersistsNewEncryptedPassword()
	{
		_sut.Add(MakeRecord());
		var id = _sut.GetAll()[0].Id;
		var newPassword = new byte[] { 0xAB, 0xCD, 0xEF };

		_sut.Update(new CredentialRecord
		{
			Id = id,
			Title = "Title",
			Username = null,
			EncryptedPassword = newPassword,
		});

		var updated = _sut.GetById(id);
		Assert.Equal(newPassword, updated!.EncryptedPassword);
	}

	[Fact]
	public void Update_NonExistentId_IsNoOp()
	{
		var ex = Record.Exception(() => _sut.Update(new CredentialRecord
		{
			Id = 9999,
			Title = "Ghost",
			EncryptedPassword = [0x00],
		}));

		Assert.Null(ex);
	}

	// ── Remove ────────────────────────────────────────────────────────────────

	[Fact]
	public void Remove_ExistingEntry_EntryIsGone()
	{
		_sut.Add(MakeRecord());
		var id = _sut.GetAll()[0].Id;

		_sut.Remove(id);

		Assert.Empty(_sut.GetAll());
	}

	[Fact]
	public void Remove_NonExistentId_IsNoOp()
	{
		var ex = Record.Exception(() => _sut.Remove(9999));

		Assert.Null(ex);
	}

	[Fact]
	public void Remove_OnlyRemovesTargetEntry()
	{
		_sut.Add(MakeRecord("Keep"));
		_sut.Add(MakeRecord("Delete"));

		var all = _sut.GetAll();
		var deleteId = all.First(r => r.Title == "Delete").Id;

		_sut.Remove(deleteId);

		var remaining = _sut.GetAll();
		Assert.Single(remaining);
		Assert.Equal("Keep", remaining[0].Title);
	}

	// ── Timestamps ────────────────────────────────────────────────────────────

	[Fact]
	public void Add_CreatedAtAndUpdatedAt_ArePopulated()
	{
		_sut.Add(MakeRecord());

		var record = _sut.GetAll()[0];

		Assert.NotEqual(default, record.CreatedAt);
		Assert.NotEqual(default, record.UpdatedAt);
	}
	// ── CredentialType ───────────────────────────────────────────────────────

	[Fact]
	public void Add_WithApiKeyType_CredentialTypeIsPersisted()
	{
		var record = MakeRecord();
		record.CredentialType = CredentialType.ApiKey;

		_sut.Add(record);

		var stored = _sut.GetAll()[0];
		Assert.Equal(CredentialType.ApiKey, stored.CredentialType);
	}

	[Fact]
	public void Add_NoExplicitType_DefaultsToPassword()
	{
		_sut.Add(MakeRecord());

		var stored = _sut.GetAll()[0];
		Assert.Equal(CredentialType.Password, stored.CredentialType);
	}

	[Fact]
	public void Update_CredentialType_IsPersisted()
	{
		_sut.Add(MakeRecord());
		var id = _sut.GetAll()[0].Id;

		_sut.Update(new CredentialRecord
		{
			Id = id,
			Title = "GitHub",
			EncryptedPassword = [0x01, 0x02, 0x03],
			CredentialType = CredentialType.ApiKey,
		});

		var updated = _sut.GetById(id)!;
		Assert.Equal(CredentialType.ApiKey, updated.CredentialType);
	}

}
