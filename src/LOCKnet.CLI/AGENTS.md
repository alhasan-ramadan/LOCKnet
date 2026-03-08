# LOCKnet.CLI — Context

## Zweck

Optionale Kommandozeilen-Alternative zur GUI. Gleiche Core-Services, kein Avalonia. Nützlich für Scripting, Wartung, Server-Umgebungen oder schnellen Zugriff ohne Fenstermanager.

## Aktueller Zustand

**Leer.** Nur `Console.WriteLine("Hello, World!")` in `Program.cs`. Keine Implementierung vorhanden.

## Geplante Kommandostruktur

```
locknet unlock <master-key>     — Sitzung öffnen
locknet list                    — Alle Credentials auflisten
locknet get <id>                — Einzelnes Credential anzeigen
locknet add                     — Interaktiv neues Credential anlegen
locknet update <id>             — Credential bearbeiten
locknet remove <id>             — Credential löschen
locknet lock                    — Sitzung sperren / schließen
locknet init                    — Datenbank initialisieren + Master-Key setzen
```

## Designregeln

- Passwörter werden **niemals** als Argument übergeben (Command-History-Risiko) — immer `Console.ReadLine()` mit `ConsoleKey`-Masking
- CLI nutzt **dieselben Core-Services** wie App — kein eigener Krypto-Code
- Kein direkter Datenbankzugriff — nur über Core-Service-Interfaces
- Auto-Lock nach 60s gilt auch hier (via `IActivityMonitor`)

## Abhängigkeiten

- `LOCKnet.Core` (Projektreferenz)
- `LOCKnet.Data` (Projektreferenz)
- Evtl. `System.CommandLine` (NuGet) für Argument-Parsing

## Niedrige Priorität

Die CLI ist nice-to-have. Erst implementieren, wenn Core und App funktionieren.

## Git- und Release-Workflow (verbindlich)

- Bei wichtigen Updates mit mehreren neuen Features MUSS nach erfolgreichem Build/Test ein semantischer Release-Tag erstellt werden (z.B. `v1.1.3`).
- Wenn nach einem Release-Tag weitere relevante Feature- oder Fix-Aenderungen folgen, MUSS ein neuer passender Folgetag erstellt werden (`vX.Y.Z`) statt bestehende Tags zu ueberschreiben.
- Commits folgen dem bestehenden Projektstandard mit klaren, konsistenten Commit-Messages im Repo-Stil (z.B. `feat: ...`, `fix: ...`, `refactor: ...`).
- Nach finaler Verifikation (Build + relevante Tests gruen) werden Aenderungen committed und zum Remote gepusht; Release-Tags werden ebenfalls gepusht.
