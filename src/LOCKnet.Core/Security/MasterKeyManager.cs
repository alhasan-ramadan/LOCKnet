using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;

namespace LOCKnet.Core.Security;

/// <summary>
/// Implementierung von <see cref="IMasterKeyManager"/>.
/// Delegiert Schlüsselableitung an <see cref="IKeyDerivationService"/> und
/// Persistenz an <see cref="IMasterKeyRepository"/>.
/// </summary>
public sealed class MasterKeyManager : IMasterKeyManager
{
	private const int WrappedVaultKeyPacketBytes = 60;
	private static readonly TimeSpan AutomaticStorageCompactionRetryDelay = TimeSpan.FromMinutes(10);
	private readonly IKeyDerivationService _kdf;
	private readonly IMasterKeyRepository _repo;
	private readonly IVaultMigrationRepository _vaultMigrationRepo;
	private readonly IEncryptionService _encryption;
	private readonly ICredentialEnvelopeService _credentialEnvelope;
	private readonly ISessionManager _session;
	private readonly ISecureStringService _secureStr;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="MasterKeyManager"/>.
	/// </summary>
	public MasterKeyManager(
		IKeyDerivationService kdf,
		IMasterKeyRepository repo,
		IVaultMigrationRepository vaultMigrationRepo,
		IEncryptionService encryption,
		ICredentialEnvelopeService credentialEnvelope,
		ISessionManager session,
		ISecureStringService secureStr)
	{
		ArgumentNullException.ThrowIfNull(kdf);
		ArgumentNullException.ThrowIfNull(repo);
		ArgumentNullException.ThrowIfNull(vaultMigrationRepo);
		ArgumentNullException.ThrowIfNull(encryption);
		ArgumentNullException.ThrowIfNull(credentialEnvelope);
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(secureStr);
		_kdf = kdf;
		_repo = repo;
		_vaultMigrationRepo = vaultMigrationRepo;
		_encryption = encryption;
		_credentialEnvelope = credentialEnvelope;
		_session = session;
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
		byte[]? kek = null;
		byte[]? vaultKey = null;
		try
		{
			var parameters = _kdf.GetDefaultParameters();
			var salt = _kdf.GenerateSalt(parameters.SaltLengthBytes);
			kek = _kdf.DeriveKey(passwordBytes, salt, parameters);
			vaultKey = RandomNumberGenerator.GetBytes(32);
			var wrappedVaultKey = _encryption.Encrypt(vaultKey, kek);

			_repo.Create(new VaultHeader
			{
				FormatVersion = VaultHeaderFormatVersion.Current,
				KdfIdentifier = _kdf.Identifier,
				KdfParameters = parameters,
				Salt = salt,
				WrappedVaultKey = wrappedVaultKey,
				LegacyPasswordHash = [],
				UsesLegacyKeyMaterial = false,
				RequiresStorageCompaction = false,
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			});
		}
		finally
		{
			if (kek is not null)
				CryptographicOperations.ZeroMemory(kek);
			if (vaultKey is not null)
				CryptographicOperations.ZeroMemory(vaultKey);
			_secureStr.ZeroMemory(passwordBytes);
		}
	}

