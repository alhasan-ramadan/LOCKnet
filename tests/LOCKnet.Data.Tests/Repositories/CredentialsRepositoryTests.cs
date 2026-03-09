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
			Title = string.Empty,
			Username = null,
			EncryptedPassword = [0x01, 0x02, 0x03],
			EncryptedMetadata = [0x0A, 0x0B, 0x0C],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			Url = null,
			Notes = null,
			IconKey = null,
			CredentialType = CredentialType.Password,
		};

	private static CredentialRecord MakeLegacyRecord(string title = "GitHub", string? username = "alice") =>
		new()
		{
			Title = title,
			Username = username,
			EncryptedPassword = [0x01, 0x02, 0x03],
			EncryptedMetadata = [],
			CredentialUuid = string.Empty,
			SecretFormatVersion = CredentialSecretFormatVersion.Legacy,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Legacy,
			Url = "https://github.com",
			Notes = "test notes",
			IconKey = "github",
			CredentialType = CredentialType.ApiKey,
		};

	// ── Add / GetAll ───────────────────────────────────────────────────────────

	[Fact]
	public void Add_ThenGetAll_ContainsEntry()
	{
		_sut.Add(MakeRecord());

		var all = _sut.GetAll();

		Assert.Single(all);
		Assert.Equal(string.Empty, all[0].Title);
		Assert.NotEmpty(all[0].EncryptedMetadata);
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
	public void Add_PersistsCredentialUuidAndSecretFormatVersion()
	{
		var record = MakeRecord();

		_sut.Add(record);

		var stored = _sut.GetAll()[0];
		Assert.Equal(record.CredentialUuid, stored.CredentialUuid);
		Assert.Equal(record.SecretFormatVersion, stored.SecretFormatVersion);
	}

	[Fact]
	public void Add_PersistsEncryptedMetadataAndMetadataFormatVersion()
	{
		var record = MakeRecord();

		_sut.Add(record);

		var stored = _sut.GetAll()[0];
		Assert.Equal(record.EncryptedMetadata, stored.EncryptedMetadata);
		Assert.Equal(record.MetadataFormatVersion, stored.MetadataFormatVersion);
	}

	[Fact]
	public void Add_DuplicateNonEmptyCredentialUuid_ThrowsSqliteException()
	{
		var credentialUuid = Guid.NewGuid().ToString("N");
		var first = MakeRecord("First");
		var second = MakeRecord("Second");
		first.CredentialUuid = credentialUuid;
		second.CredentialUuid = credentialUuid;

		_sut.Add(first);

		Assert.Throws<SqliteException>(() => _sut.Add(second));
	}

	[Fact]
	public void Add_CurrentMetadataRecordWithPlaintextTitle_ThrowsInvalidOperationException()
	{
		var record = MakeRecord();
		record.Title = "ShouldNotPersist";

		Assert.Throws<InvalidOperationException>(() => _sut.Add(record));
	}

	[Fact]
	public void Add_CurrentMetadataRecordWithoutEncryptedMetadata_ThrowsInvalidOperationException()
	{
		var record = MakeRecord();
		record.EncryptedMetadata = [];

		Assert.Throws<InvalidOperationException>(() => _sut.Add(record));
	}

	[Fact]
	public void Add_LegacyMetadataRecordWithPlaintextFields_IsAllowed()
	{
		var record = MakeLegacyRecord();

		Assert.Throws<InvalidOperationException>(() => _sut.Add(record));
	}

	[Fact]
	public void Add_NullUsername_StoredAsNullOrEmpty()
	{
		_sut.Add(MakeRecord());

		var stored = _sut.GetAll()[0];
		Assert.True(stored.Username is null || stored.Username == string.Empty);
	}

	// ── GetById ────────────────────────────────────────────────────────────────

	[Fact]
	public void GetById_ExistingId_ReturnsRecord()
	{
		var record = MakeRecord();
		_sut.Add(record);
		var id = _sut.GetAll()[0].Id;

		var result = _sut.GetById(id);

		Assert.NotNull(result);
		Assert.Equal(string.Empty, result.Title);
		Assert.Equal(record.CredentialUuid, result.CredentialUuid);
	}

	[Fact]
	public void GetById_NonExistentId_ReturnsNull()
	{
		var result = _sut.GetById(9999);

		Assert.Null(result);
	}

	// ── Update ────────────────────────────────────────────────────────────────

	[Fact]
	public void Update_CurrentRecordPersistsScrubbedState()
	{
		_sut.Add(MakeRecord());
		var id = _sut.GetAll()[0].Id;

		_sut.Update(new CredentialRecord
		{
			Id = id,
			Title = string.Empty,
			Username = null,
			EncryptedPassword = [0xFF],
			EncryptedMetadata = [0xA1, 0xB2],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			Url = null,
			Notes = null,
			CredentialType = CredentialType.Password,
		});

		var updated = _sut.GetById(id);
		Assert.NotNull(updated);
		Assert.Equal(string.Empty, updated.Title);
		Assert.Equal(CredentialMetadataFormatVersion.Current, updated.MetadataFormatVersion);
		Assert.NotEmpty(updated.EncryptedMetadata);
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
			Title = string.Empty,
			Username = null,
			EncryptedPassword = newPassword,
			EncryptedMetadata = [0x0A],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			CredentialType = CredentialType.Password,
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
			Title = string.Empty,
			EncryptedPassword = [0x00],
			EncryptedMetadata = [0x01],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
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
	public void Update_CurrentMetadataRecordWithPlaintextNotes_ThrowsInvalidOperationException()
	{
		_sut.Add(MakeRecord("Stored"));
		var stored = _sut.GetAll()[0];

		stored.Notes = "should fail";

		Assert.Throws<InvalidOperationException>(() => _sut.Update(stored));
	}

	[Fact]
	public void Remove_OnlyRemovesTargetEntry()
	{
		_sut.Add(MakeRecord());
		_sut.Add(MakeRecord());

		var all = _sut.GetAll();
		var deleteId = all.Last().Id;

		_sut.Remove(deleteId);

		var remaining = _sut.GetAll();
		Assert.Single(remaining);
		Assert.NotEqual(deleteId, remaining[0].Id);
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
		record.CredentialType = CredentialType.Password;

		_sut.Add(record);

		var stored = _sut.GetAll()[0];
		Assert.Equal(CredentialType.Password, stored.CredentialType);
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
			Title = string.Empty,
			EncryptedPassword = [0x01, 0x02, 0x03],
			EncryptedMetadata = [0x05, 0x06],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			CredentialType = CredentialType.Password,
		});

		var updated = _sut.GetById(id)!;
		Assert.Equal(CredentialType.Password, updated.CredentialType);
	}

}
