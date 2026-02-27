using CommunityToolkit.Mvvm.ComponentModel;
using LOCKnet.Core.DataAbstractions;

namespace LOCKnet.App.ViewModels;

/// <summary>
/// Shell-ViewModel: verwaltet die aktive View über <see cref="CurrentView"/>.
/// Navigation geschieht durch Austauschen des CurrentView-Objekts.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
	[ObservableProperty]
	private ViewModelBase _currentView;

	public MainWindowViewModel()
	{
		_currentView = new LoginViewModel();
		WireLoginEvents((LoginViewModel)_currentView);

		// Auf Auto-Lock reagieren
		AppServices.Current.SessionManager.Locked += (_, _) => ShowLockScreen();
	}

	// ── Navigation ────────────────────────────────────────────────────────────

	private void ShowLogin()
	{
		var vm = new LoginViewModel();
		WireLoginEvents(vm);
		CurrentView = vm;
	}

	private void ShowCredentialList()
	{
		var vm = new CredentialListViewModel();
		vm.LockRequested += (_, _) => ShowLockScreen();
		vm.AddEditRequested += (_, record) => ShowCredentialDetail(record);
		vm.TutorialRequested += (_, _) => ShowTutorial();
		CurrentView = vm;
	}

	private void ShowTutorial()
	{
		var vm = new TutorialViewModel();
		vm.Closed += (_, _) => ShowCredentialList();
		CurrentView = vm;
	}

	private void ShowLockScreen()
	{
		// Auf UI-Thread
		Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
		{
			var vm = new LockScreenViewModel();
			vm.UnlockSucceeded += (_, _) => ShowCredentialList();
			CurrentView = vm;
		});
	}

	private void ShowCredentialDetail(CredentialRecord? record)
	{
		var vm = record is null
			? new CredentialDetailViewModel()
			: new CredentialDetailViewModel(record);

		vm.SaveCompleted += (_, _) =>
		{
			ShowCredentialList();
		};
		vm.Cancelled += (_, _) => ShowCredentialList();
		CurrentView = vm;
	}

	// ── Wiring ────────────────────────────────────────────────────────────────

	private void WireLoginEvents(LoginViewModel vm)
	{
		vm.UnlockSucceeded += (_, _) => ShowCredentialList();
	}
}
