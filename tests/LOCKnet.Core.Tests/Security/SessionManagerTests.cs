using LOCKnet.Core.Security;

namespace LOCKnet.Core.Tests.Security;

public class SessionManagerTests
{
	private static byte[] GetInternalKey(SessionManager sut)
		=> (byte[]?)typeof(SessionManager)
			.GetField("_sessionKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
			?.GetValue(sut)
		?? [];

	private static byte[] MakeKey() => new byte[32];

	// ── IsUnlocked ────────────────────────────────────────────────────────────

	[Fact]
	public void IsUnlocked_Initially_ReturnsFalse()
	{
		var sut = new SessionManager();
		Assert.False(sut.IsUnlocked);
	}

	[Fact]
	public void IsUnlocked_AfterOpen_ReturnsTrue()
	{
		var sut = new SessionManager();
		sut.Open(MakeKey());
		Assert.True(sut.IsUnlocked);
	}

	[Fact]
	public void IsUnlocked_AfterLock_ReturnsFalse()
	{
		var sut = new SessionManager();
		sut.Open(MakeKey());
		sut.Lock();
		Assert.False(sut.IsUnlocked);
	}

	// ── GetSessionKey ─────────────────────────────────────────────────────────

	[Fact]
	public void GetSessionKey_WhenLocked_ReturnsNull()
	{
		var sut = new SessionManager();
		Assert.Null(sut.GetSessionKey());
	}

	[Fact]
	public void GetSessionKey_AfterOpen_ReturnsKey()
	{
		var sut = new SessionManager();
		var key = MakeKey();
		sut.Open(key);
		Assert.NotNull(sut.GetSessionKey());
	}

	[Fact]
	public void GetSessionKey_ReturnsCopy_NotLiveInternalBuffer()
	{
		var sut = new SessionManager();
		sut.Open(MakeKey());

		var first = sut.GetSessionKey()!;
		first[0] = 0xAA;
		var second = sut.GetSessionKey()!;

		Assert.NotEqual(first[0], second[0]);
	}

	[Fact]
	public void GetSessionKey_AfterLock_ReturnsNull()
	{
		var sut = new SessionManager();
		sut.Open(MakeKey());
		sut.Lock();
		Assert.Null(sut.GetSessionKey());
	}

	// ── Lock event ────────────────────────────────────────────────────────────

	[Fact]
	public void Lock_FiresLockedEvent()
	{
		var sut = new SessionManager();
		sut.Open(MakeKey());

		var fired = false;
		sut.Locked += (_, _) => fired = true;
		sut.Lock();

		Assert.True(fired);
	}

	[Fact]
	public void Lock_WhenAlreadyLocked_DoesNotThrow()
	{
		var sut = new SessionManager();
		var ex = Record.Exception(() => sut.Lock());
		Assert.Null(ex);
	}

	[Fact]
	public void Lock_WhenAlreadyLocked_FiresEvent()
	{
		var sut = new SessionManager();
		var count = 0;
		sut.Locked += (_, _) => count++;
		sut.Lock();
		Assert.Equal(1, count);
	}

	[Fact]
	public void Lock_ZeroesInternalKeyBuffer()
	{
		var sut = new SessionManager();
		var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
		sut.Open(key);

		var internalKey = GetInternalKey(sut);
		Assert.Contains(internalKey, b => b != 0);

		sut.Lock();

		Assert.All(internalKey, b => Assert.Equal(0, b));
	}

	// ── Open validation ───────────────────────────────────────────────────────

	[Fact]
	public void Open_ShortKey_Throws()
	{
		var sut = new SessionManager();
		Assert.Throws<ArgumentException>(() => sut.Open(new byte[16]));
	}

	[Fact]
	public void Open_NullKey_Throws()
	{
		var sut = new SessionManager();
		Assert.Throws<ArgumentNullException>(() => sut.Open(null!));
	}

	// ── Key zeroing on re-open ────────────────────────────────────────────────

	[Fact]
	public void Open_CalledTwice_ReplacesOldKey()
	{
		var sut = new SessionManager();
		var firstKey = new byte[32];
		firstKey[0] = 0xAA;

		sut.Open(firstKey);

		var secondKey = new byte[32];
		secondKey[0] = 0xBB;
		sut.Open(secondKey);

		// First key array should be zeroed
		Assert.Equal(0, firstKey[0]);
	}
}
