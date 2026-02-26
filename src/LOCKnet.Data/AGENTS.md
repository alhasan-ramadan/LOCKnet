# LOCKnet.Data — Context

## Zweck

Datenbankschicht. Implementiert den Zugriff auf die SQLite-Datenbank und stellt die Repository-Interfaces aus `LOCKnet.Core` konkret bereit. Kennt SQL, kennt SQLite — kennt keine UI.

## Aktueller Zustand

**Vollständig implementiert.** Diese Schicht ist die einzige, die bereits echten Produktivcode enthält.

## Dateistruktur

```
LOCKnet.Data/
├── Database.cs                        — Initialisiert Tabellen beim ersten Start
├── Models/
│   ├── Credential.cs                  — DB-Modell: Id, Title, Username, EncryptedPassword, URL, Notes, Timestamps
│   ├── MasterKey.cs                   — DB-Modell: Id (immer 1), PasswordHash, Salt, Timestamps
│   └── Setting.cs                     — DB-Modell: Id, Key, Value, Timestamps
└── Repositories/
    ├── RepositoryBase.cs              — Abstrakte Basis: hält ConnectionString, öffnet SqliteConnection
    ├── CredentialsRepository.cs       — CRUD: Add, GetAll, Update, Remove(id)
    ├── MasterKeyRepository.cs         — Singleton-Semantik: Create, Get, Update, Delete
    └── SettingsRepository.cs          — Key-Value: Set (upsert), Get(key), GetAll, Remove(key)
```

## Wichtige Designentscheidungen

- **MasterKey ist ein Singleton** — `CHECK(Id = 1)` in der Tabelle, `Create()` wirft Exception wenn bereits vorhanden
- **EncryptedPassword** ist `byte[]` (BLOB) — niemals Klartext in der DB
- **Parameterbindung überall** — SQL-Injection ist strukturell ausgeschlossen
- **`RepositoryBase.GetConnection()`** öffnet immer eine neue Verbindung — kein Connection-Pooling
- **Timestamps** werden von SQLite gesetzt (`CURRENT_TIMESTAMP`), nicht von C#

## Ausstehende Aufgaben

- Repository-Interfaces aus `LOCKnet.Core.DataAbstractions` noch **nicht implementiert** — die Repositories erben aktuell nur von `RepositoryBase`, nicht von den Core-Interfaces
- `SettingsRepository.Set()` nutzt `ON CONFLICT(Key)` — setzt voraus, dass `Key` ein UNIQUE-Constraint hat (fehlt aktuell im Schema!)
- Kein `GetById` auf `CredentialsRepository`

## Abhängigkeiten

- `LOCKnet.Core` (Projektreferenz)
- `Microsoft.Data.Sqlite 10.0.0` (NuGet)

## Konventionen

- Alle Repositories erben von `RepositoryBase`
- Kein `string`-Interpolation in SQL — immer `$parameter` mit `AddWithValue`
- XML-Docs auf allen public Methoden (teilweise vorhanden)
- `using var` für alle Connections und Commands — keine manuellen `Dispose()`-Aufrufe