	/// <inheritdoc/>
	public UnlockResult? Unlock(SecureString password)
	{
		ArgumentNullException.ThrowIfNull(password);

		var record = _repo.Get();
		if (record is null)
			throw new InvalidOperationException("Kein Master-Key vorhanden. Bitte zuerst Initialize() aufrufen.");
		ValidateHeader(record);

		var passwordBytes = _secureStr.ToByteArray(password);
		byte[]? kek = null;
		byte[]? currentVaultKey = null;
		byte[]? targetVaultKey = null;
		byte[]? resultKey = null;
		try
		{
			var parameters = record.KdfParameters;
			kek = _kdf.DeriveKey(passwordBytes, record.Salt, parameters);

			if (record.WrappedVaultKey.Length > 0)
			{
				try
				{
					currentVaultKey = _encryption.Decrypt(record.WrappedVaultKey, kek);
				}
				catch (CryptographicException)
				{
					return null;
				}

				var usesLegacyKey = record.UsesLegacyKeyMaterial ||
					(record.FormatVersion < VaultHeaderFormatVersion.Current &&
					CryptographicOperations.FixedTimeEquals(currentVaultKey, kek));

				var migration = BuildMigrationPlan(record, currentVaultKey, kek, usesLegacyKey);
				if (migration is not null)
				{
					_vaultMigrationRepo.ApplyMigration(migration.Header, migration.Credentials);
					var storageInfo = CompleteStorageCompactionIfRequired(migration.Header, migration.ActiveVaultKey, automaticRetry: true);
					targetVaultKey = migration.ActiveVaultKey;
					resultKey = targetVaultKey;
					targetVaultKey = null;
					return new UnlockResult { VaultKey = resultKey, StorageCompaction = storageInfo };
				}

				var currentStorageInfo = CompleteStorageCompactionIfRequired(record, currentVaultKey, automaticRetry: true);

				resultKey = currentVaultKey;
				currentVaultKey = null;
				return new UnlockResult { VaultKey = resultKey, StorageCompaction = currentStorageInfo };
			}

			if (record.LegacyPasswordHash.Length == 0 ||
				!_kdf.VerifyPassword(passwordBytes, record.Salt, record.LegacyPasswordHash, parameters))
			{
				return null;
			}

			currentVaultKey = _kdf.DeriveKey(passwordBytes, record.Salt, parameters);
			var legacyMigration = BuildMigrationPlan(record, currentVaultKey, kek, true)
				?? throw new InvalidOperationException("Legacy-Vault konnte nicht in das aktuelle Format migriert werden.");

			_vaultMigrationRepo.ApplyMigration(legacyMigration.Header, legacyMigration.Credentials);
			var legacyStorageInfo = CompleteStorageCompactionIfRequired(legacyMigration.Header, legacyMigration.ActiveVaultKey, automaticRetry: true);
			targetVaultKey = legacyMigration.ActiveVaultKey;
			resultKey = targetVaultKey;
			targetVaultKey = null;
			return new UnlockResult { VaultKey = resultKey, StorageCompaction = legacyStorageInfo };
		}
		finally
		{
			if (kek is not null)
				CryptographicOperations.ZeroMemory(kek);
			if (currentVaultKey is not null)
				CryptographicOperations.ZeroMemory(currentVaultKey);
			if (targetVaultKey is not null)
				CryptographicOperations.ZeroMemory(targetVaultKey);
			_secureStr.ZeroMemory(passwordBytes);
		}
	}

	/// <inheritdoc/>
	public void ChangePassword(SecureString currentPassword, SecureString newPassword)
	{
		ArgumentNullException.ThrowIfNull(currentPassword);
		ArgumentNullException.ThrowIfNull(newPassword);

		var unlock = Unlock(currentPassword);
		if (unlock is null)
			throw new UnauthorizedAccessException("Das aktuelle Passwort ist falsch.");
		var vaultKey = unlock.VaultKey;

		var record = _repo.Get();
		if (record is null)
			throw new InvalidOperationException("Kein Master-Key vorhanden.");

		var newBytes = _secureStr.ToByteArray(newPassword);
		byte[]? newKek = null;
		try
		{
			var parameters = _kdf.GetDefaultParameters();
			var newSalt = _kdf.GenerateSalt(parameters.SaltLengthBytes);
			newKek = _kdf.DeriveKey(newBytes, newSalt, parameters);
			var wrappedVaultKey = _encryption.Encrypt(vaultKey, newKek);

			_repo.Update(new VaultHeader
			{
				FormatVersion = VaultHeaderFormatVersion.Current,
				KdfIdentifier = _kdf.Identifier,
				KdfParameters = parameters,
				Salt = newSalt,
				WrappedVaultKey = wrappedVaultKey,
				LegacyPasswordHash = [],
				UsesLegacyKeyMaterial = false,
				RequiresStorageCompaction = record.RequiresStorageCompaction,
				LastStorageCompactionAttemptUtc = record.LastStorageCompactionAttemptUtc,
				LastStorageCompactionFailureKind = record.LastStorageCompactionFailureKind,
				LastStorageCompactionError = record.LastStorageCompactionError,
				CreatedAt = record.CreatedAt,
				UpdatedAt = DateTime.UtcNow
			});
		}
		finally
		{
			CryptographicOperations.ZeroMemory(vaultKey);
			if (newKek is not null)
				CryptographicOperations.ZeroMemory(newKek);
			_secureStr.ZeroMemory(newBytes);
		}
	}

