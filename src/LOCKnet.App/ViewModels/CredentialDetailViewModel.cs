using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Services;
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
	private readonly IPasswordGeneratorService _generator = new PasswordGeneratorService();
	private readonly IPasswordStrengthService _strengthService = new PasswordStrengthService();

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

	[ObservableProperty]
	private string _iconKey = string.Empty;

	[ObservableProperty]
	private bool _useUppercase = true;

	[ObservableProperty]
	private bool _useLowercase = true;

	[ObservableProperty]
	private bool _useDigits = true;

	[ObservableProperty]
	private bool _useSpecial = true;

	[ObservableProperty]
	private int _passwordLength = 16;

	[ObservableProperty]
	private int _strengthScore;

	[ObservableProperty]
	private string _strengthLabel = string.Empty;

	[ObservableProperty]
	private string _strengthColor = "#50506C";

	[ObservableProperty]
	private string _strengthSeg1 = "#2E3460";

	[ObservableProperty]
	private string _strengthSeg2 = "#2E3460";

	[ObservableProperty]
	private string _strengthSeg3 = "#2E3460";

	[ObservableProperty]
	private string _strengthSeg4 = "#2E3460";

	[ObservableProperty]
	private string _strengthSeg5 = "#2E3460";

	[ObservableProperty]
	private bool _showGenerator;

	public bool IsEditMode => _editId.HasValue;
	public string WindowTitle => IsEditMode ? "Credential bearbeiten" : "Neues Credential";

	// ── Konstruktor (neu) ─────────────────────────────────────────────────────
	public CredentialDetailViewModel()
	{
		UpdateStrengthDetails(Password);
	}

	// ── Konstruktor (bearbeiten) ───────────────────────────────────────────────
	public CredentialDetailViewModel(CredentialRecord record)
	{
		_editId = record.Id;
		Title = record.Title;
		Username = record.Username ?? string.Empty;
		Url = record.Url ?? string.Empty;
		Notes = record.Notes ?? string.Empty;
		IconKey = record.IconKey ?? string.Empty;
		// Passwort wird entschlüsselt geladen
		try
		{
			var pw = AppServices.Current.CredentialService.GetPassword(record.Id);
			if (pw is not null)
				Password = SecureStringToString(pw);
		}
		catch { /* Im Fehlerfall leer lassen */ }

		UpdateStrengthDetails(Password);
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
					string.IsNullOrWhiteSpace(Notes) ? null : Notes,
					string.IsNullOrWhiteSpace(IconKey) ? null : IconKey);
			}
			else
			{
				AppServices.Current.CredentialService.Add(
					Title,
					string.IsNullOrWhiteSpace(Username) ? null : Username,
					secure,
					string.IsNullOrWhiteSpace(Url) ? null : Url,
					string.IsNullOrWhiteSpace(Notes) ? null : Notes,
					string.IsNullOrWhiteSpace(IconKey) ? null : IconKey);
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

	[RelayCommand]
	private void ToggleGenerator() => ShowGenerator = !ShowGenerator;

	[RelayCommand]
	private void GeneratePassword()
	{
		var generated = _generator.Generate(new PasswordGeneratorOptions
		{
			Length = PasswordLength,
			UseUppercase = UseUppercase,
			UseLowercase = UseLowercase,
			UseDigits = UseDigits,
			UseSpecial = UseSpecial
		});

		Password = generated;
		ShowGenerator = false;
	}

	[RelayCommand]
	private void SetIcon(string key) => IconKey = key;

	partial void OnPasswordChanged(string value) => UpdateStrengthDetails(value);

	partial void OnStrengthScoreChanged(int value)
	{
		var litSegments = GetLitStrengthSegments();
		StrengthSeg1 = litSegments >= 1 ? StrengthColor : "#2E3460";
		StrengthSeg2 = litSegments >= 2 ? StrengthColor : "#2E3460";
		StrengthSeg3 = litSegments >= 3 ? StrengthColor : "#2E3460";
		StrengthSeg4 = litSegments >= 4 ? StrengthColor : "#2E3460";
		StrengthSeg5 = litSegments >= 5 ? StrengthColor : "#2E3460";
	}

	partial void OnStrengthColorChanged(string value)
	{
		var litSegments = GetLitStrengthSegments();
		StrengthSeg1 = litSegments >= 1 ? value : "#2E3460";
		StrengthSeg2 = litSegments >= 2 ? value : "#2E3460";
		StrengthSeg3 = litSegments >= 3 ? value : "#2E3460";
		StrengthSeg4 = litSegments >= 4 ? value : "#2E3460";
		StrengthSeg5 = litSegments >= 5 ? value : "#2E3460";
	}

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

	private void UpdateStrengthDetails(string password)
	{
		var strength = _strengthService.Evaluate(password);
		StrengthScore = strength.Score;
		StrengthLabel = strength.Label;
		StrengthColor = strength.Color;
	}

	private int GetLitStrengthSegments()
	{
		if (string.IsNullOrEmpty(Password))
		{
			return 0;
		}

		return Math.Clamp(StrengthScore + 1, 1, 5);
	}
}
