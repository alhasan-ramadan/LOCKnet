using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LOCKnet.App.ViewModels;
using System.Security;

namespace LOCKnet.App.ViewModels;

/// <summary>
/// ViewModel für den Login-Screen (Ersteinrichtung + Entsperren).
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
	public event EventHandler? UnlockSucceeded;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
	[NotifyCanExecuteChangedFor(nameof(SetupCommand))]
	private string _password = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SetupCommand))]
	private string _confirmPassword = string.Empty;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private bool _isSetupMode;

	public LoginViewModel()
	{
		IsSetupMode = !AppServices.Current.MasterKeyManager.IsInitialized;
	}

	// ── Commands ──────────────────────────────────────────────────────────────

	[RelayCommand(CanExecute = nameof(CanUnlock))]
	private void Unlock()
	{
		ErrorMessage = string.Empty;
		try
		{
			var secure = ToSecureString(Password);
			var key = AppServices.Current.MasterKeyManager.Unlock(secure);
			if (key is null)
			{
				ErrorMessage = "Falsches Passwort.";
				return;
			}

			AppServices.Current.SessionManager.Open(key);
			AppServices.Current.ActivityMonitor.Start();
			UnlockSucceeded?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private bool CanUnlock() => Password.Length > 0;

	[RelayCommand(CanExecute = nameof(CanSetup))]
	private void Setup()
	{
		ErrorMessage = string.Empty;
		if (Password != ConfirmPassword)
		{
			ErrorMessage = "Passwörter stimmen nicht überein.";
			return;
		}
		if (Password.Length < 8)
		{
			ErrorMessage = "Passwort muss mindestens 8 Zeichen lang sein.";
			return;
		}

		try
		{
			var secure = ToSecureString(Password);
			AppServices.Current.MasterKeyManager.Initialize(secure);
			IsSetupMode = false;

			// Direkt einloggen nach Setup
			var key = AppServices.Current.MasterKeyManager.Unlock(secure);
			AppServices.Current.SessionManager.Open(key!);
			AppServices.Current.ActivityMonitor.Start();
			UnlockSucceeded?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private bool CanSetup() => Password.Length > 0 && ConfirmPassword.Length > 0;

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static SecureString ToSecureString(string s)
	{
		var secure = new SecureString();
		foreach (var c in s) secure.AppendChar(c);
		secure.MakeReadOnly();
		return secure;
	}
}