	public StorageCompactionInfo GetStorageCompactionInfo()
	{
		var header = _repo.Get();
		return header is null ? BuildNoPendingStorageInfo() : BuildStorageCompactionInfo(header, autoRetryDeferred: false, overrideMessage: null);
	}

	public StorageCompactionInfo RetryPendingStorageCompaction()
	{
		var header = _repo.Get()
			?? throw new InvalidOperationException("Kein Master-Key vorhanden.");

		ValidateHeader(header);

		var sessionKey = _session.GetSessionKey();
		if (sessionKey is null)
		{
			var info = BuildStorageCompactionInfo(header, autoRetryDeferred: false, overrideMessage: "Speicherbereinigung noch offen: Die Vault ist aktuell gesperrt. Bitte zuerst entsperren.");
			return new StorageCompactionInfo
			{
				IsPending = info.IsPending,
				AutoRetryDeferred = info.AutoRetryDeferred,
				LastAttemptUtc = info.LastAttemptUtc,
				NextAutomaticRetryUtc = info.NextAutomaticRetryUtc,
				FailureKind = StorageCompactionFailureKind.BusyOrLocked,
				UserMessage = info.UserMessage,
				LastError = info.LastError,
			};
		}

		try
		{
			return CompleteStorageCompactionIfRequired(header, sessionKey, automaticRetry: false);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(sessionKey);
		}
	}

	private CredentialMigrationPlan? BuildMigrationPlan(VaultHeader header, byte[] currentVaultKey, byte[] kek, bool usesLegacyKey)
	{
		var credentials = _vaultMigrationRepo.GetAllCredentials();
		var migratedCredentials = new List<CredentialRecord>();
		var targetVaultKey = usesLegacyKey ? RandomNumberGenerator.GetBytes(32) : currentVaultKey.ToArray();
		var requiresStorageCompaction = header.RequiresStorageCompaction;
		var headerNeedsUpgrade = header.FormatVersion != VaultHeaderFormatVersion.Current ||
			header.UsesLegacyKeyMaterial != usesLegacyKey ||
			header.LegacyPasswordHash.Length > 0 ||
			header.WrappedVaultKey.Length != WrappedVaultKeyPacketBytes;

		try
		{
			foreach (var credential in credentials)
			{
				if (!NeedsCredentialMigration(credential, usesLegacyKey))
					continue;

				var needsSecretMigration = NeedsSecretMigration(credential) || usesLegacyKey;
				var needsMetadataMigration = NeedsMetadataMigration(credential) || usesLegacyKey;
				requiresStorageCompaction |= HasPlaintextMetadataResidue(credential);
				byte[]? secretPlaintext = null;
				CredentialRecord? metadataRecord = null;
				try
				{
					var migrated = CloneCredential(credential);
					migrated.CredentialUuid = EnsureCredentialUuid(migrated.CredentialUuid);

					if (needsSecretMigration)
					{
						secretPlaintext = DecryptSecretForMigration(credential, currentVaultKey, header.FormatVersion);
						migrated.SecretFormatVersion = CredentialSecretFormatVersion.Current;
						migrated.EncryptedPassword = _credentialEnvelope.Encrypt(secretPlaintext, targetVaultKey, migrated, VaultHeaderFormatVersion.Current);
					}

					if (needsMetadataMigration)
					{
						metadataRecord = DecryptMetadataForMigration(credential, currentVaultKey, header.FormatVersion);
						migrated.Title = metadataRecord.Title;
						migrated.Username = metadataRecord.Username;
						migrated.Url = metadataRecord.Url;
						migrated.Notes = metadataRecord.Notes;
						migrated.IconKey = metadataRecord.IconKey;
						migrated.CredentialType = metadataRecord.CredentialType;
						migrated.MetadataFormatVersion = _credentialEnvelope.CurrentMetadataVersion;
						migrated.EncryptedMetadata = _credentialEnvelope.EncryptMetadata(migrated, targetVaultKey, VaultHeaderFormatVersion.Current);
						migrated = SanitizePersistedMetadata(migrated);
					}

					migrated.UpdatedAt = DateTime.UtcNow;
					migratedCredentials.Add(migrated);
				}
				finally
				{
					if (secretPlaintext is not null)
						CryptographicOperations.ZeroMemory(secretPlaintext);
					if (metadataRecord is not null)
						metadataRecord.EncryptedMetadata = [];
				}
			}

			if (!headerNeedsUpgrade && migratedCredentials.Count == 0)
			{
				CryptographicOperations.ZeroMemory(targetVaultKey);
				return null;
			}

			var migratedHeader = CloneHeader(header);
			migratedHeader.FormatVersion = VaultHeaderFormatVersion.Current;
			migratedHeader.KdfIdentifier = _kdf.Identifier;
			migratedHeader.KdfParameters = CloneParameters(header.KdfParameters);
			migratedHeader.LegacyPasswordHash = [];
			migratedHeader.WrappedVaultKey = _encryption.Encrypt(targetVaultKey, kek);
			migratedHeader.UsesLegacyKeyMaterial = false;
			migratedHeader.RequiresStorageCompaction = requiresStorageCompaction;
			migratedHeader.LastStorageCompactionAttemptUtc = null;
			migratedHeader.LastStorageCompactionFailureKind = StorageCompactionFailureKind.None;
			migratedHeader.LastStorageCompactionError = null;
			migratedHeader.UpdatedAt = DateTime.UtcNow;

			return new CredentialMigrationPlan(migratedHeader, migratedCredentials, targetVaultKey);
		}
		catch
		{
			CryptographicOperations.ZeroMemory(targetVaultKey);
			throw;
		}
	}

