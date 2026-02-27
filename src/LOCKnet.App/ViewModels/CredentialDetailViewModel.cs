using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LOCKnet.Core.DataAbstractions;
using System.Security;

namespace LOCKnet.App.ViewModels;

/// <summary>
/// ViewModel für das Hinzufügen / Bearbeiten eines Credentials.
/// </summary>
public partial class CredentialDetailViewModel : ViewModelBase
{
	public event EventHandler? SaveCompleted;
	public event EventHandler? Cancelled;

	private readonly int? _editId;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
	private string _title = string.Empty;

	[ObservableProperty]
	private string _username = string.Empty;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(SaveCommand))]
	private string _password = string.Empty;

	[ObservableProperty]
	private string _url = string.Empty;

	[ObservableProperty]
	private string _notes = string.Empty;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private bool _isPasswordVisible;

	public bool IsEditMode => _editId.HasValue;
	public string WindowTitle => IsEditMode ? "Credential bearbeiten" : "Neues Credential";

	// ── Konstruktor (neu) ─────────────────────────────────────────────────────
	public CredentialDetailViewModel() { }

	// ── Konstruktor (bearbeiten) ───────────────────────────────────────────────
	public CredentialDetailViewModel(CredentialRecord record)
	{
		_editId = record.Id;
		Title = record.Title;
		Username = record.Username ?? string.Empty;
		Url = record.Url ?? string.Empty;
		Notes = record.Notes ?? string.Empty;
		// Passwort wird entschlüsselt geladen
		try
		{
			var pw = AppServices.Current.CredentialService.GetPassword(record.Id);
			if (pw is not null)
				Password = SecureStringToString(pw);
		}
		catch { /* Im Fehlerfall leer lassen */ }
	}

	// ── Commands ──────────────────────────────────────────────────────────────

	[RelayCommand(CanExecute = nameof(CanSave))]
	private void Save()
	{
		ErrorMessage = string.Empty;
		try
		{
			var secure = ToSecureString(Password);

			if (IsEditMode)
			{
				AppServices.Current.CredentialService.Update(
					_editId!.Value, Title,
					string.IsNullOrWhiteSpace(Username) ? null : Username,
					secure,
					string.IsNullOrWhiteSpace(Url) ? null : Url,
					string.IsNullOrWhiteSpace(Notes) ? null : Notes);
			}
			else
			{
				AppServices.Current.CredentialService.Add(
					Title,
					string.IsNullOrWhiteSpace(Username) ? null : Username,
					secure,
					string.IsNullOrWhiteSpace(Url) ? null : Url,
					string.IsNullOrWhiteSpace(Notes) ? null : Notes);
			}

			SaveCompleted?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private bool CanSave() => !string.IsNullOrWhiteSpace(Title) && Password.Length > 0;

	[RelayCommand]
	private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

	[RelayCommand]
	private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static SecureString ToSecureString(string s)
	{
		var secure = new SecureString();
		foreach (var c in s) secure.AppendChar(c);
		secure.MakeReadOnly();
		return secure;
	}

	private static string SecureStringToString(SecureString s)
	{
		var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(s);
		try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr) ?? string.Empty; }
		finally { System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr); }
	}
}
