using CommunityToolkit.Mvvm.ComponentModel;
using LOCKnet.App.ViewModels;
using LOCKnet.Core.Crypto;
using System.Security;

namespace LOCKnet.App.ViewModels;

/// <summary>
/// ViewModel für den Login-Screen (Ersteinrichtung + Entsperren).
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
	public event EventHandler? UnlockSucceeded;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private bool _isSetupMode;

	public LoginViewModel()
	{
		IsSetupMode = !AppServices.Current.MasterKeyManager.IsInitialized;
	}

	public void Unlock(SecureString password)
	{
		ArgumentNullException.ThrowIfNull(password);
		ErrorMessage = string.Empty;
		try
		{
			var unlock = AppServices.Current.MasterKeyManager.Unlock(password);
			if (unlock is null)
			{
				ErrorMessage = "Falsches Passwort.";
				return;
			}

			AppServices.Current.SessionManager.Open(unlock.VaultKey);
			AppServices.Current.ActivityMonitor.Start();
			UnlockSucceeded?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			ErrorMessage = MapError(ex);
		}
	}

	public void Setup(SecureString password, SecureString confirmPassword)
	{
		ArgumentNullException.ThrowIfNull(password);
		ArgumentNullException.ThrowIfNull(confirmPassword);

		ErrorMessage = string.Empty;
		if (!SecureEquals(password, confirmPassword))
		{
			ErrorMessage = "Passwörter stimmen nicht überein.";
			return;
		}
		if (password.Length < 8)
		{
			ErrorMessage = "Passwort muss mindestens 8 Zeichen lang sein.";
			return;
		}

		try
		{
			AppServices.Current.MasterKeyManager.Initialize(password);
			IsSetupMode = false;

			var unlock = AppServices.Current.MasterKeyManager.Unlock(password)
				?? throw new InvalidOperationException("Neu angelegte Vault konnte nicht entsperrt werden.");
			AppServices.Current.SessionManager.Open(unlock.VaultKey);
			AppServices.Current.ActivityMonitor.Start();
			UnlockSucceeded?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			ErrorMessage = MapError(ex);
		}
	}

	private static bool SecureEquals(SecureString left, SecureString right)
	{
		var secureStringService = new SecureStringService();
		var leftBytes = secureStringService.ToByteArray(left);
		var rightBytes = secureStringService.ToByteArray(right);
		try
		{
			return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
		}
		finally
		{
			secureStringService.ZeroMemory(leftBytes);
			secureStringService.ZeroMemory(rightBytes);
		}
	}

	private static string MapError(Exception ex)
		=> ex switch
		{
			InvalidOperationException => ex.Message,
			ArgumentException => ex.Message,
			_ => "Der Vorgang konnte nicht abgeschlossen werden."
		};
}
