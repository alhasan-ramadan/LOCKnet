using Avalonia;
using LOCKnet.Data;
using LOCKnet.Data.Models;
using LOCKnet.Data.Repositories;
using System;
using System.Linq;

namespace LOCKnet.App;

sealed class Program
{
	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
	// yet and stuff might break.
	[STAThread]
	public static void Main(string[] args)
	{
		// Avalonia starten
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

// Avalonia configuration, don't remove; also used by visual designer.
	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.WithInterFont()
			.LogToTrace();
}
