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
