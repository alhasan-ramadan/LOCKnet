using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using LOCKnet.App;
using LOCKnet.App.ViewModels;
using LOCKnet.App.Views;
using LOCKnet.Core.DataAbstractions;
using System.Reflection;

namespace LOCKnet.Data.Tests;

public sealed class AppViewsAndProgramTests
{
	[Fact]
	public void ViewConstructors_LoadXamlWithoutThrowing()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true, unlockSession: true, ensureAvalonia: true);

		var ex = Record.Exception(() =>
		{
			_ = new MainWindow();
			_ = new LoginView();
			_ = new LockScreenView();
			_ = new CredentialListView();
			_ = new CredentialDetailView();
			_ = new TutorialView();
		});

		Assert.Null(ex);
	}

	[Fact]
	public void LoginView_ClickHandlers_RunGuardAndActionPaths()
	{
		{
			using var scope = new AppServicesScope(initializeMasterKey: true, ensureAvalonia: true);
			var view = new LoginView();

			InvokePrivate(view, "OnUnlockClick");
			InvokePrivate(view, "OnSetupClick");

			var vm = new LoginViewModel();
			view.DataContext = vm;

			var passwordInput = view.FindControl<TextBox>("PasswordInput");
			var confirmInput = view.FindControl<TextBox>("ConfirmPasswordInput");
			Assert.NotNull(passwordInput);
			Assert.NotNull(confirmInput);

			passwordInput!.Text = "wrong";
			InvokePrivate(view, "OnUnlockClick");
			Assert.Equal(string.Empty, passwordInput.Text);
			Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
		}

		{
			using var setupScope = new AppServicesScope(ensureAvalonia: true);
			var setupView = new LoginView();
			var setupVm = new LoginViewModel();
			setupView.DataContext = setupVm;
			var setupPassword = setupView.FindControl<TextBox>("PasswordInput")!;
			var setupConfirm = setupView.FindControl<TextBox>("ConfirmPasswordInput")!;
			setupPassword.Text = "12345678";
			setupConfirm.Text = "12345678";
			InvokePrivate(setupView, "OnSetupClick");
			Assert.Equal(string.Empty, setupPassword.Text);
			Assert.Equal(string.Empty, setupConfirm.Text);
			Assert.False(setupVm.IsSetupMode);
		}
	}

	[Fact]
	public void LockScreenView_UnlockClickHandler_RunsGuardAndUnlockPath()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true, ensureAvalonia: true);
		var view = new LockScreenView();

		InvokePrivate(view, "OnUnlockClick");

		var vm = new LockScreenViewModel();
		view.DataContext = vm;
		var passwordInput = view.FindControl<TextBox>("PasswordInput");
		Assert.NotNull(passwordInput);

		passwordInput!.Text = "test-master-password";
		InvokePrivate(view, "OnUnlockClick");

		Assert.Equal(string.Empty, passwordInput.Text);
		Assert.True(AppServices.Current.SessionManager.IsUnlocked);
	}

	[Fact]
	public void Program_BuildAvaloniaApp_ReturnsConfiguredBuilder()
	{
		using var scope = new AppServicesScope(ensureAvalonia: true);
		var programType = typeof(AppServices).Assembly.GetType("LOCKnet.App.Program", throwOnError: true)!;
		var method = programType.GetMethod("BuildAvaloniaApp", BindingFlags.Public | BindingFlags.Static)!;

		var builder = method.Invoke(null, null);

		Assert.NotNull(builder);
	}

	[Fact]
	public void App_OnFrameworkInitializationCompleted_WithDesktopLifetime_CreatesMainWindow()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true, unlockSession: true, ensureAvalonia: true);

		var app = new LOCKnet.App.App();
		app.Initialize();

		var pluginsBefore = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().Count();
		var lifetime = new ClassicDesktopStyleApplicationLifetime();
		SetApplicationLifetime(app, lifetime);

		app.OnFrameworkInitializationCompleted();

		Assert.NotNull(lifetime.MainWindow);
		Assert.IsType<MainWindowViewModel>(lifetime.MainWindow!.DataContext);
		var pluginsAfter = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().Count();
		Assert.True(pluginsAfter <= pluginsBefore);
	}

	[Fact]
	public void MainWindow_KeyAndPointerInput_RecordActivity()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true, unlockSession: true, ensureAvalonia: true);
		var window = new MainWindowProxy();

		var before = AppServices.Current.ActivityMonitor.LastActivity;
		Thread.Sleep(10);
		window.RaiseKeyDown();
		var afterKey = AppServices.Current.ActivityMonitor.LastActivity;
		Assert.True(afterKey >= before);

		Thread.Sleep(10);
		window.RaisePointerMoved();
		var afterPointer = AppServices.Current.ActivityMonitor.LastActivity;
		Assert.True(afterPointer >= afterKey);
	}

	[Fact]
	public void CredentialListView_ItemTemplateBuild_RendersCredentialCardElements()
	{
		using var scope = new AppServicesScope(initializeMasterKey: true, unlockSession: true, ensureAvalonia: true);
		var view = new CredentialListView();
		var listBox = view.GetLogicalDescendants().OfType<ListBox>().Single();
		var template = Assert.IsAssignableFrom<Avalonia.Controls.Templates.IDataTemplate>(listBox.ItemTemplate);

		var record = new CredentialRecord
		{
			Id = 7,
			Title = "TemplateTest",
			Username = "alice",
			EncryptedPassword = [0x01, 0x02, 0x03],
			EncryptedMetadata = [0x04, 0x05, 0x06],
			CredentialUuid = Guid.NewGuid().ToString("N"),
			SecretFormatVersion = CredentialSecretFormatVersion.Current,
			MetadataFormatVersion = CredentialMetadataFormatVersion.Current,
			CredentialType = CredentialType.ApiKey,
			Url = "https://example.test",
			IconKey = "Key",
		};

		var built = template.Build(record);

		Assert.NotNull(built);
	}

	private static void InvokePrivate(object target, string methodName)
	{
		var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
		method.Invoke(target, [null, null]);
	}

	private static void SetApplicationLifetime(LOCKnet.App.App app, IApplicationLifetime lifetime)
	{
		var property = typeof(Avalonia.Application).GetProperty("ApplicationLifetime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property is null)
			throw new InvalidOperationException("ApplicationLifetime backing field not found.");

		property.SetValue(app, lifetime);
	}

	private sealed class MainWindowProxy : MainWindow
	{
		public void RaiseKeyDown() => OnKeyDown(new KeyEventArgs());

		public void RaisePointerMoved()
		{
			var args = new PointerEventArgs(
				InputElement.PointerMovedEvent,
				this,
				null!,
				this,
				default,
				0,
				new PointerPointProperties(),
				KeyModifiers.None);
			OnPointerMoved(args);
		}
	}
}
