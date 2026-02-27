using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LOCKnet.Core.DataAbstractions;
using System.Collections.ObjectModel;
using System.Security;

namespace LOCKnet.App.ViewModels;

/// <summary>
/// ViewModel für die Credential-Liste (Hauptansicht nach dem Login).
/// </summary>
public partial class CredentialListViewModel : ViewModelBase
{
	public event EventHandler? LockRequested;
	public event EventHandler<CredentialRecord?>? AddEditRequested;
	public event EventHandler? TutorialRequested;

	private IReadOnlyList<CredentialRecord> _allCredentials = [];
	private DispatcherTimer? _countdownTimer;

	[ObservableProperty]
	private ObservableCollection<CredentialRecord> _credentials = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(EditCommand))]
	[NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
	[NotifyCanExecuteChangedFor(nameof(CopyPasswordCommand))]
	private CredentialRecord? _selectedCredential;

	[ObservableProperty]
	private string _searchText = string.Empty;

	[ObservableProperty]
	private string _statusMessage = string.Empty;

	[ObservableProperty]
	private string _lockTimerText = string.Empty;

	public CredentialListViewModel()
	{
		Refresh();
		StartCountdownTimer();
	}

	// ── Countdown ─────────────────────────────────────────────────────────────

	private void StartCountdownTimer()
	{
		_countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		_countdownTimer.Tick += (_, _) => UpdateLockTimer();
		_countdownTimer.Start();
		UpdateLockTimer();
	}

	private void UpdateLockTimer()
	{
		var monitor = AppServices.Current.ActivityMonitor;
		if (!monitor.IsRunning)
		{
			LockTimerText = string.Empty;
			return;
		}
		var elapsed = DateTimeOffset.UtcNow - monitor.LastActivity;
		var remaining = monitor.Timeout - elapsed;
		if (remaining <= TimeSpan.Zero)
		{
			LockTimerText = "⏱ 0:00";
			return;
		}
		LockTimerText = $"⏱ {(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
	}


	public void Refresh()
	{
		try
		{
			_allCredentials = AppServices.Current.CredentialService.GetAll();
			ApplyFilter();
		}
		catch (Exception ex)
		{
			StatusMessage = ex.Message;
		}
	}

	partial void OnSearchTextChanged(string value) => ApplyFilter();

	private void ApplyFilter()
	{
		var filtered = string.IsNullOrWhiteSpace(SearchText)
			? _allCredentials
			: _allCredentials.Where(c =>
				c.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
				(c.Username?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
				(c.Url?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));

		Credentials = new ObservableCollection<CredentialRecord>(filtered);
	}

	// ── Commands ──────────────────────────────────────────────────────────────

	[RelayCommand]
	private void Add() => AddEditRequested?.Invoke(this, null);

	[RelayCommand(CanExecute = nameof(HasSelection))]
	private void Edit() => AddEditRequested?.Invoke(this, SelectedCredential);

	[RelayCommand(CanExecute = nameof(HasSelection))]
	private void Delete()
	{
		if (SelectedCredential is null) return;
		try
		{
			AppServices.Current.CredentialService.Remove(SelectedCredential.Id);
			StatusMessage = $"\"{SelectedCredential.Title}\" gelöscht.";
			Refresh();
		}
		catch (Exception ex)
		{
			StatusMessage = ex.Message;
		}
	}

	[RelayCommand(CanExecute = nameof(HasSelection))]
	private void CopyPassword()
	{
		if (SelectedCredential is null) return;
		try
		{
			var pw = AppServices.Current.CredentialService.GetPassword(SelectedCredential.Id);
			if (pw is null)
			{
				StatusMessage = "Passwort nicht gefunden.";
				return;
			}

			var text = SecureStringToString(pw);
			var clipboard = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop ? TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard : null;
			if (clipboard is not null) _ = clipboard.SetTextAsync(text);
			StatusMessage = "Passwort kopiert.";

			// Clipboard nach 30s leeren
			_ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
				Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => { if (clipboard is not null) await clipboard.ClearAsync(); }));
		}
		catch (Exception ex)
		{
			StatusMessage = ex.Message;
		}
	}

	[RelayCommand]
	private void ShowTutorial() => TutorialRequested?.Invoke(this, EventArgs.Empty);

	[RelayCommand]
	private void Lock()
	{
		AppServices.Current.ActivityMonitor.Stop();
		AppServices.Current.SessionManager.Lock();
		LockRequested?.Invoke(this, EventArgs.Empty);
	}

	private bool HasSelection() => SelectedCredential is not null;

	// ── Helpers ───────────────────────────────────────────────────────────────

	private static string SecureStringToString(SecureString s)
	{
		var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(s);
		try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr) ?? string.Empty; }
		finally { System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr); }
	}
}
