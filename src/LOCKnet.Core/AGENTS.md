# LOCKnet.Core — Context

## Zweck

Das Herzstück der Anwendung. Enthält **ausschließlich** Interfaces, Domänenmodelle und Service-Logik — keine Datenbankdetails, keine UI-Abhängigkeiten. Alle anderen Projekte (Data, App, CLI) hängen von Core ab, aber nie umgekehrt.

## Aktueller Zustand

**LEER.** Nur `Class1.cs`-Platzhalter vorhanden. Die vollständige Architektur ist in `docs/LOCKnet_Core.md` dokumentiert und wartet auf Implementierung.

## Geplante Verzeichnisstruktur

```
LOCKnet.Core/
├── Crypto/
│   ├── IKeyDerivationService.cs   — Argon2/PBKDF2 Schlüsselableitung
│   ├── IEncryptionService.cs      — AES-256 Ver-/Entschlüsselung
│   └── ISecureStringService.cs    — SecureString-Kapselung
├── Security/
│   ├── IMasterKeyManager.cs       — Validierung + Sitzungsverwaltung
│   ├── ISessionManager.cs         — Auth-Status, Session-Schlüssel im RAM
│   ├── IActivityMonitor.cs        — Auto-Lock nach 60s Inaktivität
│   └── IIntegrityService.cs       — EXE-Hash-Prüfung gegen Tampering
├── Services/
│   ├── ICredentialService.cs      — CRUD + Ver-/Entschlüsselung von Credentials
│   └── ISettingsService.cs        — Key-Value-Einstellungen
├── DataAbstractions/
│   ├── ICredentialRepository.cs   — Interface, implementiert von Data
│   ├── IMasterKeyRepository.cs    — Interface, implementiert von Data
│   └── ISettingsRepository.cs     — Interface, implementiert von Data
└── Infrastructure/
    └── IRepositoryProvider.cs     — Zentraler DI-Zugangspunkt für Repos
```

## Kritische Regeln für dieses Projekt

1. **Keine Referenzen** auf LOCKnet.Data, LOCKnet.App, LOCKnet.CLI
2. **Keine** `Microsoft.Data.Sqlite` oder andere Datenbankpakete
3. Interfaces in `DataAbstractions/` definieren den Vertrag — Data liefert die Implementierungen
4. Alle public APIs brauchen XML-Dokumentationskommentare
5. `SecureString` statt `string` für Passwörter wo möglich
6. Kein Klartext-Passwort darf Core verlassen

## Was hier implementiert werden muss (Priorität)

1. `IKeyDerivationService` + Implementierung (Argon2 oder PBKDF2)
2. `IEncryptionService` + Implementierung (AES-256-CBC oder AES-256-GCM)
3. `ISecureStringService` + Implementierung
4. `IMasterKeyManager` + Implementierung
5. Repository-Interfaces (`DataAbstractions/`)
6. `ISessionManager` + `IActivityMonitor`
7. `ICredentialService` + Implementierung
8. `ISettingsService` + Implementierung

## Abhängigkeiten (NuGet)

Aktuell: keine. Geplant:
- `Konscious.Security.Cryptography.Argon2` (für Argon2id)
- oder alternativ: nur .NET-internes `Rfc2898DeriveBytes` für PBKDF2
