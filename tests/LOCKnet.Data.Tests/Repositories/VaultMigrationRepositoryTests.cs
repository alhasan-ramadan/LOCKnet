using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests.Repositories;

public class VaultMigrationRepositoryTests : IDisposable
{
	private readonly SqliteConnection _keepAlive;
	private readonly string _connectionString;
	private readonly MasterKeyRepository _masterKeyRepository;
	private readonly CredentialsRepository _credentialsRepository;
	private readonly VaultMigrationRepository _sut;

	public VaultMigrationRepositoryTests()
	{
		var dbName = $"vault_migration_{Guid.NewGuid():N}";
		var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
		_connectionString = connectionString;

		_keepAlive = new SqliteConnection(connectionString);
		_keepAlive.Open();

		new Database(connectionString, true).Initialize();
		_masterKeyRepository = new MasterKeyRepository(connectionString);
		_credentialsRepository = new CredentialsRepository(connectionString);
		_sut = new VaultMigrationRepository(connectionString);
	}

	public void Dispose() => _keepAlive.Dispose();

	[Fact]
	public void ApplyMigration_UpdatesHeaderAndCredentialInOneCall()
	{
		_masterKeyRepository.Create(new VaultHeader
		{
			FormatVersion = VaultHeaderFormatVersion.Legacy,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters
			{
				HashAlgorithm = "SHA256",
				Iterations = 600_000,
				KeyLengthBytes = 32,
				SaltLengthBytes = 32,
			},
			Salt = Enumerable.Repeat((byte)0xAA, 32).ToArray(),
			LegacyPasswordHash = Enumerable.Repeat((byte)0xBB, 32).ToArray(),
			WrappedVaultKey = [],
			UsesLegacyKeyMaterial = true,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		});

		using (var connection = new SqliteConnection(_connectionString))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = @"
                INSERT INTO Credentials (Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion, URL, Notes, IconKey, CredentialType)
                VALUES ($title, $username, $password, $encryptedMetadata, $credentialUuid, $secretFormatVersion, $metadataFormatVersion, $url, $notes, $iconKey, $credentialType);";
			command.Parameters.AddWithValue("$title", "GitHub");
			command.Parameters.AddWithValue("$username", "legacy-user");
			command.Parameters.AddWithValue("$password", new byte[] { 0x01, 0x02, 0x03 });
			command.Parameters.AddWithValue("$encryptedMetadata", DBNull.Value);
			command.Parameters.AddWithValue("$credentialUuid", string.Empty);
			command.Parameters.AddWithValue("$secretFormatVersion", CredentialSecretFormatVersion.Legacy);
			command.Parameters.AddWithValue("$metadataFormatVersion", CredentialMetadataFormatVersion.Legacy);
			command.Parameters.AddWithValue("$url", "https://legacy.example");
			command.Parameters.AddWithValue("$notes", "legacy-note");
			command.Parameters.AddWithValue("$iconKey", "legacy-icon");
			command.Parameters.AddWithValue("$credentialType", (int)CredentialType.ApiKey);
			command.ExecuteNonQuery();
		}

		var credential = _credentialsRepository.GetAll().Single();
		var migratedCredential = new CredentialRecord
		{
			Id = credential.Id,
			Title = string.Empty,
			Username = null,
			EncryptedPassword = [0x09, 0x08, 0x07],
			EncryptedMetadata = [0x06, 0x05, 0x04],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			CredentialType = CredentialType.Password,
			Url = null,
			Notes = null,
			IconKey = null,
			CreatedAt = credential.CreatedAt,
			UpdatedAt = DateTime.UtcNow,
		};

		var migratedHeader = _masterKeyRepository.Get()!;
		migratedHeader.FormatVersion = VaultHeaderFormatVersion.Current;
		migratedHeader.WrappedVaultKey = Enumerable.Repeat((byte)0xCC, 60).ToArray();
		migratedHeader.LegacyPasswordHash = [];
		migratedHeader.UsesLegacyKeyMaterial = false;

		_sut.ApplyMigration(migratedHeader, [migratedCredential]);

