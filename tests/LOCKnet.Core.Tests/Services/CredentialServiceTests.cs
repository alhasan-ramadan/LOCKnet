using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using LOCKnet.Core.Services;
using System.Security;

namespace LOCKnet.Core.Tests.Services;

// ── Minimal in-memory fake für ICredentialRepository ─────────────────────────

sealed class InMemoryCredentialRepo : ICredentialRepository
{
	private readonly List<CredentialRecord> _store = [];
	private int _nextId = 1;

	public void Add(CredentialRecord credential)
	{
		credential.Id = _nextId++;
		_store.Add(credential);
	}

	public IReadOnlyList<CredentialRecord> GetAll() => _store.AsReadOnly();

	public CredentialRecord? GetById(int id) => _store.FirstOrDefault(c => c.Id == id);

	public void Update(CredentialRecord credential)
	{
		var idx = _store.FindIndex(c => c.Id == credential.Id);
		if (idx >= 0) _store[idx] = credential;
	}

	public void Remove(int id) => _store.RemoveAll(c => c.Id == id);
}

// ─────────────────────────────────────────────────────────────────────────────

public class CredentialServiceTests
{
	private static SecureString MakeSecure(string value)
	{
		var s = new SecureString();
		foreach (var c in value) s.AppendChar(c);
		s.MakeReadOnly();
		return s;
	}

	private static (CredentialService sut, SessionManager session, InMemoryCredentialRepo repo) BuildSut()
	{
		var repo = new InMemoryCredentialRepo();
		var session = new SessionManager();
		var sut = new CredentialService(
			repo,
			new AesGcmEncryptionService(),
			session,
			new SecureStringService());
		return (sut, session, repo);
	}

	private static void OpenSession(SessionManager session)
	{
		// Deterministischer Test-Key (32 Bytes)
		var key = new byte[32];
		for (var i = 0; i < 32; i++) key[i] = (byte)(i + 1);
		session.Open(key);
	}

	// ── Session guard ─────────────────────────────────────────────────────────

	[Fact]
	public void Add_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(
			() => sut.Add("GitHub", "user", MakeSecure("pw")));
	}

	[Fact]
	public void GetAll_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(() => sut.GetAll());
	}

	[Fact]
	public void GetPassword_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(() => sut.GetPassword(1));
	}

	[Fact]
	public void Remove_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(() => sut.Remove(1));
	}

	// ── Add / GetAll ──────────────────────────────────────────────────────────

	[Fact]
	public void Add_ThenGetAll_ContainsEntry()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		sut.Add("GitHub", "alice", MakeSecure("secret"), "https://github.com");

		var all = sut.GetAll();
		Assert.Single(all);
		Assert.Equal("GitHub", all[0].Title);
		Assert.Equal("alice", all[0].Username);
	}

	[Fact]
	public void Add_PasswordIsStoredEncrypted()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("MyService", null, MakeSecure("plaintextpassword"));

		var record = repo.GetAll()[0];
		// Encrypted bytes must not equal plaintext bytes
		var plaintextBytes = System.Text.Encoding.UTF8.GetBytes("plaintextpassword");
		Assert.False(record.EncryptedPassword.SequenceEqual(plaintextBytes));
	}

	// ── GetPassword ───────────────────────────────────────────────────────────

	[Fact]
	public void GetPassword_ReturnsDecryptedPassword()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		const string original = "SuperSecretPassword!";
		sut.Add("TestSite", "bob", MakeSecure(original));
		var all = sut.GetAll();

		using var decrypted = sut.GetPassword(all[0].Id)!;
		var decryptedBytes = new SecureStringService().ToByteArray(decrypted);
		var decryptedText = System.Text.Encoding.UTF8.GetString(decryptedBytes);

		Assert.Equal(original, decryptedText);
	}

	[Fact]
	public void GetPassword_NonExistentId_ReturnsNull()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		Assert.Null(sut.GetPassword(999));
	}

	// ── Update ────────────────────────────────────────────────────────────────

	[Fact]
	public void Update_WithNewPassword_PasswordChanges()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		sut.Add("Site", "user", MakeSecure("old"));
		var id = sut.GetAll()[0].Id;

		sut.Update(id, "Site", "user", MakeSecure("new"));

		using var pw = sut.GetPassword(id)!;
		var bytes = new SecureStringService().ToByteArray(pw);
		Assert.Equal("new", System.Text.Encoding.UTF8.GetString(bytes));
	}

	[Fact]
	public void Update_WithoutNewPassword_PreservesEncryptedPassword()
	{
		var (sut, session, repo) = BuildSut();
		OpenSession(session);

		sut.Add("Site", "user", MakeSecure("original"));
		var id = sut.GetAll()[0].Id;
		var originalEncrypted = repo.GetAll()[0].EncryptedPassword;

		sut.Update(id, "Site Updated", "user", newPassword: null);

		var updated = repo.GetById(id)!;
		Assert.Equal("Site Updated", updated.Title);
		Assert.Equal(originalEncrypted, updated.EncryptedPassword);
	}

	[Fact]
	public void Update_NonExistentId_Throws()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		Assert.Throws<InvalidOperationException>(
			() => sut.Update(999, "X", null, null));
	}

	// ── Remove ────────────────────────────────────────────────────────────────

	[Fact]
	public void Remove_ExistingEntry_IsGone()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);

		sut.Add("DeleteMe", null, MakeSecure("pw"));
		var id = sut.GetAll()[0].Id;

		sut.Remove(id);

		Assert.Empty(sut.GetAll());
	}

	// ── Session lock during operation ─────────────────────────────────────────

	[Fact]
	public void GetPassword_AfterSessionLocked_Throws()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);
		sut.Add("Site", "user", MakeSecure("pw"));
		var id = sut.GetAll()[0].Id;

		session.Lock();

		Assert.Throws<InvalidOperationException>(() => sut.GetPassword(id));
	}


	// ── Add title validation ────────────────────────────────────────────────────

	[Fact]
	public void Add_EmptyTitle_Throws()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);
		Assert.Throws<ArgumentException>(() => sut.Add("", null, MakeSecure("pw")));
	}

	[Fact]
	public void Add_WhitespaceTitleOnly_Throws()
	{
		var (sut, session, _) = BuildSut();
		OpenSession(session);
		Assert.Throws<ArgumentException>(() => sut.Add("   ", null, MakeSecure("pw")));
	}

	// ── Update when locked ──────────────────────────────────────────────────────

	[Fact]
	public void Update_WhenLocked_Throws()
	{
		var (sut, _, _) = BuildSut();
		Assert.Throws<InvalidOperationException>(
			() => sut.Update(1, "Title", null, null));
	}
}
