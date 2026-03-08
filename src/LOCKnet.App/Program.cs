using Avalonia;
using System.IO;

namespace LOCKnet.App;

sealed class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		// Datenbank liegt neben der EXE (USB-Stick-Betrieb)
		var dbPath = Path.Combine(
			AppContext.BaseDirectory,
			"locknet.db");

		AppServices.Initialize(dbPath);
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	public static AppBuilder BuildAvaloniaApp()
	{
		var builder = AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.WithInterFont();

#if DEBUG
		builder = builder.LogToTrace();
#endif

		return builder;
	}
}
