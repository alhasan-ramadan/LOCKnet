using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using System.Security;

namespace LOCKnet.Core.Services;

/// <summary>
/// Implementierung von <see cref="ICredentialService"/>.
/// Kombiniert <see cref="ISessionManager"/> (Session-Key), <see cref="IEncryptionService"/> (AES-GCM)
/// und <see cref="ICredentialRepository"/> (Persistenz).
/// </summary>
public sealed class CredentialService : ICredentialService
{
	private readonly ICredentialRepository _repo;
	private readonly IMasterKeyRepository _masterKeyRepo;
	private readonly ICredentialEnvelopeService _credentialEnvelope;
	private readonly ISessionManager _session;
	private readonly ISecureStringService _secureStr;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="CredentialService"/>.
	/// </summary>
	public CredentialService(
		ICredentialRepository repo,
		IMasterKeyRepository masterKeyRepo,
		IEncryptionService encryption,
		ICredentialEnvelopeService credentialEnvelope,
		ISessionManager session,
		ISecureStringService secureStr)
	{
		ArgumentNullException.ThrowIfNull(repo);
		ArgumentNullException.ThrowIfNull(masterKeyRepo);
		ArgumentNullException.ThrowIfNull(credentialEnvelope);
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(secureStr);
		_repo = repo;
		_masterKeyRepo = masterKeyRepo;
		_credentialEnvelope = credentialEnvelope;
		_session = session;
		_secureStr = secureStr;
	}

	/// <inheritdoc/>
	public void Add(string title, string? username, SecureString password, string? url = null, string? notes = null, string? iconKey = null, CredentialType credentialType = CredentialType.Password)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);
		ArgumentNullException.ThrowIfNull(password);

		var key = RequireSessionKey();
		var header = RequireCurrentHeader();
		var passwordBytes = _secureStr.ToByteArray(password);
		try
		{
			var record = new CredentialRecord
			{
				Title = title,
				Username = username,
				CredentialUuid = Guid.NewGuid().ToString("N"),
				SecretFormatVersion = _credentialEnvelope.CurrentVersion,
				MetadataFormatVersion = _credentialEnvelope.CurrentMetadataVersion,
				Url = url,
				Notes = notes,
				IconKey = iconKey,
				CredentialType = credentialType,
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			};
			record.EncryptedPassword = _credentialEnvelope.Encrypt(passwordBytes, key, record, header.FormatVersion);
			record.EncryptedMetadata = _credentialEnvelope.EncryptMetadata(record, key, header.FormatVersion);
			_repo.Add(ToPersistedRecord(record));
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
		var key = RequireSessionKey();
		var header = RequireCurrentHeader();
		try
		{
			return _repo.GetAll()
				.Select(record => MaterializeRecord(record, key, header.FormatVersion))
				.ToList();
		}
		finally
		{
			_secureStr.ZeroMemory(key);
		}
	}

	/// <inheritdoc/>
	public SecureString? GetPassword(int id)
	{
		var key = RequireSessionKey();
		var header = RequireCurrentHeader();
		try
		{
			var record = _repo.GetById(id);
			if (record is null) return null;

			var decrypted = _credentialEnvelope.Decrypt(record, key, header.FormatVersion);
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
		var header = RequireCurrentHeader();
		try
		{
			var existing = _repo.GetById(id)
				?? throw new InvalidOperationException($"Credential mit ID {id} nicht gefunden.");

			byte[] encryptedPassword;
			var credentialUuid = EnsureCredentialUuid(existing.CredentialUuid);
			var secretFormatVersion = existing.SecretFormatVersion;
			if (newPassword is not null)
			{
				var passwordBytes = _secureStr.ToByteArray(newPassword);
				try
				{
					secretFormatVersion = _credentialEnvelope.CurrentVersion;
					var encryptedRecord = new CredentialRecord
					{
						Id = id,
						CredentialUuid = credentialUuid,
						CredentialType = credentialType,
					};
					encryptedPassword = _credentialEnvelope.Encrypt(passwordBytes, key, encryptedRecord, header.FormatVersion);
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

			var materialized = new CredentialRecord
			{
				Id = id,
				Title = title,
				Username = username,
				EncryptedPassword = encryptedPassword,
				EncryptedMetadata = [],
				CredentialUuid = credentialUuid,
				SecretFormatVersion = secretFormatVersion,
				MetadataFormatVersion = _credentialEnvelope.CurrentMetadataVersion,
				Url = url,
				Notes = notes,
				IconKey = iconKey,
				CredentialType = credentialType,
				CreatedAt = existing.CreatedAt,
				UpdatedAt = DateTime.UtcNow
			};
			materialized.EncryptedMetadata = _credentialEnvelope.EncryptMetadata(materialized, key, header.FormatVersion);
			_repo.Update(ToPersistedRecord(materialized));
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

	private VaultHeader RequireCurrentHeader()
	{
		var header = _masterKeyRepo.Get()
			?? throw new InvalidOperationException("VaultHeader konnte nicht geladen werden.");

		if (header.FormatVersion != VaultHeaderFormatVersion.Current)
			throw new InvalidOperationException("Vault ist noch nicht auf das aktuelle Secret-Format migriert.");

		return header;
	}

	private CredentialRecord MaterializeRecord(CredentialRecord persisted, byte[] key, int vaultFormatVersion)
	{
		var materialized = _credentialEnvelope.DecryptMetadata(persisted, key, vaultFormatVersion);
		materialized.EncryptedPassword = persisted.EncryptedPassword.ToArray();
		materialized.EncryptedMetadata = persisted.EncryptedMetadata.ToArray();
		materialized.SecretFormatVersion = persisted.SecretFormatVersion;
		materialized.MetadataFormatVersion = persisted.MetadataFormatVersion;
		materialized.CredentialUuid = persisted.CredentialUuid;
		materialized.CreatedAt = persisted.CreatedAt;
		materialized.UpdatedAt = persisted.UpdatedAt;
		materialized.Id = persisted.Id;
		return materialized;
	}

	private static CredentialRecord ToPersistedRecord(CredentialRecord materialized) => new()
	{
		Id = materialized.Id,
		Title = string.Empty,
		Username = null,
		EncryptedPassword = materialized.EncryptedPassword,
		EncryptedMetadata = materialized.EncryptedMetadata,
		CredentialUuid = materialized.CredentialUuid,
		SecretFormatVersion = materialized.SecretFormatVersion,
		MetadataFormatVersion = materialized.MetadataFormatVersion,
		Url = null,
		Notes = null,
		CreatedAt = materialized.CreatedAt,
		UpdatedAt = materialized.UpdatedAt,
		IconKey = null,
		CredentialType = CredentialType.Password,
	};

	private static string EnsureCredentialUuid(string credentialUuid)
		=> Guid.TryParseExact(credentialUuid, "N", out _) ? credentialUuid : Guid.NewGuid().ToString("N");
}