	private byte[] DecryptForMigration(CredentialRecord credential, byte[] currentVaultKey, int vaultFormatVersion)
		=> DecryptSecretForMigration(credential, currentVaultKey, vaultFormatVersion);

	private byte[] DecryptSecretForMigration(CredentialRecord credential, byte[] currentVaultKey, int vaultFormatVersion)
		=> credential.SecretFormatVersion switch
		{
			CredentialSecretFormatVersion.Legacy => _encryption.Decrypt(credential.EncryptedPassword, currentVaultKey),
			CredentialSecretFormatVersion.AesGcmV1 => _credentialEnvelope.Decrypt(credential, currentVaultKey, vaultFormatVersion),
			CredentialSecretFormatVersion.AesGcmV2 => _credentialEnvelope.Decrypt(credential, currentVaultKey, vaultFormatVersion),
			_ => throw new InvalidOperationException($"Nicht unterstuetzte Secret-Formatversion: {credential.SecretFormatVersion}"),
		};

	private CredentialRecord DecryptMetadataForMigration(CredentialRecord credential, byte[] currentVaultKey, int vaultFormatVersion)
		=> credential.MetadataFormatVersion switch
		{
			CredentialMetadataFormatVersion.Legacy => CloneCredential(credential),
			CredentialMetadataFormatVersion.AesGcmV1 => _credentialEnvelope.DecryptMetadata(credential, currentVaultKey, vaultFormatVersion),
			_ => throw new InvalidOperationException($"Nicht unterstuetzte Metadaten-Formatversion: {credential.MetadataFormatVersion}"),
		};

	private void ValidateHeader(VaultHeader header)
	{
		ArgumentNullException.ThrowIfNull(header);

		if (header.FormatVersion < VaultHeaderFormatVersion.Legacy || header.FormatVersion > VaultHeaderFormatVersion.Current)
			throw new InvalidOperationException($"Nicht unterstuetzte VaultHeader-Version: {header.FormatVersion}");

		if (!string.Equals(header.KdfIdentifier, _kdf.Identifier, StringComparison.Ordinal))
			throw new InvalidOperationException($"Nicht unterstuetzter KDF-Identifier: {header.KdfIdentifier}");

		if (header.KdfParameters is null)
			throw new InvalidOperationException("VaultHeader enthaelt keine KDF-Parameter.");

		_kdf.ValidateParameters(header.KdfParameters);

		if (header.Salt.Length != header.KdfParameters.SaltLengthBytes)
			throw new InvalidOperationException("VaultHeader enthaelt einen ungueltigen Salt.");

		if (header.FormatVersion == VaultHeaderFormatVersion.Legacy)
		{
			if (header.WrappedVaultKey.Length != 0)
				throw new InvalidOperationException("Legacy-VaultHeader darf keinen WrappedVaultKey enthalten.");

			if (header.LegacyPasswordHash.Length == 0)
				throw new InvalidOperationException("Legacy-VaultHeader enthaelt keinen Passwort-Hash.");

			return;
		}

		if (header.WrappedVaultKey.Length != WrappedVaultKeyPacketBytes)
			throw new InvalidOperationException("VaultHeader enthaelt einen ungueltigen WrappedVaultKey.");
	}

