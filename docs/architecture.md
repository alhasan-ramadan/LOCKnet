# Architektur von LOCKnet

## Schichtenmodell

- App: GUI, entsperrt die Vault, zeigt Statusmeldungen und arbeitet mit `StorageCompactionInfo`
- Core: Sicherheits- und Orchestrierungsschicht; validiert aktuelle Daten, steuert degradierten Unlock und Retry-Logik
- Data: SQLite-Zugriff, Rewrite-Aufbau, Artefakt-Recovery und dateibasierter Austausch der Vault-Datei
- CLI: Terminal-Zugang, nutzt dieselben Core-/Data-Flows wie die App

## Storage-Rewrite-Flow

1. Migration oder Plausibilitaetsverletzung setzt `RequiresStorageCompaction` im `VaultHeader`.
2. Beim Unlock darf die Vault weiter geoeffnet werden, wenn die kryptografische Entsperrung erfolgreich war. Cleanup kann dabei noch pending sein.
3. Core validiert vor einem neuen Rewrite alle aktuellen Credentials mit dem aktiven VaultKey.
4. Data baut per `VACUUM INTO` eine frische Datenbankdatei (`<db>.rewrite.tmp`) auf, validiert sie und ersetzt erst danach die Hauptdatei.
5. Nach erfolgreichem Austausch werden alte Artefakte entfernt; erst dann wird der Pending-Status geloescht.

## Startup-Recovery vor SQLite-Open

- `Database.Initialize()` startet die Artefakt-Recovery vor jedem `SqliteConnection.Open()`.
- Relevante Artefakte sind `<db>.rewrite.tmp` und `<db>.rewrite.bak`.
- Wenn die Hauptdatei fehlt oder ungueltig ist und das Backup verwendbar ist, wird das Backup wiederhergestellt.
- Wenn keine verwendbare Hauptdatei und kein Backup existieren, aber die Temp-Datei gueltig ist, wird die Temp-Datei zur neuen Hauptdatei promoviert.
- Wenn Hauptdatei und Backup gleichzeitig gueltig sind, wird nur noch die alte Backup-Datei entfernt. Der Pending-Status darf erst nach dieser erfolgreichen Finalisierung geloescht werden.

## Benutzerzustand

- Gesunder Unlock: `StorageCompactionInfo.IsPending == false`; normale Nutzung ohne Cleanup-Hinweis.
- Degradierter Unlock: VaultKey ist gueltig, aber `RequiresStorageCompaction` bleibt gesetzt; die UI soll die Statusmeldung anzeigen und die Vault trotzdem benutzbar lassen.
- Manueller Retry: nur sinnvoll bei entsperrter Session, weil die Vorpruefung den aktiven Session-Key benoetigt.
- Expliziter Fail: fehlgeformte aktuelle Rows, kaputte Envelopes oder eine nicht wiederherstellbare Rewrite-Unterbrechung bleiben pending und muessen fuer Backup-/Recovery-Entscheidungen sichtbar bleiben.

## Sicherheitsgrenzen

- Der Rewrite verbessert Storage-Hygiene deutlich gegenueber `VACUUM`-only, ist aber keine forensische Loeschgarantie.
- Flash-Wear-Leveling, Host-Caches und ein kompromittierter Host bleiben ausserhalb des Schutzmodells.
- Vollstaendige Datenbankverschluesselung auf Dateiebene ist weiterhin kein Bestandteil dieses Slices.
