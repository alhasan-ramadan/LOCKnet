namespace LOCKnet.Core.Security;

/// <summary>
/// Implementierung von <see cref="IActivityMonitor"/>.
/// Verwendet einen <see cref="System.Threading.Timer"/> — kein UI-Thread nötig.
/// Bei Timeout wird <see cref="ISessionManager.Lock"/> aufgerufen.
/// </summary>
public sealed class ActivityMonitor : IActivityMonitor
{
    private readonly ISessionManager _session;
    private System.Threading.Timer? _timer;
    private TimeSpan _timeout = TimeSpan.FromSeconds(60);
    private volatile bool _running;
    private bool _disposed;

    /// <summary>
    /// Initialisiert eine neue Instanz von <see cref="ActivityMonitor"/>.
    /// </summary>
    /// <param name="session">Die Sitzung, die bei Timeout gesperrt wird.</param>
    public ActivityMonitor(ISessionManager session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <inheritdoc/>
    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "Timeout muss positiv sein.");
            _timeout = value;
            if (_running)
                ResetTimer();
        }
    }

    /// <inheritdoc/>
    public bool IsRunning => _running;

    /// <inheritdoc/>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running) return;

        _running = true;
        _timer = new System.Threading.Timer(OnTimeout, null, _timeout, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc/>
    public void Stop()
    {
        _running = false;
        _timer?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc/>
    public void RecordActivity()
    {
        if (_running)
            ResetTimer();
    }

    private void ResetTimer()
    {
        _timer?.Change(_timeout, System.Threading.Timeout.InfiniteTimeSpan);
    }

    private void OnTimeout(object? state)
    {
        if (!_running) return;
        _session.Lock();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer?.Dispose();
        _timer = null;
    }
}