	private StorageCompactionInfo CompleteStorageCompactionIfRequired(VaultHeader header, byte[] activeVaultKey, bool automaticRetry)
	{
		if (!header.RequiresStorageCompaction)
			return BuildNoPendingStorageInfo();

		var now = DateTime.UtcNow;
		if (automaticRetry && header.LastStorageCompactionAttemptUtc is DateTime lastAttemptUtc)
		{
			var nextRetryUtc = lastAttemptUtc + AutomaticStorageCompactionRetryDelay;
			if (nextRetryUtc > now)
				return BuildStorageCompactionInfo(header, autoRetryDeferred: true, overrideMessage: $"Speicherbereinigung noch offen. Naechster automatischer Versuch nach {nextRetryUtc.ToLocalTime():HH:mm}. Du kannst die Bereinigung jederzeit manuell erneut starten.");
		}

		if (!_vaultMigrationRepo.HasPendingStorageArtifacts())
		{
			var validationFailure = ValidateStorageRewriteReadiness(header, activeVaultKey);
			if (validationFailure is not null)
				return PersistStorageCompactionFailure(header, validationFailure.Value.failureKind, validationFailure.Value.userMessage, validationFailure.Value.lastError);
		}

		var result = _vaultMigrationRepo.CompactStorage();
		var updated = CloneHeader(header);
		updated.LastStorageCompactionAttemptUtc = now;
		updated.LastStorageCompactionFailureKind = result.FailureKind;
		updated.LastStorageCompactionError = result.LastError;

		if (!result.IsPending)
		{
			updated.RequiresStorageCompaction = false;
			updated.LastStorageCompactionAttemptUtc = null;
			updated.LastStorageCompactionFailureKind = StorageCompactionFailureKind.None;
			updated.LastStorageCompactionError = null;
			updated.UpdatedAt = DateTime.UtcNow;
			_repo.Update(updated);
			return result;
		}

		updated.RequiresStorageCompaction = true;
		updated.UpdatedAt = DateTime.UtcNow;
		_repo.Update(updated);
		return BuildStorageCompactionInfo(updated, autoRetryDeferred: false, overrideMessage: result.UserMessage);
	}

	private (StorageCompactionFailureKind failureKind, string userMessage, string lastError)? ValidateStorageRewriteReadiness(VaultHeader header, byte[] activeVaultKey)
	{
		try
		{
			ValidateHeaderForStorageRewrite(header);
			foreach (var credential in _vaultMigrationRepo.GetAllCredentials())
				ValidateCredentialForStorageRewrite(credential, activeVaultKey, header.FormatVersion);

			return null;
		}
		catch (CryptographicException ex)
		{
			return (StorageCompactionFailureKind.Corruption, "Speicherbereinigung noch offen: Aktuelle Vault-Daten konnten nicht mehr authentifiziert werden. Backup pruefen.", ex.Message);
		}
		catch (InvalidOperationException ex)
		{
			return (StorageCompactionFailureKind.Corruption, "Speicherbereinigung noch offen: Aktuelle Vault-Daten sind inkonsistent. Backup pruefen.", ex.Message);
		}
		catch (JsonException ex)
		{
			return (StorageCompactionFailureKind.Corruption, "Speicherbereinigung noch offen: Aktuelle Vault-Metadaten sind inkonsistent. Backup pruefen.", ex.Message);
		}
	}

	private StorageCompactionInfo PersistStorageCompactionFailure(VaultHeader header, StorageCompactionFailureKind failureKind, string userMessage, string lastError)
	{
		var updated = CloneHeader(header);
		updated.RequiresStorageCompaction = true;
		updated.LastStorageCompactionAttemptUtc = DateTime.UtcNow;
		updated.LastStorageCompactionFailureKind = failureKind;
		updated.LastStorageCompactionError = lastError;
		updated.UpdatedAt = DateTime.UtcNow;
		_repo.Update(updated);
		return BuildStorageCompactionInfo(updated, autoRetryDeferred: false, overrideMessage: userMessage);
	}

