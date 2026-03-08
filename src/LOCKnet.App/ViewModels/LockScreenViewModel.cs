using CommunityToolkit.Mvvm.ComponentModel;
using System.Security;

namespace LOCKnet.App.ViewModels;

/// <summary>
/// Wird angezeigt, wenn die Sitzung durch Auto-Lock oder manuell gesperrt wurde.
/// </summary>
public partial class LockScreenViewModel : ViewModelBase
{
	public event EventHandler? UnlockSucceeded;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

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
			ErrorMessage = ex is InvalidOperationException ? ex.Message : "Entsperren fehlgeschlagen.";
		}
	}
}
