using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LOCKnet.App.ViewModels;

/// <summary>
/// Wird angezeigt, wenn die Sitzung durch Auto-Lock oder manuell gesperrt wurde.
/// </summary>
public partial class LockScreenViewModel : ViewModelBase
{
	public event EventHandler? UnlockSucceeded;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
	private string _password = string.Empty;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[RelayCommand(CanExecute = nameof(CanUnlock))]
	private void Unlock()
	{
		ErrorMessage = string.Empty;
		try
		{
			var secure = new System.Security.SecureString();
			foreach (var c in Password) secure.AppendChar(c);
			secure.MakeReadOnly();

			var key = AppServices.Current.MasterKeyManager.Unlock(secure);
			if (key is null)
			{
				ErrorMessage = "Falsches Passwort.";
				Password = string.Empty;
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
}