	private void ValidateHeaderForStorageRewrite(VaultHeader header)
	{
		if (header.FormatVersion != VaultHeaderFormatVersion.Current)
			throw new InvalidOperationException("Storage-Rewrite erwartet einen aktuellen VaultHeader.");

		if (header.UsesLegacyKeyMaterial)
			throw new InvalidOperationException("Storage-Rewrite darf nicht mit Legacy-Keymaterial ausgefuehrt werden.");

		if (header.LegacyPasswordHash.Length > 0)
			throw new InvalidOperationException("Storage-Rewrite erwartet keinen Legacy-Passwort-Hash mehr im Header.");
	}

	private void ValidateCredentialForStorageRewrite(CredentialRecord credential, byte[] activeVaultKey, int vaultFormatVersion)
	{
		if (credential.SecretFormatVersion != CredentialSecretFormatVersion.Current)
			throw new InvalidOperationException($"Credential {credential.Id} verwendet noch ein Legacy-Secret-Format.");

		if (credential.MetadataFormatVersion != CredentialMetadataFormatVersion.Current)
			throw new InvalidOperationException($"Credential {credential.Id} verwendet noch ein Legacy-Metadaten-Format.");

		if (!Guid.TryParseExact(credential.CredentialUuid, "N", out _))
			throw new InvalidOperationException($"Credential {credential.Id} enthaelt eine ungueltige CredentialUuid.");

		if (credential.EncryptedPassword.Length == 0)
			throw new InvalidOperationException($"Credential {credential.Id} enthaelt keine verschluesselten Secret-Daten.");

		if (credential.EncryptedMetadata.Length == 0)
			throw new InvalidOperationException($"Credential {credential.Id} enthaelt keine verschluesselten Metadaten.");

		if (HasPlaintextMetadataResidue(credential))
			throw new InvalidOperationException($"Credential {credential.Id} enthaelt unerwartete Klartext-Metadaten.");

		var secret = _credentialEnvelope.Decrypt(credential, activeVaultKey, vaultFormatVersion);
		try
		{
		}
		finally
		{
			CryptographicOperations.ZeroMemory(secret);
		}

		_ = _credentialEnvelope.DecryptMetadata(credential, activeVaultKey, vaultFormatVersion);
	}

	private static bool NeedsCredentialMigration(CredentialRecord credential, bool usesLegacyKey)
		=> usesLegacyKey || NeedsSecretMigration(credential) || NeedsMetadataMigration(credential);

	private static bool NeedsSecretMigration(CredentialRecord credential)
		=> credential.SecretFormatVersion != CredentialSecretFormatVersion.Current || !Guid.TryParseExact(credential.CredentialUuid, "N", out _);

	private static bool NeedsMetadataMigration(CredentialRecord credential)
		=> credential.MetadataFormatVersion != CredentialMetadataFormatVersion.Current ||
			!Guid.TryParseExact(credential.CredentialUuid, "N", out _) ||
			HasPlaintextMetadataResidue(credential);

	private static bool HasPlaintextMetadataResidue(CredentialRecord credential)
		=> !string.IsNullOrEmpty(credential.Title) ||
			!string.IsNullOrEmpty(credential.Username) ||
			!string.IsNullOrEmpty(credential.Url) ||
			!string.IsNullOrEmpty(credential.Notes) ||
			!string.IsNullOrEmpty(credential.IconKey) ||
			credential.CredentialType != CredentialType.Password;

	private static string EnsureCredentialUuid(string credentialUuid)
		=> Guid.TryParseExact(credentialUuid, "N", out _) ? credentialUuid : Guid.NewGuid().ToString("N");

	private static VaultKdfParameters CloneParameters(VaultKdfParameters parameters) => new()
	{
		HashAlgorithm = parameters.HashAlgorithm,
		Iterations = parameters.Iterations,
		KeyLengthBytes = parameters.KeyLengthBytes,
		SaltLengthBytes = parameters.SaltLengthBytes,
	};

