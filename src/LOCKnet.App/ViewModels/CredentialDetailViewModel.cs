using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Services;
using System.Collections.ObjectModel;
using System.Security;
using System.Text.Json;

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
	[NotifyPropertyChangedFor(nameof(IsPasswordCredential))]
	[NotifyPropertyChangedFor(nameof(IsApiKeyCredential))]
	[NotifyPropertyChangedFor(nameof(IsBackupCodesCredential))]
	[NotifyPropertyChangedFor(nameof(SecretFieldLabel))]
	[NotifyPropertyChangedFor(nameof(SecretFieldWatermark))]
	[NotifyPropertyChangedFor(nameof(UsernameLabel))]
	private CredentialType _credentialType = CredentialType.Password;

	/// <summary>Gibt an ob der aktuelle Typ 'Passwort' ist. Steuert RadioButton-Binding.</summary>
	public bool IsPasswordCredential
	{
		get => CredentialType == CredentialType.Password;
		set { if (value) CredentialType = CredentialType.Password; }
	}

	/// <summary>Gibt an ob der aktuelle Typ 'API-Schluessel' ist. Steuert RadioButton-Binding.</summary>
	public bool IsApiKeyCredential
	{
		get => CredentialType == CredentialType.ApiKey;
		set { if (value) CredentialType = CredentialType.ApiKey; }
	}

	/// <summary>Gibt an ob der aktuelle Typ 'Backup-Codes' ist.</summary>
	public bool IsBackupCodesCredential
	{
		get => CredentialType == CredentialType.BackupCodes;
		set { if (value) CredentialType = CredentialType.BackupCodes; }
	}

	/// <summary>Dynamisches Label fuer das Geheimnis-Feld je nach Credential-Typ.</summary>
	public string SecretFieldLabel => CredentialType == CredentialType.ApiKey ? "API-Schlüssel *" : "Passwort *";

	/// <summary>Dynamischer Watermark-Text fuer das Geheimnis-Feld je nach Credential-Typ.</summary>
	public string SecretFieldWatermark => CredentialType == CredentialType.ApiKey
		? (IsEditMode ? "Leer lassen, um aktuellen API-Schlüssel zu behalten" : "API-Schlüssel eingeben")
		: (IsEditMode ? "Leer lassen, um aktuelles Passwort zu behalten" : "Passwort eingeben");

	/// <summary>Dynamisches Label fuer das Benutzername-Feld je nach Credential-Typ.</summary>
	public string UsernameLabel => CredentialType switch
	{
		CredentialType.ApiKey => "Client-ID / Bezeichner",
		CredentialType.BackupCodes => "Account / E-Mail",
		_ => "Benutzername"
	};
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

	[ObservableProperty]
	private string _backupCodesInput = string.Empty;

	[ObservableProperty]
	private bool _showUsedBackupCodes = true;

	public ObservableCollection<BackupCodeItemModel> BackupCodes { get; } = [];

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
		CredentialType = record.CredentialType;
		if (CredentialType == CredentialType.BackupCodes)
			LoadBackupCodes(record.Id);

		UpdateStrengthDetails(Password);
	}

	// ── Commands ──────────────────────────────────────────────────────────────

	[RelayCommand(CanExecute = nameof(CanSave))]
	private void Save()
	{
		ErrorMessage = string.Empty;
		try
		{
			if (CredentialType == CredentialType.BackupCodes)
			{
				if (BackupCodes.Count == 0)
				{
					ErrorMessage = "Mindestens ein Backup-Code ist erforderlich.";
					return;
				}

				Password = JsonSerializer.Serialize(new BackupCodesPayload
				{
					Items = BackupCodes.Select((item, index) => new BackupCodeEntry
					{
						Value = item.Value,
						IsUsed = item.IsUsed,
						UsedAt = item.IsUsed ? item.UsedAt : null,
						SortOrder = index,
					}).ToList()
				});
			}

			var secure = string.IsNullOrEmpty(Password) ? null : ToSecureString(Password);

			if (IsEditMode)
			{
				AppServices.Current.CredentialService.Update(
					_editId!.Value, Title,
					string.IsNullOrWhiteSpace(Username) ? null : Username,
					secure,
					string.IsNullOrWhiteSpace(Url) ? null : Url,
					string.IsNullOrWhiteSpace(Notes) ? null : Notes,
					string.IsNullOrWhiteSpace(IconKey) ? null : IconKey,
					CredentialType);
			}
			else
			{
				if (secure is null)
				{
					ErrorMessage = CredentialType == CredentialType.ApiKey ? "API-Schlüssel ist erforderlich." : "Passwort ist erforderlich.";
					return;
				}

				AppServices.Current.CredentialService.Add(
					Title,
					string.IsNullOrWhiteSpace(Username) ? null : Username,
					secure,
					string.IsNullOrWhiteSpace(Url) ? null : Url,
					string.IsNullOrWhiteSpace(Notes) ? null : Notes,
					string.IsNullOrWhiteSpace(IconKey) ? null : IconKey,
					CredentialType);
			}

			SaveCompleted?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private bool CanSave()
	{
		if (string.IsNullOrWhiteSpace(Title))
			return false;

		if (CredentialType == CredentialType.BackupCodes)
			return BackupCodes.Count > 0;

		return IsEditMode || Password.Length > 0;
	}

	[RelayCommand]
	private void ImportBackupCodes()
	{
		foreach (var code in BackupCodeParser.Parse(BackupCodesInput))
		{
			if (BackupCodes.Any(x => string.Equals(x.Value, code, StringComparison.OrdinalIgnoreCase)))
				continue;

			BackupCodes.Add(new BackupCodeItemModel
			{
				Value = code,
				IsUsed = false,
			});
		}

		BackupCodesInput = string.Empty;
		SaveCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand]
	private void ToggleBackupCodeUsed(BackupCodeItemModel? item)
	{
		if (item is null)
			return;

		item.IsUsed = !item.IsUsed;
		item.UsedAt = item.IsUsed ? DateTime.UtcNow : null;
	}

	[RelayCommand]
	private void RemoveBackupCode(BackupCodeItemModel? item)
	{
		if (item is null)
			return;

		BackupCodes.Remove(item);
		SaveCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand]
	private void CopyBackupCode(BackupCodeItemModel? item)
	{
		if (item is null)
			return;

		_ = CopyToClipboardAsync(item.Value);
	}

	[RelayCommand]
	private void CopyActiveBackupCodes()
	{
		var text = string.Join(Environment.NewLine, BackupCodes.Where(c => !c.IsUsed).Select(c => c.Value));
		if (text.Length > 0)
			_ = CopyToClipboardAsync(text);
	}

	[RelayCommand]
	private void CopyAllBackupCodes()
	{
		var text = string.Join(Environment.NewLine, BackupCodes.Select(c => c.Value));
		if (text.Length > 0)
			_ = CopyToClipboardAsync(text);
	}

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

	partial void OnCredentialTypeChanged(CredentialType value)
	{
		if (value != CredentialType.BackupCodes)
			BackupCodesInput = string.Empty;

		SaveCommand.NotifyCanExecuteChanged();
	}

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

	private void LoadBackupCodes(int id)
	{
		var secure = AppServices.Current.CredentialService.GetPassword(id);
		if (secure is null)
			return;

		var raw = SecureStringToString(secure);
		if (string.IsNullOrWhiteSpace(raw))
			return;

		try
		{
			var payload = JsonSerializer.Deserialize<BackupCodesPayload>(raw);
			if (payload?.Items is null)
				return;

			BackupCodes.Clear();
			foreach (var entry in payload.Items.OrderBy(i => i.SortOrder))
			{
				BackupCodes.Add(new BackupCodeItemModel
				{
					Value = entry.Value,
					IsUsed = entry.IsUsed,
					UsedAt = entry.IsUsed ? entry.UsedAt : null
				});
			}
		}
		catch (JsonException)
		{
			foreach (var code in BackupCodeParser.Parse(raw))
			{
				BackupCodes.Add(new BackupCodeItemModel
				{
					Value = code,
					IsUsed = false,
				});
			}
		}
	}

	private static string SecureStringToString(SecureString s)
	{
		var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(s);
		try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr) ?? string.Empty; }
		finally { System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr); }
	}

	private static async Task CopyToClipboardAsync(string text)
	{
		var clipboard = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
			? TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard
			: null;
		if (clipboard is null)
			return;

		await clipboard.SetTextAsync(text);
		_ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
			Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => await clipboard.ClearAsync()));
	}

	private sealed class BackupCodesPayload
	{
		public List<BackupCodeEntry> Items { get; set; } = [];
	}

	private sealed class BackupCodeEntry
	{
		public string Value { get; set; } = string.Empty;
		public bool IsUsed { get; set; }
		public DateTime? UsedAt { get; set; }
		public int SortOrder { get; set; }
	}
}

public partial class BackupCodeItemModel : ObservableObject
{
	[ObservableProperty]
	private string _value = string.Empty;

	[ObservableProperty]
	private bool _isUsed;

	[ObservableProperty]
	private DateTime? _usedAt;
}
