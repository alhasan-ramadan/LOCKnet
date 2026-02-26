# LOCKnet.App — Context

## Zweck

Die grafische Benutzeroberfläche. Einziger Einstiegspunkt für den normalen Nutzer. Zeigt Login, Credential-Liste, Bearbeitungsmasken. Ruft ausschließlich Core-Services auf — nie direkt Repositories.

## Aktueller Zustand

**Skelett.** Avalonia-Template-Code. Kein echtes Feature implementiert.

- `MainWindow.axaml` — zeigt nur `"Welcome to LOCKnet!"` per TextBlock
- `MainWindowViewModel.cs` — enthält nur den Greeting-String
- `Program.cs` — Avalonia-Startcode (funktioniert, aber enthält ungenutzte Data-Imports)
- `App.axaml.cs` — Standard Avalonia-Setup, DisableAvaloniaDataAnnotationValidation

## Dateistruktur (aktuell)

```
LOCKnet.App/
├── Program.cs                   — Avalonia-Entry-Point, [STAThread]
├── App.axaml / App.axaml.cs     — Avalonia Application-Klasse
├── ViewLocator.cs               — Avalonia ViewLocator (Standard-Template)
├── Views/
│   ├── MainWindow.axaml         — Leeres Hauptfenster (nur Greeting-TextBlock)
│   └── MainWindow.axaml.cs      — Code-Behind (leer)
├── ViewModels/
│   ├── ViewModelBase.cs         — Basisklasse (CommunityToolkit.Mvvm)
│   └── MainWindowViewModel.cs   — Nur Greeting-Eigenschaft
├── Models/                      — Leer
└── Assets/
    └── favicon.ico
```

## Geplante Views

| View | Zweck |
|------|-------|
| `LoginView` | Master-Key eingeben, erste Einrichtung erkennen |
| `MainView` / `CredentialListView` | Liste aller Credentials, Such-/Filterleiste |
| `CredentialDetailView` | Credential anlegen / bearbeiten |
| `SettingsView` | Lock-Timeout, Theme, Datenbankpfad |
| `LockScreenView` | Anzeige bei Auto-Lock nach 60s |

## MVVM-Muster

- ViewModels erben von `ViewModelBase` (CommunityToolkit.Mvvm)
- Views haben **kein** Code-Behind außer InitializeComponent
- Bindings über `{Binding}` in AXAML
- Commands als `[RelayCommand]`-Attribute auf ViewModels
- ViewModels kennen **nur** Core-Service-Interfaces — keine direkten Repository-Aufrufe

## Abhängigkeiten

- `LOCKnet.Core` (Projektreferenz)
- `LOCKnet.Data` (Projektreferenz)
- Avalonia UI (NuGet)
- CommunityToolkit.Mvvm (NuGet, für ViewModelBase)

## Wichtige Hinweise

- `Program.cs` enthält noch ungenutzte `using LOCKnet.Data.*` Imports — werden entfernt, sobald DI aufgebaut ist
- Die App soll als **Self-Contained Single-File EXE** publisht werden (USB-Stick-Portabilität)
- Kein Auto-Updater, kein Telemetrie, keine Netzwerkzugriffe
