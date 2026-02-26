using System.Security.Cryptography;

namespace LOCKnet.Core.Security;

/// <summary>
/// Implementierung von <see cref="ISessionManager"/>.
/// Thread-safe: alle Zugriffe auf den Session-Key sind durch ein Lock geschützt.
/// </summary>
public sealed class SessionManager : ISessionManager
{
    private readonly object _lock = new();
    private byte[]? _sessionKey;

    /// <inheritdoc/>
    public bool IsUnlocked
    {
        get
        {
            lock (_lock)
                return _sessionKey is not null;
        }
    }

    /// <inheritdoc/>
    public event EventHandler? Locked;

    /// <inheritdoc/>
    public byte[]? GetSessionKey()
    {
        lock (_lock)
            return _sessionKey;
    }

    /// <inheritdoc/>
    public void Open(byte[] sessionKey)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        if (sessionKey.Length != 32)
            throw new ArgumentException("Session-Key muss genau 32 Bytes lang sein.", nameof(sessionKey));

        lock (_lock)
        {
            // Alten Key sicher überschreiben falls vorhanden
            if (_sessionKey is not null)
                CryptographicOperations.ZeroMemory(_sessionKey);

            _sessionKey = sessionKey;
        }
    }

    /// <inheritdoc/>
    public void Lock()
    {
        lock (_lock)
        {
            if (_sessionKey is not null)
            {
                CryptographicOperations.ZeroMemory(_sessionKey);
                _sessionKey = null;
            }
        }

        Locked?.Invoke(this, EventArgs.Empty);
    }
}
