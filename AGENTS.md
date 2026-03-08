# LOCKnet — Root Context

## Was ist dieses Projekt?

**LOCKnet** (Local Offline Credential Keeper) ist ein vollständig offline arbeitender Passwortmanager für .NET 9, der direkt von einem USB-Stick gestartet wird. Keine Cloud, keine externen Abhängigkeiten, keine Installation nötig.

## Kernziel

Zugangsdaten (Credentials) sicher auf einem USB-Stick speichern und verwalten. Die SQLite-Datenbank ist vollständig verschlüsselt — ohne Master-Key ist sie wertlos.

## Solution-Struktur

```
LOCKnet.sln
├── src/
│   ├── LOCKnet.Core/      — Geschäftslogik, Kryptografie, Interfaces (KEINE Abhängigkeiten)
│   ├── LOCKnet.Data/      — SQLite-Datenbankzugriff, Repositories (referenziert Core)
│   ├── LOCKnet.App/       — Avalonia UI (referenziert Core + Data)
│   └── LOCKnet.CLI/       — Konsolen-Interface (referenziert Core + Data)
└── tests/
    ├── LOCKnet.Core.Tests/ — xUnit-Tests für Core
    └── LOCKnet.Data.Tests/ — xUnit-Tests für Data
```

## Abhängigkeitsregeln (KRITISCH)

```
Core  ← keine Abhängigkeiten
Data  ← Core
App   ← Core + Data
CLI   ← Core + Data
```

Core darf NIEMALS Data, App oder CLI referenzieren. Data darf NIEMALS App oder CLI referenzieren.

## Tech Stack

| Was | Womit |
|-----|-------|
| Sprache | C# 13, .NET 9 |
| UI | Avalonia UI (cross-platform) |
| Datenbank | SQLite via `Microsoft.Data.Sqlite` |
| Verschlüsselung | AES-256, Argon2/PBKDF2 (geplant) |
| Tests | xUnit |
| Container | Docker (Dockerfile vorhanden) |

## Aktueller Implementierungsstand

| Schicht | Status |
|---------|--------|
| LOCKnet.Data | **Implementiert** — Models, Repositories, Database-Initialisierung |
| LOCKnet.Core | **Leer** — nur `Class1.cs` Platzhalter, Architektur in `docs/` dokumentiert |
| LOCKnet.App | **Skelett** — Avalonia-Template, kein echter Inhalt |
| LOCKnet.CLI | **Leer** — nur `Console.WriteLine("Hello, World!")` |
| Tests | **Minimal** — 1 trivialer DB-Test, 1 leerer Platzhalter |

## Sicherheitsanforderungen (nicht verhandelbar)

- AES-256-Verschlüsselung für alle gespeicherten Passwörter
- Schlüsselableitung mit Argon2 oder PBKDF2 aus dem Master-Key
- SecureString-Handling: keine Klartextpasswörter im RAM
- Auto-Lock nach 60 Sekunden Inaktivität
- Keine unverschlüsselten Daten auf dem Stick

## Konventionen

- Nullable enabled — kein `!` ohne guten Grund
- Implicit usings enabled
- XML-Dokumentationskommentare auf allen public APIs
- Parameterbindung statt String-Interpolation in SQL (SQL-Injection-Schutz)
- Repositories erben von `RepositoryBase`

## Git- und Release-Workflow (verbindlich)

- Bei wichtigen Updates mit mehreren neuen Features MUSS nach erfolgreichem Build/Test ein semantischer Release-Tag erstellt werden (z.B. `v1.1.3`).
- Wenn nach einem Release-Tag weitere relevante Feature- oder Fix-Aenderungen folgen, MUSS ein neuer passender Folgetag erstellt werden (`vX.Y.Z`) statt bestehende Tags zu ueberschreiben.
- Commits folgen dem bestehenden Projektstandard mit klaren, konsistenten Commit-Messages im Repo-Stil (z.B. `feat: ...`, `fix: ...`, `refactor: ...`).
- Nach finaler Verifikation (Build + relevante Tests gruen) werden Aenderungen committed und zum Remote gepusht; Release-Tags werden ebenfalls gepusht.
