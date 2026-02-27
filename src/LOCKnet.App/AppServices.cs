using LOCKnet.Core.Crypto;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using LOCKnet.Core.Services;
using LOCKnet.Data;
using LOCKnet.Data.Repositories;

namespace LOCKnet.App;

/// <summary>
/// Zentraler Service-Container. Wird einmalig beim App-Start erzeugt
/// und von ViewModels über AppServices.Current zugegriffen.
/// </summary>
public sealed class AppServices
{
	private static AppServices? _current;

	/// <summary>Die aktuelle, einmalig erzeugte Instanz.</summary>
	public static AppServices Current => _current
		?? throw new InvalidOperationException("AppServices wurde noch nicht initialisiert.");

	// ── Exposed services ──────────────────────────────────────────────────────

	public IMasterKeyManager MasterKeyManager { get; }
	public ISessionManager SessionManager { get; }
	public IActivityMonitor ActivityMonitor { get; }
	public ICredentialService CredentialService { get; }

	// ── Constructor ───────────────────────────────────────────────────────────

	private AppServices(string dbPath)
	{
		var connectionString = $"Data Source={dbPath}";

		// Data layer
		var db = new Database(dbPath);
		db.Initialize();

		ICredentialRepository credRepo = new CredentialsRepository(connectionString);
		IMasterKeyRepository masterKeyRepo = new MasterKeyRepository(connectionString);

		// Crypto layer
		IKeyDerivationService kdf = new Pbkdf2KeyDerivationService();
		IEncryptionService encryption = new AesGcmEncryptionService();
		ISecureStringService secureStr = new SecureStringService();

		// Security layer
		var sessionManager = new SessionManager();
		SessionManager = sessionManager;
		MasterKeyManager = new MasterKeyManager(kdf, masterKeyRepo, secureStr);
		ActivityMonitor = new ActivityMonitor(sessionManager)
		{
			Timeout = TimeSpan.FromSeconds(60)
		};

		// Service layer
		CredentialService = new CredentialService(credRepo, encryption, sessionManager, secureStr);
	}

	/// <summary>
	/// Initialisiert die AppServices einmalig.
	/// </summary>
	/// <param name="dbPath">Pfad zur SQLite-Datenbank.</param>
	public static void Initialize(string dbPath)
	{
		_current = new AppServices(dbPath);
	}
}
