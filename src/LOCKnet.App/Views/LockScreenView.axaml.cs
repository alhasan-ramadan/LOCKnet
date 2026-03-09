using Avalonia.Controls;
using Avalonia.Interactivity;
using LOCKnet.App.ViewModels;
using System.Security;

namespace LOCKnet.App.Views;

public partial class LockScreenView : UserControl
{
	public LockScreenView()
	{
		InitializeComponent();
	}

	private void OnUnlockClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not LockScreenViewModel vm)
			return;

		var passwordInput = this.FindControl<TextBox>("PasswordInput");
		if (passwordInput is null)
			return;

		using var password = CreateSecureString(passwordInput.Text);
		vm.Unlock(password);
		passwordInput.Text = string.Empty;
	}

	private static SecureString CreateSecureString(string? value)
	{
		var secure = new SecureString();
		if (!string.IsNullOrEmpty(value))
		{
			foreach (var c in value)
				secure.AppendChar(c);
		}

		secure.MakeReadOnly();
		return secure;
	}
}
