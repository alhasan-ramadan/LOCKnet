# LOCKnet Docs — Context

## Zweck

Architektur- und Planungsdokumente. Kein ausführbarer Code.

## Dateien

| Datei | Inhalt | Qualität |
|-------|--------|----------|
| `architecture.md` | Kurzes Schichtenmodell (4 Zeilen) | Stub |
| `LOCKnet_Core.md` | Vollständige Core-Architektur mit Interfaces, Abhängigkeiten, Verzeichnisstruktur | **Vollständig** |
| `LOCKnet_Data.md` | Beschreibung der Data-Schicht | **Leer** (0 Bytes) |
| `prompt.md` | Ursprünglicher Projektbeschreibungs-Prompt | Referenz |
| `references.md` | Projektreferenzen-Übersicht | Vollständig |

## Bekannte Lücken

- `architecture.md` — zu knapp, sollte DI-Flow, Session-Lifecycle und Encrypt/Decrypt-Datenfluss zeigen
- `LOCKnet_Data.md` — leer, sollte Schema, Repository-Pattern, Upsert-Quirks dokumentieren
- Kein Dokument über den Sicherheitsflow (Master-Key-Setup, Session-Lifecycle, Auto-Lock)
- Kein Dokument über das Deployment (Publish-Einstellungen, USB-Stick-Layout)

## Hinweis für Agenten

Diese Dateien sind **Referenz, kein Source-of-Truth-Code**. Bei Widersprüchen zwischen Docs und implementiertem Code gilt der Code.
