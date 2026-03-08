using LOCKnet.Core.DataAbstractions;
using System.Collections.ObjectModel;

namespace LOCKnet.App.ViewModels;

/// <summary>
/// Statische Design-Time-Daten für den Avalonia-Previewer.
/// Wird ausschließlich über Design.DataContext referenziert — nie zur Laufzeit verwendet.
/// </summary>
public static class DesignData
{
	/// <summary>Sample-ViewModel für die Credential-Liste (Designer-Vorschau).</summary>
	public static CredentialListDesignViewModel CredentialList { get; } = new();
}

/// <summary>
/// Design-Time-Variante des CredentialListViewModel.
/// Initialisiert ohne AppServices — enthält nur statische Sample-Daten.
/// </summary>
public sealed class CredentialListDesignViewModel : ViewModelBase
{
	public ObservableCollection<CredentialRecord> Credentials { get; } =
	[
		new CredentialRecord
		{
			Id = 1,
			Title = "GitHub",
			Username = "Alhasan Ramadan",
			Url = "https://github.com/alhasan-ramadan/LOCKnet",
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		},
		new CredentialRecord
		{
			Id = 2,
			Title = "Beispiel-Eintrag",
			Username = "nutzer@example.com",
			Url = "https://example.com",
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
			CredentialType = CredentialType.ApiKey,
		},
	];

	public CredentialRecord? SelectedCredential { get; set; }
	public string SearchText { get; set; } = string.Empty;
	public string StatusMessage { get; set; } = string.Empty;
	public string LockTimerText { get; set; } = "⏱ 0:58";
}
