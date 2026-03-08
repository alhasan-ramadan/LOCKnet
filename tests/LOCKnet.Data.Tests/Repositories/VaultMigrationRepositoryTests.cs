using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data;
using LOCKnet.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace LOCKnet.Data.Tests.Repositories;

public class VaultMigrationRepositoryTests : IDisposable
{
	private readonly SqliteConnection _keepAlive;
	private readonly MasterKeyRepository _masterKeyRepository;
	private readonly CredentialsRepository _credentialsRepository;
	private readonly VaultMigrationRepository _sut;

	public VaultMigrationRepositoryTests()
	{
		var dbName = $"vault_migration_{Guid.NewGuid():N}";
		var connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

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

		_credentialsRepository.Add(new CredentialRecord
		{
			Title = "GitHub",
			Username = "legacy-user",
			EncryptedPassword = [0x01, 0x02, 0x03],
			EncryptedMetadata = [],
			CredentialUuid = string.Empty,
			SecretFormatVersion = CredentialSecretFormatVersion.Legacy,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Legacy,
			CredentialType = CredentialType.ApiKey,
			Url = "https://legacy.example",
			Notes = "legacy-note",
			IconKey = "legacy-icon",
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		});

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
}
