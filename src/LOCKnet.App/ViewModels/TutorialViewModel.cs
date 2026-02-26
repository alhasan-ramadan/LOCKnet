using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LOCKnet.App.ViewModels;

/// <summary>
/// ViewModel für die Tutorial-/Hilfe-Ansicht.
/// </summary>
public partial class TutorialViewModel : ViewModelBase
{
    public event EventHandler? Closed;

    [ObservableProperty]
    private int _currentStep;

    public TutorialStep[] Steps { get; } =
    [
        new(
            "🔒 Willkommen bei LOCKnet",
            "LOCKnet ist dein persönlicher, vollständig offline arbeitender Passwortmanager. " +
            "Alle Daten liegen verschlüsselt auf deinem USB-Stick — keine Cloud, keine externen Server.",
            "1 / 5"),
        new(
            "🔑 Master-Passwort",
            "Beim ersten Start richtest du ein Master-Passwort ein. " +
            "Dieses Passwort schützt alle deine Zugangsdaten. " +
            "Wähle ein starkes Passwort und bewahre es sicher auf — es gibt keine Wiederherstellung.",
            "2 / 5"),
        new(
            "➕ Neuen Eintrag anlegen",
            "Klicke auf \"+ Neu\" in der oberen Leiste, um einen neuen Eintrag zu erstellen. " +
            "Trage Titel, Benutzernamen, Passwort, URL und optionale Notizen ein. " +
            "Mit 🙈/👁️ kannst du das Passwort ein- und ausblenden.",
            "3 / 5"),
        new(
            "📋 Eintrag verwenden",
            "Klicke auf einen Eintrag in der Liste, um ihn auszuwählen. " +
            "Dann kannst du das Passwort mit \"📋 Passwort kopieren\" in die Zwischenablage kopieren. " +
            "Das Passwort wird nach 30 Sekunden automatisch aus der Zwischenablage gelöscht.",
            "4 / 5"),
        new(
            "🛡️ Sicherheitshinweise",
            "• LOCKnet sperrt sich nach 60 Sekunden Inaktivität automatisch.\n" +
            "• Klicke auf \"🔒 Sperren\" um die App sofort zu sperren.\n" +
            "• Trenne den USB-Stick niemals während des Betriebs.\n" +
            "• Erstelle regelmäßig eine Sicherungskopie des Sticks.",
            "5 / 5"),
    ];

    public TutorialStep CurrentStepData => Steps[CurrentStep];
    public bool CanGoBack => CurrentStep > 0;
    public bool CanGoForward => CurrentStep < Steps.Length - 1;
    public bool IsLastStep => CurrentStep == Steps.Length - 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
            OnPropertyChanged(nameof(CurrentStepData));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            OnPropertyChanged(nameof(IsLastStep));
            BackCommand.NotifyCanExecuteChanged();
            ForwardCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Forward()
    {
        if (CurrentStep < Steps.Length - 1)
        {
            CurrentStep++;
            OnPropertyChanged(nameof(CurrentStepData));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            OnPropertyChanged(nameof(IsLastStep));
            BackCommand.NotifyCanExecuteChanged();
            ForwardCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Ein einzelner Tutorial-Schritt.</summary>
/// <param name="Title">Überschrift des Schritts.</param>
/// <param name="Content">Erklärungstext.</param>
/// <param name="StepLabel">Fortschrittsanzeige z.B. "1 / 5".</param>
public record TutorialStep(string Title, string Content, string StepLabel);
