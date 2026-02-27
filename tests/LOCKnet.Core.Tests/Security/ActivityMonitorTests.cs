using LOCKnet.Core.Security;

namespace LOCKnet.Core.Tests.Security;

public class ActivityMonitorTests
{
	private static SessionManager MakeSession() => new();

	// ── Start / Stop / IsRunning ──────────────────────────────────────────────

	[Fact]
	public void IsRunning_BeforeStart_ReturnsFalse()
	{
		using var sut = new ActivityMonitor(MakeSession());
		Assert.False(sut.IsRunning);
	}

	[Fact]
	public void IsRunning_AfterStart_ReturnsTrue()
	{
		using var sut = new ActivityMonitor(MakeSession());
		sut.Timeout = TimeSpan.FromMinutes(10);
		sut.Start();
		Assert.True(sut.IsRunning);
	}

	[Fact]
	public void IsRunning_AfterStop_ReturnsFalse()
	{
		using var sut = new ActivityMonitor(MakeSession());
		sut.Timeout = TimeSpan.FromMinutes(10);
		sut.Start();
		sut.Stop();
		Assert.False(sut.IsRunning);
	}

	[Fact]
	public void Start_CalledTwice_DoesNotThrow()
	{
		using var sut = new ActivityMonitor(MakeSession());
		sut.Timeout = TimeSpan.FromMinutes(10);
		sut.Start();
		var ex = Record.Exception(() => sut.Start());
		Assert.Null(ex);
	}

	// ── Auto-Lock ─────────────────────────────────────────────────────────────

	[Fact]
	public async Task AutoLock_AfterTimeout_LocksSession()
	{
		var session = MakeSession();
		session.Open(new byte[32]);
		using var sut = new ActivityMonitor(session) { Timeout = TimeSpan.FromMilliseconds(100) };

		sut.Start();

		// Warte auf Timeout + Puffer
		await Task.Delay(300);

		Assert.False(session.IsUnlocked);
	}

	[Fact]
	public async Task RecordActivity_ResetsTimer_PreventsLock()
	{
		var session = MakeSession();
		session.Open(new byte[32]);
		using var sut = new ActivityMonitor(session) { Timeout = TimeSpan.FromMilliseconds(200) };

		sut.Start();

		// Aktivität kurz vor Ablauf aufzeichnen
		await Task.Delay(100);
		sut.RecordActivity();
		await Task.Delay(100);
		sut.RecordActivity();
		await Task.Delay(100);

		// Nach 3×100ms und 2 Reset-Calls sollte die Sitzung noch offen sein
		Assert.True(session.IsUnlocked);
	}

	[Fact]
	public async Task Stop_PreventsAutoLock()
	{
		var session = MakeSession();
		session.Open(new byte[32]);
		using var sut = new ActivityMonitor(session) { Timeout = TimeSpan.FromMilliseconds(100) };

		sut.Start();
		sut.Stop(); // sofort stoppen

		await Task.Delay(250); // Timeout würde eigentlich feuern

		Assert.True(session.IsUnlocked, "Stopped monitor must not lock the session.");
	}

	// ── Timeout property ──────────────────────────────────────────────────────

	[Fact]
	public void Timeout_SetToZeroOrNegative_Throws()
	{
		using var sut = new ActivityMonitor(MakeSession());
		Assert.Throws<ArgumentOutOfRangeException>(() => sut.Timeout = TimeSpan.Zero);
		Assert.Throws<ArgumentOutOfRangeException>(() => sut.Timeout = TimeSpan.FromSeconds(-1));
	}

	[Fact]
	public void Timeout_DefaultValue_Is60Seconds()
	{
		using var sut = new ActivityMonitor(MakeSession());
		Assert.Equal(TimeSpan.FromSeconds(60), sut.Timeout);
	}

	// ── Dispose ───────────────────────────────────────────────────────────────

	[Fact]
	public void Dispose_CanBeCalledTwice()
	{
		var sut = new ActivityMonitor(MakeSession());
		sut.Dispose();
		var ex = Record.Exception(() => sut.Dispose());
		Assert.Null(ex);
	}

	[Fact]
	public void Start_AfterDispose_Throws()
	{
		var sut = new ActivityMonitor(MakeSession());
		sut.Dispose();
		Assert.Throws<ObjectDisposedException>(() => sut.Start());
	}
}
