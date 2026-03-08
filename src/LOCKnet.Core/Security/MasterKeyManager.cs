using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using System.Security;
using System.Security.Cryptography;

namespace LOCKnet.Core.Security;

/// <summary>
/// Implementierung von <see cref="IMasterKeyManager"/>.
/// Delegiert Schlüsselableitung an <see cref="IKeyDerivationService"/> und
/// Persistenz an <see cref="IMasterKeyRepository"/>.
/// </summary>
public sealed class MasterKeyManager : IMasterKeyManager
{
	private const int WrappedVaultKeyPacketBytes = 60;
	private readonly IKeyDerivationService _kdf;
	private readonly IMasterKeyRepository _repo;
	private readonly IVaultMigrationRepository _vaultMigrationRepo;
	private readonly IEncryptionService _encryption;
	private readonly ICredentialEnvelopeService _credentialEnvelope;
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
		ISecureStringService secureStr)
	{
		ArgumentNullException.ThrowIfNull(kdf);
		ArgumentNullException.ThrowIfNull(repo);
		ArgumentNullException.ThrowIfNull(vaultMigrationRepo);
		ArgumentNullException.ThrowIfNull(encryption);
		ArgumentNullException.ThrowIfNull(credentialEnvelope);
		ArgumentNullException.ThrowIfNull(secureStr);
		_kdf = kdf;
		_repo = repo;
		_vaultMigrationRepo = vaultMigrationRepo;
		_encryption = encryption;
		_credentialEnvelope = credentialEnvelope;
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
	public byte[]? Unlock(SecureString password)
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
					CompleteStorageCompactionIfRequired(migration.Header);
					targetVaultKey = migration.ActiveVaultKey;
					resultKey = targetVaultKey;
					targetVaultKey = null;
					return resultKey;
				}

				CompleteStorageCompactionIfRequired(record);

				resultKey = currentVaultKey;
				currentVaultKey = null;
				return resultKey;
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
			CompleteStorageCompactionIfRequired(legacyMigration.Header);
			targetVaultKey = legacyMigration.ActiveVaultKey;
			resultKey = targetVaultKey;
			targetVaultKey = null;
			return resultKey;
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

		var vaultKey = Unlock(currentPassword);
		if (vaultKey is null)
			throw new UnauthorizedAccessException("Das aktuelle Passwort ist falsch.");

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
				RequiresStorageCompaction = false,
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

	private CredentialMigrationPlan? BuildMigrationPlan(VaultHeader header, byte[] currentVaultKey, byte[] kek, bool usesLegacyKey)
	{
		var credentials = _vaultMigrationRepo.GetAllCredentials();
		var migratedCredentials = new List<CredentialRecord>();
		var targetVaultKey = usesLegacyKey ? RandomNumberGenerator.GetBytes(32) : currentVaultKey.ToArray();
		var requiresStorageCompaction = header.RequiresStorageCompaction;
		var headerNeedsUpgrade = header.FormatVersion != VaultHeaderFormatVersion.Current ||
			header.UsesLegacyKeyMaterial != usesLegacyKey ||
			header.RequiresStorageCompaction ||
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

	private void CompleteStorageCompactionIfRequired(VaultHeader header)
	{
		if (!header.RequiresStorageCompaction)
			return;

		try
		{
			_vaultMigrationRepo.CompactStorage();
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("SQLite-Kompaktierung fehlgeschlagen. Freien Speicherplatz, Dateisperren und I/O-Fehler pruefen.", ex);
		}

		var compacted = CloneHeader(header);
		compacted.RequiresStorageCompaction = false;
		compacted.UpdatedAt = DateTime.UtcNow;
		_repo.Update(compacted);
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
		CreatedAt = header.CreatedAt,
		UpdatedAt = header.UpdatedAt,
	};

	private sealed record CredentialMigrationPlan(VaultHeader Header, IReadOnlyList<CredentialRecord> Credentials, byte[] ActiveVaultKey);
}
