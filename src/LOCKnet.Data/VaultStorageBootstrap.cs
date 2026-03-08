using LOCKnet.Core.DataAbstractions;
using LOCKnet.Data.Repositories;

namespace LOCKnet.Data;

/// <summary>
/// Bietet den aktuellen Bootstrap- und Routing-Seam fuer Storage-Modus, Datenbankinitialisierung und Repository-Erzeugung.
/// </summary>
public sealed class VaultStorageBootstrap
{
	private readonly ISqliteConnectionFactory _connectionFactory;

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="VaultStorageBootstrap"/> fuer einen Vault-Pfad.
	/// </summary>
	/// <param name="databasePath">Pfad zur Vault-Datei.</param>
	public VaultStorageBootstrap(string databasePath)
		: this(new PlainSqliteConnectionFactory(databasePath))
	{
	}

	/// <summary>
	/// Initialisiert eine neue Instanz von <see cref="VaultStorageBootstrap"/> fuer eine zentrale Connection-Factory.
	/// </summary>
	/// <param name="connectionFactory">Factory fuer Storage-spezifische SQLite-Verbindungen.</param>
	public VaultStorageBootstrap(ISqliteConnectionFactory connectionFactory)
	{
		ArgumentNullException.ThrowIfNull(connectionFactory);

		_connectionFactory = connectionFactory;
		Storage = connectionFactory.Storage;
	}

	/// <summary>
	/// Der erkannte Storage-Descriptor fuer die aktuelle Vault.
	/// </summary>
	public VaultStorageDescriptor Storage { get; }

	/// <summary>
	/// Initialisiert den aktuell ohne Schluessel erreichbaren Storage.
	/// </summary>
	public void InitializeAccessibleStorage()
	{
		if (Storage.RequiresKeyAtOpen)
			return;

		new Database(_connectionFactory).Initialize();
	}

	/// <summary>
	/// Erzeugt das Credential-Repository fuer den aktuellen Storage-Modus.
	/// </summary>
	/// <returns>Die aktuelle Implementierung von <see cref="ICredentialRepository"/>.</returns>
	public ICredentialRepository CreateCredentialRepository() => new CredentialsRepository(_connectionFactory);

	/// <summary>
	/// Erzeugt das MasterKey-Repository fuer den aktuellen Storage-Modus.
	/// </summary>
	/// <returns>Die aktuelle Implementierung von <see cref="IMasterKeyRepository"/>.</returns>
	public IMasterKeyRepository CreateMasterKeyRepository() => new MasterKeyRepository(_connectionFactory);

	/// <summary>
	/// Erzeugt das Settings-Repository fuer den aktuellen Storage-Modus.
	/// </summary>
	/// <returns>Die aktuelle Implementierung von <see cref="ISettingsRepository"/>.</returns>
	public ISettingsRepository CreateSettingsRepository() => new SettingsRepository(_connectionFactory);

	/// <summary>
	/// Erzeugt das Migrations-Repository fuer den aktuellen Storage-Modus.
	/// </summary>
	/// <returns>Die aktuelle Implementierung von <see cref="IVaultMigrationRepository"/>.</returns>
	public IVaultMigrationRepository CreateVaultMigrationRepository() => new VaultMigrationRepository(_connectionFactory);

	/// <summary>
	/// Erzeugt einen Bootstrap fuer Tests mit direktem Connection-String.
	/// </summary>
	/// <param name="connectionString">SQLite-Connection-String.</param>
	/// <returns>Ein Bootstrap fuer die aktuelle Plain-SQLite-Implementierung.</returns>
	internal static VaultStorageBootstrap FromConnectionString(string connectionString)
		=> new(PlainSqliteConnectionFactory.FromConnectionString(connectionString));
}
