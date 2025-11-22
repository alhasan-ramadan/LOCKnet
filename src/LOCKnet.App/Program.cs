using Avalonia;
using LOCKnet.Data.Models;
using LOCKnet.Data.Repositories;
using System;

namespace LOCKnet.App;

sealed class Program
{
	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
	// yet and stuff might break.
	[STAThread]
	public static void Main(string[] args)
	{
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		
		var database = new Data.Database();
		database.Initialize();
		
		var repo = new CredentialsRepository("Data Source=credentials.db");

		// Neuen Credential hinzufügen
		repo.Add(new Credential
		{
			Title = "E-Mail",
			Username = "user@example.com",
			EncryptedPassword = [],
			URL = "https://example.com",
			Notes = "Privat"
		});

		// Alle Credentials auslesen
		var allCredentials = repo.GetAll();
		foreach(var cred in allCredentials)
		{
			Console.WriteLine($"{cred.Title}: {cred.Username}");
		}

	}

// Avalonia configuration, don't remove; also used by visual designer.
	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.WithInterFont()
			.LogToTrace();
}
