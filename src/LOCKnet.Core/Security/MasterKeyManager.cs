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
	private readonly IKeyDerivationService _kdf;
	private readonly IMasterKeyRepository _repo;
	private readonly IEncryptionService _encryption;
	private readonly ISecureStringService _secureStr;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="MasterKeyManager"/>.
	/// </summary>
	public MasterKeyManager(
		IKeyDerivationService kdf,
		IMasterKeyRepository repo,
		IEncryptionService encryption,
		ISecureStringService secureStr)
	{
		ArgumentNullException.ThrowIfNull(kdf);
		ArgumentNullException.ThrowIfNull(repo);
		ArgumentNullException.ThrowIfNull(encryption);
		ArgumentNullException.ThrowIfNull(secureStr);
		_kdf = kdf;
		_repo = repo;
		_encryption = encryption;
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
				FormatVersion = 1,
				KdfIdentifier = _kdf.Identifier,
				KdfParameters = parameters,
				Salt = salt,
				WrappedVaultKey = wrappedVaultKey,
				LegacyPasswordHash = [],
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

		var passwordBytes = _secureStr.ToByteArray(password);
		byte[]? kek = null;
		try
		{
			var parameters = record.KdfParameters ?? _kdf.GetDefaultParameters();
			kek = _kdf.DeriveKey(passwordBytes, record.Salt, parameters);

			if (record.WrappedVaultKey.Length > 0)
			{
				try
				{
					return _encryption.Decrypt(record.WrappedVaultKey, kek);
				}
				catch (CryptographicException)
				{
					return null;
				}
			}

			if (record.LegacyPasswordHash.Length == 0 ||
				!_kdf.VerifyPassword(passwordBytes, record.Salt, record.LegacyPasswordHash, parameters))
			{
				return null;
			}

			var legacyVaultKey = _kdf.DeriveKey(passwordBytes, record.Salt, parameters);
			var migrated = CloneHeader(record);
			migrated.FormatVersion = 1;
			migrated.KdfIdentifier = _kdf.Identifier;
			migrated.KdfParameters = parameters;
			migrated.WrappedVaultKey = _encryption.Encrypt(legacyVaultKey, kek);
			migrated.LegacyPasswordHash = [];
			migrated.UpdatedAt = DateTime.UtcNow;
			_repo.Update(migrated);
			return legacyVaultKey;
		}
		finally
		{
			if (kek is not null)
				CryptographicOperations.ZeroMemory(kek);
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
				FormatVersion = 1,
				KdfIdentifier = _kdf.Identifier,
				KdfParameters = parameters,
				Salt = newSalt,
				WrappedVaultKey = wrappedVaultKey,
				LegacyPasswordHash = [],
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

	private static VaultHeader CloneHeader(VaultHeader header) => new()
	{
		FormatVersion = header.FormatVersion,
		KdfIdentifier = header.KdfIdentifier,
		KdfParameters = new VaultKdfParameters
		{
			HashAlgorithm = header.KdfParameters.HashAlgorithm,
			Iterations = header.KdfParameters.Iterations,
			KeyLengthBytes = header.KdfParameters.KeyLengthBytes,
			SaltLengthBytes = header.KdfParameters.SaltLengthBytes,
		},
		Salt = header.Salt.ToArray(),
		WrappedVaultKey = header.WrappedVaultKey.ToArray(),
		LegacyPasswordHash = header.LegacyPasswordHash.ToArray(),
		CreatedAt = header.CreatedAt,
		UpdatedAt = header.UpdatedAt,
	};
}