		var storedHeader = _masterKeyRepository.Get()!;
		var storedCredential = _credentialsRepository.GetById(credential.Id)!;
		Assert.Equal(VaultHeaderFormatVersion.Current, storedHeader.FormatVersion);
		Assert.False(storedHeader.UsesLegacyKeyMaterial);
		Assert.Equal(migratedCredential.CredentialUuid, storedCredential.CredentialUuid);
		Assert.Equal(CredentialSecretFormatVersion.Current, storedCredential.SecretFormatVersion);
		Assert.Equal(CredentialMetadataFormatVersion.Current, storedCredential.MetadataFormatVersion);
		Assert.Equal(migratedCredential.EncryptedPassword, storedCredential.EncryptedPassword);
		Assert.Equal(migratedCredential.EncryptedMetadata, storedCredential.EncryptedMetadata);
		Assert.Equal(string.Empty, storedCredential.Title);
		Assert.Null(storedCredential.Username);
	}

	[Fact]
	public void ApplyMigration_PersistsPendingStorageCompactionStateFromHeader()
	{
		_masterKeyRepository.Create(new VaultHeader
		{
			FormatVersion = VaultHeaderFormatVersion.Legacy,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters
			{
				HashAlgorithm = "SHA256",
				Iterations = 600_000,
				KeyLengthBytes = 32,
				SaltLengthBytes = 32,
			},
			Salt = Enumerable.Repeat((byte)0xAA, 32).ToArray(),
			LegacyPasswordHash = Enumerable.Repeat((byte)0xBB, 32).ToArray(),
			WrappedVaultKey = [],
			UsesLegacyKeyMaterial = true,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		});

		using (var connection = new SqliteConnection(_connectionString))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = @"
                INSERT INTO Credentials (Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion, URL, Notes, IconKey, CredentialType)
                VALUES ($title, $username, $password, $encryptedMetadata, $credentialUuid, $secretFormatVersion, $metadataFormatVersion, $url, $notes, $iconKey, $credentialType);";
			command.Parameters.AddWithValue("$title", "GitHub");
			command.Parameters.AddWithValue("$username", "legacy-user");
			command.Parameters.AddWithValue("$password", new byte[] { 0x01, 0x02, 0x03 });
			command.Parameters.AddWithValue("$encryptedMetadata", DBNull.Value);
			command.Parameters.AddWithValue("$credentialUuid", string.Empty);
			command.Parameters.AddWithValue("$secretFormatVersion", CredentialSecretFormatVersion.Legacy);
			command.Parameters.AddWithValue("$metadataFormatVersion", CredentialMetadataFormatVersion.Legacy);
			command.Parameters.AddWithValue("$url", "https://legacy.example");
			command.Parameters.AddWithValue("$notes", "legacy-note");
			command.Parameters.AddWithValue("$iconKey", "legacy-icon");
			command.Parameters.AddWithValue("$credentialType", (int)CredentialType.ApiKey);
			command.ExecuteNonQuery();
		}

		var credential = _credentialsRepository.GetAll().Single();
		var migratedCredential = new CredentialRecord
		{
			Id = credential.Id,
			Title = string.Empty,
			Username = null,
			EncryptedPassword = [0x09, 0x08, 0x07],
			EncryptedMetadata = [0x06, 0x05, 0x04],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			CredentialType = CredentialType.Password,
			Url = null,
			Notes = null,
			IconKey = null,
			CreatedAt = credential.CreatedAt,
			UpdatedAt = DateTime.UtcNow,
		};

		var lastAttemptUtc = DateTime.UtcNow.AddMinutes(-3);
		var migratedHeader = _masterKeyRepository.Get()!;
		migratedHeader.FormatVersion = VaultHeaderFormatVersion.Current;
		migratedHeader.WrappedVaultKey = Enumerable.Repeat((byte)0xCC, 60).ToArray();
		migratedHeader.LegacyPasswordHash = [];
		migratedHeader.UsesLegacyKeyMaterial = false;
		migratedHeader.RequiresStorageCompaction = true;
		migratedHeader.LastStorageCompactionAttemptUtc = lastAttemptUtc;
		migratedHeader.LastStorageCompactionFailureKind = StorageCompactionFailureKind.InsufficientSpace;
		migratedHeader.LastStorageCompactionError = "disk-full";

		_sut.ApplyMigration(migratedHeader, [migratedCredential]);

		var storedHeader = _masterKeyRepository.Get()!;
		Assert.True(storedHeader.RequiresStorageCompaction);
		Assert.Equal(lastAttemptUtc, storedHeader.LastStorageCompactionAttemptUtc);
		Assert.Equal(StorageCompactionFailureKind.InsufficientSpace, storedHeader.LastStorageCompactionFailureKind);
		Assert.Equal("disk-full", storedHeader.LastStorageCompactionError);
	}

	[Fact]
	public void ApplyMigration_CurrentRecordWithPlaintextMetadata_ThrowsInvalidOperationException()
	{
		_masterKeyRepository.Create(new VaultHeader
		{
			FormatVersion = VaultHeaderFormatVersion.Current,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters
			{
				HashAlgorithm = "SHA256",
				Iterations = 600_000,
				KeyLengthBytes = 32,
				SaltLengthBytes = 32,
			},
			Salt = Enumerable.Repeat((byte)0xAA, 32).ToArray(),
			WrappedVaultKey = Enumerable.Repeat((byte)0xCC, 60).ToArray(),
			LegacyPasswordHash = [],
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		});

		var invalid = new CredentialRecord
		{
			Id = 1,
			Title = "plaintext",
			EncryptedPassword = [0x01, 0x02],
			EncryptedMetadata = [0x03, 0x04],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		};

		Assert.Throws<InvalidOperationException>(() => _sut.ApplyMigration(_masterKeyRepository.Get()!, [invalid]));
	}

	[Fact]
	public void ApplyMigration_CurrentRecordWithMalformedUuid_IsRejectedByDatabaseTrigger()
	{
		_masterKeyRepository.Create(new VaultHeader
		{
			FormatVersion = VaultHeaderFormatVersion.Current,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters
			{
				HashAlgorithm = "SHA256",
				Iterations = 600_000,
				KeyLengthBytes = 32,
				SaltLengthBytes = 32,
			},
			Salt = Enumerable.Repeat((byte)0xAA, 32).ToArray(),
			WrappedVaultKey = Enumerable.Repeat((byte)0xCC, 60).ToArray(),
			LegacyPasswordHash = [],
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		});

		using (var connection = new SqliteConnection(_connectionString))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = @"
                INSERT INTO Credentials (Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion, URL, Notes, IconKey, CredentialType)
                VALUES ($title, $username, $password, $encryptedMetadata, $credentialUuid, $secretFormatVersion, $metadataFormatVersion, $url, $notes, $iconKey, $credentialType);";
			command.Parameters.AddWithValue("$title", string.Empty);
			command.Parameters.AddWithValue("$username", DBNull.Value);
			command.Parameters.AddWithValue("$password", new byte[] { 0x01, 0x02, 0x03 });
			command.Parameters.AddWithValue("$encryptedMetadata", new byte[] { 0x04, 0x05, 0x06 });
			command.Parameters.AddWithValue("$credentialUuid", "not-a-valid-current-uuid");
			command.Parameters.AddWithValue("$secretFormatVersion", CredentialSecretFormatVersion.Current);
			command.Parameters.AddWithValue("$metadataFormatVersion", CredentialMetadataFormatVersion.Current);
			command.Parameters.AddWithValue("$url", DBNull.Value);
			command.Parameters.AddWithValue("$notes", DBNull.Value);
			command.Parameters.AddWithValue("$iconKey", DBNull.Value);
			command.Parameters.AddWithValue("$credentialType", (int)CredentialType.Password);

			var ex = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
			Assert.Contains("Current metadata records must not persist plaintext metadata.", ex.Message);
		}
	}

	[Fact]
	public void ApplyMigration_CurrentRecordWithValidUuid_IsAcceptedByDatabaseTrigger()
	{
		_masterKeyRepository.Create(new VaultHeader
		{
			FormatVersion = VaultHeaderFormatVersion.Current,
			KdfIdentifier = "PBKDF2-SHA256",
			KdfParameters = new VaultKdfParameters
			{
				HashAlgorithm = "SHA256",
				Iterations = 600_000,
				KeyLengthBytes = 32,
				SaltLengthBytes = 32,
			},
			Salt = Enumerable.Repeat((byte)0xAA, 32).ToArray(),
			WrappedVaultKey = Enumerable.Repeat((byte)0xCC, 60).ToArray(),
			LegacyPasswordHash = [],
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		});

		var credentialUuid = Guid.NewGuid().ToString("N");

		using (var connection = new SqliteConnection(_connectionString))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = @"
                INSERT INTO Credentials (Title, Username, EncryptedPassword, EncryptedMetadata, CredentialUuid, SecretFormatVersion, MetadataFormatVersion, URL, Notes, IconKey, CredentialType)
                VALUES ($title, $username, $password, $encryptedMetadata, $credentialUuid, $secretFormatVersion, $metadataFormatVersion, $url, $notes, $iconKey, $credentialType);";
			command.Parameters.AddWithValue("$title", string.Empty);
			command.Parameters.AddWithValue("$username", DBNull.Value);
			command.Parameters.AddWithValue("$password", new byte[] { 0x01, 0x02, 0x03 });
			command.Parameters.AddWithValue("$encryptedMetadata", new byte[] { 0x04, 0x05, 0x06 });
			command.Parameters.AddWithValue("$credentialUuid", credentialUuid);
			command.Parameters.AddWithValue("$secretFormatVersion", CredentialSecretFormatVersion.Current);
			command.Parameters.AddWithValue("$metadataFormatVersion", CredentialMetadataFormatVersion.Current);
			command.Parameters.AddWithValue("$url", DBNull.Value);
			command.Parameters.AddWithValue("$notes", DBNull.Value);
			command.Parameters.AddWithValue("$iconKey", DBNull.Value);
			command.Parameters.AddWithValue("$credentialType", (int)CredentialType.Password);

			command.ExecuteNonQuery();
		}

		var stored = _credentialsRepository.GetAll().Single();
		Assert.Equal(credentialUuid, stored.CredentialUuid);
		Assert.Equal(CredentialMetadataFormatVersion.Current, stored.MetadataFormatVersion);
	}
}
