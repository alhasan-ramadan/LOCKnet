using System.Security;
using System.Security.Cryptography;
using System.Text;
using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.Core.Security;

/// <summary>
/// Implementierung von <see cref="IMasterKeyManager"/>.
/// Delegiert Schlüsselableitung an <see cref="IKeyDerivationService"/> und
/// Persistenz an <see cref="IMasterKeyRepository"/>.
/// </summary>
public sealed class MasterKeyManager : IMasterKeyManager
{
    private readonly IKeyDerivationService _kdf;
    private readonly IMasterKeyRepository _repo;
    private readonly ISecureStringService _secureStr;

    /// <summary>
    /// Initialisiert eine neue Instanz von <see cref="MasterKeyManager"/>.
    /// </summary>
    public MasterKeyManager(
        IKeyDerivationService kdf,
        IMasterKeyRepository repo,
        ISecureStringService secureStr)
    {
        ArgumentNullException.ThrowIfNull(kdf);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(secureStr);
        _kdf = kdf;
        _repo = repo;
        _secureStr = secureStr;
    }

    /// <inheritdoc/>
    public bool IsInitialized => _repo.Get() is not null;

    /// <inheritdoc/>
    public void Initialize(SecureString password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (IsInitialized)
            throw new InvalidOperationException("Master-Key ist bereits initialisiert.");

        var passwordBytes = _secureStr.ToByteArray(password);
        try
        {
            var salt = _kdf.GenerateSalt();
            var hash = _kdf.ComputePasswordHash(passwordBytes, salt);

            _repo.Create(new MasterKeyRecord
            {
                Salt = salt,
                PasswordHash = hash,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        finally
        {
            _secureStr.ZeroMemory(passwordBytes);
        }
    }

    /// <inheritdoc/>
    public byte[]? Unlock(SecureString password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var record = _repo.Get();
        if (record is null)
            throw new InvalidOperationException("Kein Master-Key vorhanden. Bitte zuerst Initialize() aufrufen.");

        var passwordBytes = _secureStr.ToByteArray(password);
        try
        {
            if (!_kdf.VerifyPassword(passwordBytes, record.Salt, record.PasswordHash))
                return null;

            return _kdf.DeriveKey(passwordBytes, record.Salt);
        }
        finally
        {
            _secureStr.ZeroMemory(passwordBytes);
        }
    }

    /// <inheritdoc/>
    public void ChangePassword(SecureString currentPassword, SecureString newPassword)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);

        var record = _repo.Get();
        if (record is null)
            throw new InvalidOperationException("Kein Master-Key vorhanden.");

        var currentBytes = _secureStr.ToByteArray(currentPassword);
        try
        {
            if (!_kdf.VerifyPassword(currentBytes, record.Salt, record.PasswordHash))
                throw new UnauthorizedAccessException("Das aktuelle Passwort ist falsch.");
        }
        finally
        {
            _secureStr.ZeroMemory(currentBytes);
        }

        var newBytes = _secureStr.ToByteArray(newPassword);
        try
        {
            var newSalt = _kdf.GenerateSalt();
            var newHash = _kdf.ComputePasswordHash(newBytes, newSalt);

            _repo.Update(new MasterKeyRecord
            {
                Salt = newSalt,
                PasswordHash = newHash,
                CreatedAt = record.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            });
        }
        finally
        {
            _secureStr.ZeroMemory(newBytes);
        }
    }
}