	private static CredentialRecord CloneCredential(CredentialRecord credential) => new()
	{
		Id = credential.Id,
		Title = credential.Title,
		Username = credential.Username,
		EncryptedPassword = credential.EncryptedPassword.ToArray(),
		EncryptedMetadata = credential.EncryptedMetadata.ToArray(),
		CredentialUuid = credential.CredentialUuid,
		SecretFormatVersion = credential.SecretFormatVersion,
		MetadataFormatVersion = credential.MetadataFormatVersion,
		Url = credential.Url,
		Notes = credential.Notes,
		CreatedAt = credential.CreatedAt,
		UpdatedAt = credential.UpdatedAt,
		IconKey = credential.IconKey,
		CredentialType = credential.CredentialType,
	};

	private static CredentialRecord SanitizePersistedMetadata(CredentialRecord credential)
	{
		credential.Title = string.Empty;
		credential.Username = null;
		credential.Url = null;
		credential.Notes = null;
		credential.IconKey = null;
		credential.CredentialType = CredentialType.Password;
		return credential;
	}

	private static VaultHeader CloneHeader(VaultHeader header) => new()
	{
		FormatVersion = header.FormatVersion,
		KdfIdentifier = header.KdfIdentifier,
		KdfParameters = CloneParameters(header.KdfParameters),
		Salt = header.Salt.ToArray(),
		WrappedVaultKey = header.WrappedVaultKey.ToArray(),
		LegacyPasswordHash = header.LegacyPasswordHash.ToArray(),
		UsesLegacyKeyMaterial = header.UsesLegacyKeyMaterial,
		RequiresStorageCompaction = header.RequiresStorageCompaction,
		LastStorageCompactionAttemptUtc = header.LastStorageCompactionAttemptUtc,
		LastStorageCompactionFailureKind = header.LastStorageCompactionFailureKind,
		LastStorageCompactionError = header.LastStorageCompactionError,
		CreatedAt = header.CreatedAt,
		UpdatedAt = header.UpdatedAt,
	};

	private static StorageCompactionInfo BuildNoPendingStorageInfo() => new()
	{
		IsPending = false,
		FailureKind = StorageCompactionFailureKind.None,
		UserMessage = string.Empty,
	};

	private static StorageCompactionInfo BuildStorageCompactionInfo(VaultHeader header, bool autoRetryDeferred, string? overrideMessage)
	{
		if (!header.RequiresStorageCompaction)
			return BuildNoPendingStorageInfo();

		DateTime? nextRetryUtc = header.LastStorageCompactionAttemptUtc is DateTime lastAttemptUtc
			? lastAttemptUtc + AutomaticStorageCompactionRetryDelay
			: null;
		var message = overrideMessage;
		if (string.IsNullOrWhiteSpace(message))
		{
			message = header.LastStorageCompactionFailureKind switch
			{
				StorageCompactionFailureKind.None => "Speicherbereinigung steht noch aus. Die Vault ist entsperrt, aber alte Speicherreste koennen bis zur erfolgreichen Bereinigung verbleiben.",
				StorageCompactionFailureKind.BusyOrLocked => "Speicherbereinigung noch offen: Die Vault-Datei oder ein Rewrite-Artefakt ist noch gesperrt. Andere Apps schliessen und erneut versuchen.",
				StorageCompactionFailureKind.InsufficientSpace => "Speicherbereinigung noch offen: Fuer den Vault-Rewrite ist nicht genug freier Speicherplatz vorhanden.",
				StorageCompactionFailureKind.Io => "Speicherbereinigung noch offen: Beim Rewrite der Vault-Datei ist ein I/O-Fehler aufgetreten.",
				StorageCompactionFailureKind.Corruption => "Speicherbereinigung noch offen: Aktuelle Vault-Daten sind inkonsistent oder beschaedigt. Backup pruefen.",
				_ => "Speicherbereinigung noch offen: Der Vault-Rewrite konnte nicht abgeschlossen werden.",
			};
		}

		return new StorageCompactionInfo
		{
			IsPending = true,
			AutoRetryDeferred = autoRetryDeferred,
			LastAttemptUtc = header.LastStorageCompactionAttemptUtc,
			NextAutomaticRetryUtc = nextRetryUtc,
			FailureKind = header.LastStorageCompactionFailureKind,
			LastError = header.LastStorageCompactionError,
			UserMessage = message,
		};
	}

	private sealed record CredentialMigrationPlan(VaultHeader Header, IReadOnlyList<CredentialRecord> Credentials, byte[] ActiveVaultKey);
}
