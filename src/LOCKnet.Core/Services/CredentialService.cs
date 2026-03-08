using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using System.Security;
using System.Text;

namespace LOCKnet.Core.Services;

/// <summary>
/// Implementierung von <see cref="ICredentialService"/>.
/// Kombiniert <see cref="ISessionManager"/> (Session-Key), <see cref="IEncryptionService"/> (AES-GCM)
/// und <see cref="ICredentialRepository"/> (Persistenz).
/// </summary>
public sealed class CredentialService : ICredentialService
{
	private readonly ICredentialRepository _repo;
	private readonly IEncryptionService _encryption;
	private readonly ISessionManager _session;
	private readonly ISecureStringService _secureStr;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="CredentialService"/>.
	/// </summary>
	public CredentialService(
		ICredentialRepository repo,
		IEncryptionService encryption,
		ISessionManager session,
		ISecureStringService secureStr)
	{
		ArgumentNullException.ThrowIfNull(repo);
		ArgumentNullException.ThrowIfNull(encryption);
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(secureStr);
		_repo = repo;
		_encryption = encryption;
		_session = session;
		_secureStr = secureStr;
	}

	/// <inheritdoc/>
	public void Add(string title, string? username, SecureString password, string? url = null, string? notes = null, string? iconKey = null, CredentialType credentialType = CredentialType.Password)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		ArgumentNullException.ThrowIfNull(password);

		var key = RequireSessionKey();
		var passwordBytes = _secureStr.ToByteArray(password);
		try
		{
			var encrypted = _encryption.Encrypt(passwordBytes, key);
			_repo.Add(new CredentialRecord
			{
				Title = title,
				Username = username,
				EncryptedPassword = encrypted,
				Url = url,
				Notes = notes,
				IconKey = iconKey,
				CredentialType = credentialType,
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			});
		}
		finally
		{
			_secureStr.ZeroMemory(key);
			_secureStr.ZeroMemory(passwordBytes);
		}
	}

	/// <inheritdoc/>
	public IReadOnlyList<CredentialRecord> GetAll()
	{
		RequireUnlocked();
		return _repo.GetAll();
	}

	/// <inheritdoc/>
	public SecureString? GetPassword(int id)
	{
		var key = RequireSessionKey();
		try
		{
			var record = _repo.GetById(id);
			if (record is null) return null;

			var decrypted = _encryption.Decrypt(record.EncryptedPassword, key);
			try
			{
				return _secureStr.FromByteArray(decrypted);
			}
			finally
			{
				_secureStr.ZeroMemory(decrypted);
			}
		}
		finally
		{
			_secureStr.ZeroMemory(key);
		}
	}

	/// <inheritdoc/>
	public void Update(int id, string title, string? username, SecureString? newPassword, string? url = null, string? notes = null, string? iconKey = null, CredentialType credentialType = CredentialType.Password)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);

		var key = RequireSessionKey();
		try
		{
			var existing = _repo.GetById(id)
				?? throw new InvalidOperationException($"Credential mit ID {id} nicht gefunden.");

			byte[] encryptedPassword;
			if (newPassword is not null)
			{
				var passwordBytes = _secureStr.ToByteArray(newPassword);
				try
				{
					encryptedPassword = _encryption.Encrypt(passwordBytes, key);
				}
				finally
				{
					_secureStr.ZeroMemory(passwordBytes);
				}
			}
			else
			{
				encryptedPassword = existing.EncryptedPassword;
			}

			_repo.Update(new CredentialRecord
			{
				Id = id,
				Title = title,
				Username = username,
				EncryptedPassword = encryptedPassword,
				Url = url,
				Notes = notes,
				IconKey = iconKey,
				CredentialType = credentialType,
				CreatedAt = existing.CreatedAt,
				UpdatedAt = DateTime.UtcNow
			});
		}
		finally
		{
			_secureStr.ZeroMemory(key);
		}
	}

	/// <inheritdoc/>
	public void Remove(int id)
	{
		RequireUnlocked();
		_repo.Remove(id);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	private byte[] RequireSessionKey()
	{
		var key = _session.GetSessionKey();
		if (key is null)
			throw new InvalidOperationException("Sitzung ist gesperrt. Bitte zuerst entsperren.");
		return key;
	}

	private void RequireUnlocked()
	{
		if (!_session.IsUnlocked)
			throw new InvalidOperationException("Sitzung ist gesperrt. Bitte zuerst entsperren.");
	}
}
