using LOCKnet.App;
using LOCKnet.App.ViewModels;
using LOCKnet.Core.DataAbstractions;
using LOCKnet.Core.Security;
using System.Reflection;
using System.Security;

namespace LOCKnet.Data.Tests;

public sealed class AppViewModelTests
{
	[Fact]
	public void LoginViewModel_InSetupMode_ValidatesAndInitializesVault()
	{
		using var scope = new AppServicesScope();
		var vm = new LoginViewModel();

		Assert.True(vm.IsSetupMode);

		using var mismatch = AppServicesScope.MakeSecure("12345678");
		using var mismatchConfirm = AppServicesScope.MakeSecure("12345679");
		vm.Setup(mismatch, mismatchConfirm);
		Assert.Contains("stimmen nicht", vm.ErrorMessage);

		using var shortPassword = AppServicesScope.MakeSecure("1234567");
		using var shortConfirm = AppServicesScope.MakeSecure("1234567");
		vm.Setup(shortPassword, shortConfirm);
		Assert.Contains("mindestens 8", vm.ErrorMessage);

		var fired = false;
		vm.UnlockSucceeded += (_, _) => fired = true;
		using var valid = AppServicesScope.MakeSecure("12345678");
		using var validConfirm = AppServicesScope.MakeSecure("12345678");
		vm.Setup(valid, validConfirm);

		Assert.False(vm.IsSetupMode);
		Assert.True(fired);
		Assert.True(AppServices.Current.SessionManager.IsUnlocked);
		Assert.True(AppServices.Current.ActivityMonitor.IsRunning);
	}

