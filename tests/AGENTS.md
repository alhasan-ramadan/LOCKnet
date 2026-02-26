# LOCKnet Tests — Context

## Zweck

xUnit-Tests für Core-Logik und Data-Schicht. Sicherheitskritischer Code (Krypto, Key-Derivation) muss gut getestet sein.

## Aktueller Zustand

**Minimal.** Fast kein sinnvoller Test vorhanden.

| Datei | Inhalt |
|-------|--------|
| `LOCKnet.Core.Tests/UnitTest1.cs` | Leerer `[Fact]` ohne Assertions |
| `LOCKnet.Data.Tests/DatabaseTests.cs` | Prüft nur `db != null` nach `Initialize()` |
| `LOCKnet.Data.Tests/UnitTest1.cs` | Leer |

## Projektstruktur

```
tests/
├── LOCKnet.Core.Tests/     — referenziert LOCKnet.Core
│   └── UnitTest1.cs        — Platzhalter
└── LOCKnet.Data.Tests/     — referenziert LOCKnet.Data
    ├── DatabaseTests.cs    — 1 trivialer Test
    └── UnitTest1.cs        — Platzhalter
```

## Was getestet werden muss (nach Priorität)

### LOCKnet.Core.Tests
1. **KeyDerivationService** — gleicher Input liefert gleichen Hash, verschiedener Salt → verschiedenes Ergebnis
2. **EncryptionService** — Encrypt → Decrypt Round-trip, Tamper-Erkennung (AES-GCM)
3. **MasterKeyManager** — korrekter Key entsperrt, falscher Key schlägt fehl
4. **ActivityMonitor** — Timeout löst Lock aus
5. **CredentialService** — Passwort wird verschlüsselt gespeichert, nie im Klartext zurückgegeben

### LOCKnet.Data.Tests
1. **CredentialsRepository** — Add/GetAll/Update/Remove funktioniert auf In-Memory-DB
2. **MasterKeyRepository** — zweiter `Create()`-Call wirft Exception
3. **SettingsRepository** — `Set()` macht echten Upsert
4. **Database.Initialize()** — idempotent (zweimaliger Aufruf kein Fehler)

## Test-Konventionen

- In-Memory SQLite für DB-Tests: `"Data Source=:memory:"` — kein Schreiben auf Disk
- Arrange-Act-Assert Struktur
- Keine statischen Felder zwischen Tests (kein shared state)
- xUnit `IDisposable` für Setup/Teardown von DB-Verbindungen
- Test-Dateinamen: `{KlassenName}Tests.cs`

## Abhängigkeiten

- `xunit` + `xunit.runner.visualstudio`
- `Microsoft.NET.Test.Sdk`
- Für Mocking (sobald benötigt): `Moq` oder `NSubstitute`
