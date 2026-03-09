using Avalonia;
using Avalonia.Headless;
using LOCKnet.App;
using System.Security;
using System.Security.Cryptography;
using System.Threading;

namespace LOCKnet.Data.Tests;

internal sealed class AppServicesScope : IDisposable
{
	private const string DefaultPassword = "test-master-password";
	private static int _avaloniaInitialized;

	public AppServicesScope(bool initializeMasterKey = false, bool unlockSession = false, bool ensureAvalonia = false)
	{
		DatabasePath = Path.Combine(Path.GetTempPath(), $"locknet-app-tests-{Guid.NewGuid():N}.db");
		AppServices.Initialize(DatabasePath);

		if (initializeMasterKey)
		{
			using var secure = MakeSecure(DefaultPassword);
			if (!AppServices.Current.MasterKeyManager.IsInitialized)
				AppServices.Current.MasterKeyManager.Initialize(secure);

			if (unlockSession)
			{
				var unlock = AppServices.Current.MasterKeyManager.Unlock(secure)!;
				VaultKey = unlock.VaultKey.ToArray();
				AppServices.Current.SessionManager.Open(unlock.VaultKey);
				CryptographicOperations.ZeroMemory(unlock.VaultKey);
			}
		}

		if (ensureAvalonia)
			EnsureAvaloniaInitialized();
	}

	public string DatabasePath { get; }

	public byte[]? VaultKey { get; }

	public static SecureString MakeSecure(string value)
	{
		var secure = new SecureString();
		foreach (var c in value)
			secure.AppendChar(c);
		secure.MakeReadOnly();
		return secure;
	}

	public static void EnsureAvaloniaInitialized()
	{
		if (Interlocked.Exchange(ref _avaloniaInitialized, 1) == 1)
			return;

		AppBuilder.Configure<LOCKnet.App.App>()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions())
			.SetupWithoutStarting();
	}

	public void Dispose()
	{
		if (VaultKey is not null)
			CryptographicOperations.ZeroMemory(VaultKey);

		try
		{
			AppServices.Current.ActivityMonitor.Stop();
			AppServices.Current.SessionManager.Lock();
		}
		catch (InvalidOperationException)
		{
		}

		TryDelete(DatabasePath);
		TryDelete(DatabasePath + "-wal");
		TryDelete(DatabasePath + "-shm");
	}

	private static void TryDelete(string path)
	{
		if (!File.Exists(path))
			return;

		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
		}
	}
}