	[Fact]
	public void LoginViewModel_Unlock_HandlesWrongAndCorrectPassword()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true);
		var vm = new LoginViewModel();

		Assert.False(vm.IsSetupMode);

		using var wrong = AppServicesScope.MakeSecure("wrong-password");
		vm.Unlock(wrong);
		Assert.Contains("Falsches Passwort", vm.ErrorMessage);

		var fired = false;
		vm.UnlockSucceeded += (_, _) => fired = true;
		using var correct = AppServicesScope.MakeSecure("test-master-password");
		vm.Unlock(correct);

		Assert.True(fired);
		Assert.True(AppServices.Current.SessionManager.IsUnlocked);
	}

	[Fact]
	public void LockScreenViewModel_Unlock_HandlesWrongAndCorrectPassword()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true);
		var vm = new LockScreenViewModel();

		using var wrong = AppServicesScope.MakeSecure("wrong-password");
		vm.Unlock(wrong);
		Assert.Contains("Falsches Passwort", vm.ErrorMessage);

		var fired = false;
		vm.UnlockSucceeded += (_, _) => fired = true;
		using var correct = AppServicesScope.MakeSecure("test-master-password");
		vm.Unlock(correct);

		Assert.True(fired);
		Assert.True(AppServices.Current.SessionManager.IsUnlocked);
		Assert.True(AppServices.Current.ActivityMonitor.IsRunning);
	}

	[Fact]
	public void LockScreenViewModel_Unlock_WhenVaultNotInitialized_MapsInvalidOperationMessage()
	{
		using var scope = new AppServicesScope();
		var vm = new LockScreenViewModel();

		using var password = AppServicesScope.MakeSecure("whatever");
		vm.Unlock(password);

		Assert.Contains("Initialize", vm.ErrorMessage);
	}

	[Fact]
	public void CredentialDetailViewModel_NewAndEditModes_ExecuteCommandsAndPersist()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true, unlockSession: true);
		var vm = new CredentialDetailViewModel();

		Assert.False(vm.IsEditMode);
		Assert.Equal("Neues Credential", vm.WindowTitle);
		Assert.True(vm.IsPasswordCredential);
		Assert.False(vm.IsApiKeyCredential);

		vm.Title = "GitHub";
		vm.Password = string.Empty;
		Assert.False(vm.SaveCommand.CanExecute(null));

		vm.CredentialType = CredentialType.ApiKey;
		Assert.False(vm.IsPasswordCredential);
		Assert.True(vm.IsApiKeyCredential);
		Assert.Contains("API", vm.SecretFieldLabel);
		Assert.Contains("API", vm.SecretFieldWatermark);
		Assert.Contains("Client", vm.UsernameLabel);
		vm.SaveCommand.Execute(null);
		Assert.Contains("API-Schl", vm.ErrorMessage);

		vm.IsPasswordCredential = true;
		Assert.True(vm.IsPasswordCredential);
		Assert.False(vm.IsApiKeyCredential);
		Assert.Contains("Passwort", vm.SecretFieldLabel);

		vm.Password = "secret-value";
		var saved = false;
		vm.SaveCompleted += (_, _) => saved = true;
		vm.SaveCommand.Execute(null);

		Assert.True(saved);
		var stored = AppServices.Current.CredentialService.GetAll().Single();
		Assert.Equal("GitHub", stored.Title);

		vm.TogglePasswordVisibilityCommand.Execute(null);
		Assert.True(vm.IsPasswordVisible);
		vm.TogglePasswordVisibilityCommand.Execute(null);
		Assert.False(vm.IsPasswordVisible);

		vm.ToggleGeneratorCommand.Execute(null);
		Assert.True(vm.ShowGenerator);
		vm.GeneratePasswordCommand.Execute(null);
		Assert.False(vm.ShowGenerator);
		Assert.NotEmpty(vm.Password);
		Assert.True(vm.Password.Length >= 8);

		vm.SetIconCommand.Execute("Key");
		Assert.Equal("Key", vm.IconKey);

		var cancelled = false;
		vm.Cancelled += (_, _) => cancelled = true;
		vm.CancelCommand.Execute(null);
		Assert.True(cancelled);

		var editVm = new CredentialDetailViewModel(stored);
		Assert.True(editVm.IsEditMode);
		Assert.Equal("Credential bearbeiten", editVm.WindowTitle);
		editVm.IsApiKeyCredential = true;
		Assert.True(editVm.IsApiKeyCredential);
		Assert.Contains("aktuellen API", editVm.SecretFieldWatermark);
		editVm.Title = "GitHub Updated";
		editVm.Password = string.Empty;
		editVm.SaveCommand.Execute(null);

		Assert.Equal("GitHub Updated", AppServices.Current.CredentialService.GetAll().Single().Title);

		AppServices.Current.SessionManager.Lock();
		editVm.Password = "new-secret";
		editVm.SaveCommand.Execute(null);
		Assert.False(string.IsNullOrWhiteSpace(editVm.ErrorMessage));
	}

	[Fact]
	public async Task CredentialListViewModel_CommandsFilteringAndLockTimerPaths_Work()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true, unlockSession: true);
		AppServices.Current.CredentialService.Add("GitHub", "alice", AppServicesScope.MakeSecure("pw-1"));
		AppServices.Current.CredentialService.Add("GitLab", "bob", AppServicesScope.MakeSecure("pw-2"));

		var vm = new CredentialListViewModel();
		try
		{
			Assert.Equal(2, vm.Credentials.Count);

			vm.SearchText = "hub";
			Assert.Single(vm.Credentials);

			CredentialRecord? addRecord = new();
			vm.AddEditRequested += (_, record) => addRecord = record;
			vm.AddCommand.Execute(null);
			Assert.Null(addRecord);

			vm.SelectedCredential = vm.Credentials.Single();
			CredentialRecord? editRecord = null;
			vm.AddEditRequested += (_, record) => editRecord = record;
			vm.EditCommand.Execute(null);
			Assert.NotNull(editRecord);

			vm.CopyPasswordCommand.Execute(null);
			Assert.Contains("kopiert", vm.StatusMessage);

			vm.SelectedCredential = new CredentialRecord { Id = 9999, Title = "missing" };
			vm.CopyPasswordCommand.Execute(null);
			Assert.Contains("nicht gefunden", vm.StatusMessage);

			vm.SelectedCredential = vm.Credentials.Single();
			vm.DeleteCommand.Execute(null);
			Assert.Single(AppServices.Current.CredentialService.GetAll());
			vm.SearchText = string.Empty;

			AppServices.Current.SessionManager.Lock();
			vm.SelectedCredential = vm.Credentials.Single();
			vm.DeleteCommand.Execute(null);
			Assert.False(string.IsNullOrWhiteSpace(vm.StatusMessage));

			vm.CopyPasswordCommand.Execute(null);
			Assert.False(string.IsNullOrWhiteSpace(vm.StatusMessage));

			vm.Refresh();
			Assert.False(string.IsNullOrWhiteSpace(vm.StatusMessage));

			using var unlockPassword2 = AppServicesScope.MakeSecure("test-master-password");
			var unlock2 = AppServices.Current.MasterKeyManager.Unlock(unlockPassword2)!;
			AppServices.Current.SessionManager.Open(unlock2.VaultKey);

			var tutorialRequested = false;
			vm.TutorialRequested += (_, _) => tutorialRequested = true;
			vm.ShowTutorialCommand.Execute(null);
			Assert.True(tutorialRequested);

			var lockRequested = false;
			vm.LockRequested += (_, _) => lockRequested = true;
			vm.LockCommand.Execute(null);
			Assert.True(lockRequested);
			Assert.False(AppServices.Current.SessionManager.IsUnlocked);

			var updateLockTimer = typeof(CredentialListViewModel).GetMethod("UpdateLockTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
			updateLockTimer.Invoke(vm, null);
			Assert.Equal(string.Empty, vm.LockTimerText);

			using var unlockPassword = AppServicesScope.MakeSecure("test-master-password");
			var unlock = AppServices.Current.MasterKeyManager.Unlock(unlockPassword)!;
			AppServices.Current.SessionManager.Open(unlock.VaultKey);
			AppServices.Current.ActivityMonitor.Timeout = TimeSpan.FromSeconds(10);
			AppServices.Current.ActivityMonitor.Start();
			updateLockTimer.Invoke(vm, null);
			Assert.Contains(":", vm.LockTimerText);

			var lastActivityField = typeof(ActivityMonitor).GetField("<LastActivity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
			lastActivityField.SetValue(AppServices.Current.ActivityMonitor, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(20));
			AppServices.Current.ActivityMonitor.Timeout = TimeSpan.FromSeconds(1);
			updateLockTimer.Invoke(vm, null);
			Assert.Contains("0:00", vm.LockTimerText);

			MarkStorageCleanupPending();
			await vm.RetryStorageCleanupCommand.ExecuteAsync(null);
			Assert.False(vm.IsRetryingStorageCleanup);
			Assert.True(vm.RetryStorageCleanupCommand.CanExecute(null) || !vm.IsStorageCleanupPending);
		}
		finally
		{
			StopCountdownTimer(vm);
		}
	}

	[Fact]
	public void MainWindowViewModel_PrivateNavigationMethods_SwitchViews()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true, unlockSession: true, ensureAvalonia: true);
		var vm = new MainWindowViewModel();

		Assert.IsType<LoginViewModel>(vm.CurrentView);

		var showLogin = typeof(MainWindowViewModel).GetMethod("ShowLogin", BindingFlags.Instance | BindingFlags.NonPublic)!;
		var showCredentialList = typeof(MainWindowViewModel).GetMethod("ShowCredentialList", BindingFlags.Instance | BindingFlags.NonPublic)!;
		var showTutorial = typeof(MainWindowViewModel).GetMethod("ShowTutorial", BindingFlags.Instance | BindingFlags.NonPublic)!;
		var showCredentialDetail = typeof(MainWindowViewModel).GetMethod("ShowCredentialDetail", BindingFlags.Instance | BindingFlags.NonPublic)!;

		showLogin.Invoke(vm, null);
		Assert.IsType<LoginViewModel>(vm.CurrentView);

		showCredentialList.Invoke(vm, null);
		Assert.IsType<CredentialListViewModel>(vm.CurrentView);
		StopCountdownTimer((CredentialListViewModel)vm.CurrentView);

		showTutorial.Invoke(vm, null);
		Assert.IsType<TutorialViewModel>(vm.CurrentView);

		showCredentialDetail.Invoke(vm, [null]);
		Assert.IsType<CredentialDetailViewModel>(vm.CurrentView);
		var detail = (CredentialDetailViewModel)vm.CurrentView;
		detail.Title = "FromMainWindow";
		detail.Password = "abc12345";
		detail.SaveCommand.Execute(null);
		Assert.IsType<CredentialListViewModel>(vm.CurrentView);
		StopCountdownTimer((CredentialListViewModel)vm.CurrentView);

		var existingRecord = new CredentialRecord
		{
			Id = 1,
			Title = "Existing",
			CredentialUuid = Guid.NewGuid().ToString("N"),
			EncryptedPassword = [0x01, 0x02, 0x03],
			EncryptedMetadata = [0x04, 0x05, 0x06],
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
		};
		showCredentialDetail.Invoke(vm, [existingRecord]);
		Assert.IsType<CredentialDetailViewModel>(vm.CurrentView);

		var showLockScreen = typeof(MainWindowViewModel).GetMethod("ShowLockScreen", BindingFlags.Instance | BindingFlags.NonPublic)!;
		showLockScreen.Invoke(vm, null);
		Thread.Sleep(50);
		if (vm.CurrentView is CredentialListViewModel pendingList)
			StopCountdownTimer(pendingList);
		Assert.True(vm.CurrentView is CredentialDetailViewModel or LockScreenViewModel or CredentialListViewModel);
	}

	[Fact]
	public void LoginViewModel_MapErrorAndCatchPaths_AreCovered()
	{
		{
			using var scope = new AppServicesScope();
			var vm = new LoginViewModel();
			using var password = AppServicesScope.MakeSecure("12345678");
			vm.Unlock(password);
			Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
		}

		{
			using var initializedScope = new AppServicesScope(initializeMasterKey: true);
			var initializedVm = new LoginViewModel();
			using var p = AppServicesScope.MakeSecure("12345678");
			using var c = AppServicesScope.MakeSecure("12345678");
			initializedVm.Setup(p, c);
			Assert.False(string.IsNullOrWhiteSpace(initializedVm.ErrorMessage));
		}

		var mapError = typeof(LoginViewModel).GetMethod("MapError", BindingFlags.Static | BindingFlags.NonPublic)!;
		Assert.Equal("arg", mapError.Invoke(null, [new ArgumentException("arg")]) as string);
		Assert.Equal("Der Vorgang konnte nicht abgeschlossen werden.", mapError.Invoke(null, [new Exception("boom")]) as string);
	}

	[Fact]
	public void TutorialViewModel_NavigationAndCloseEvent_CoverAllPaths()
	{
		var vm = new TutorialViewModel();
		Assert.Equal(5, vm.Steps.Length);
		Assert.Equal(0, vm.CurrentStep);
		Assert.False(vm.CanGoBack);
		Assert.True(vm.CanGoForward);

		vm.BackCommand.Execute(null);
		Assert.Equal(0, vm.CurrentStep);

		vm.ForwardCommand.Execute(null);
		vm.ForwardCommand.Execute(null);
		vm.ForwardCommand.Execute(null);
		vm.ForwardCommand.Execute(null);
		Assert.True(vm.IsLastStep);
		Assert.False(vm.CanGoForward);

		vm.ForwardCommand.Execute(null);
		Assert.Equal(4, vm.CurrentStep);

		vm.BackCommand.Execute(null);
		Assert.Equal(3, vm.CurrentStep);

		var closed = false;
		vm.Closed += (_, _) => closed = true;
		vm.CloseCommand.Execute(null);
		Assert.True(closed);
	}

	[Fact]
	public void DesignData_ExposesSampleCredentialList()
	{
		var data = DesignData.CredentialList;

		Assert.Equal(2, data.Credentials.Count);
		Assert.NotEmpty(data.Credentials[0].Title);
		Assert.Equal("0:58", data.LockTimerText.Replace("\u23F1 ", string.Empty));
	}

	private static void StopCountdownTimer(CredentialListViewModel vm)
	{
		var field = typeof(CredentialListViewModel).GetField("_countdownTimer", BindingFlags.Instance | BindingFlags.NonPublic);
		var timer = field?.GetValue(vm);
		timer?.GetType().GetMethod("Stop", BindingFlags.Instance | BindingFlags.Public)?.Invoke(timer, null);
	}

	private static void MarkStorageCleanupPending()
	{
		var manager = AppServices.Current.MasterKeyManager;
		var repoField = typeof(MasterKeyManager).GetField("_repo", BindingFlags.Instance | BindingFlags.NonPublic)!;
		var repo = (IMasterKeyRepository)repoField.GetValue(manager)!;
		var header = repo.Get()!;
		header.RequiresStorageCompaction = true;
		header.LastStorageCompactionFailureKind = StorageCompactionFailureKind.BusyOrLocked;
		header.LastStorageCompactionAttemptUtc = DateTime.UtcNow.AddHours(-1);
		repo.Update(header);
	}
}
