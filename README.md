# **LOCKnet – Projektbeschreibung**

[![CI](https://github.com/alhasan-ramadan/LOCKnet/actions/workflows/ci.yml/badge.svg)](https://github.com/alhasan-ramadan/LOCKnet/actions/workflows/ci.yml)
[![Release](https://github.com/alhasan-ramadan/LOCKnet/actions/workflows/release.yml/badge.svg)](https://github.com/alhasan-ramadan/LOCKnet/actions/workflows/release.yml)
[![Coverage](https://alhasan-ramadan.github.io/LOCKnet/coverage/badge_linecoverage.svg)](https://alhasan-ramadan.github.io/LOCKnet/coverage/)
[![Documentation](https://img.shields.io/badge/docs-doxygen-blue)](https://alhasan-ramadan.github.io/LOCKnet/doxygen/)


**LOCKnet** (Local Offline Credential Keeper) ist ein vollständig offline arbeitender Passwortmanager, der in **.NET 9** entwickelt wird. Die Anwendung soll direkt von einem **USB‑Stick** gestartet werden können und keinerlei externe Abhängigkeiten benötigen. Der Fokus liegt auf maximaler Sicherheit, portabler Nutzung und klarer Schichtentrennung.

## **Zielsetzung**

LOCKnet stellt eine sichere, portable Lösung zur Verwaltung von Zugangsdaten bereit. Die Zugangsdaten werden in einer **lokalen SQL-Datenbank** gespeichert, die vollständig verschlüsselt ist und ohne das Master‑Passwort nicht geöffnet oder ausgelesen werden kann.
Der Passwortmanager soll auf jedem Rechner über eine auf dem Stick liegende **Standalone‑EXE** gestartet werden können.

## **Projektstruktur (Solution)**

Die Solution besteht aus mehreren sauber getrennten Projekten:

1. **LOCKnet.App**
   Die grafische Benutzeroberfläche, entwickelt mit **Avalonia UI**.
   Startpunkt der Applikation (EXE auf dem Stick).

2. **LOCKnet.Core**
   Enthält die gesamte Geschäftslogik:

   - Verschlüsselung, Hashing, kryptografische Funktionen
   - Master‑Key‑Verarbeitung
   - SecureString‑Handling
   - Timeout‑Mechanismen
   - Domänenmodelle

3. **LOCKnet.Data**
   Verantwortlich für alle Datenbankzugriffe:

   - SQL‑Schicht
   - Zugriff auf die verschlüsselte Datenbank
   - Repositories für Benutzer, Credentials, Gruppen usw.
   - Verhindern von SQL‑Injection durch Parameterbindung

4. **LOCKnet.CLI**
   Optionale Kommandozeilenversion für Wartung/Skripte.

5. **LOCKnet.Core.Tests / LOCKnet.Data.Tests**
   Unit‑Tests mit **xUnit**.

## **Sicherheitsanforderungen**

- Vollständig **verschlüsselte SQL‑Datenbank** (z. B. AES‑256 in Kombination mit benutzerseitiger Schlüsselderivation).
- Zugriff nur nach erfolgreicher Eingabe des **Master‑Keys**.
- Nach **60 Sekunden Inaktivität** automatische Sperrung oder vollständiges Beenden der Anwendung.
- Keine Klartextpasswörter, weder in Dateien noch im RAM (so weit möglich).
- Einsatz sicherer Hashing‑Verfahren (z. B. Argon2, PBKDF2).
- Optionale Maßnahmen gegen **Tampering**, um Manipulationen an der EXE zu erschweren (obwohl vollständiger Schutz nicht möglich ist).
- Möglichkeit, den Stick zu klonen / kopieren, ohne Sicherheitsverlust.

## **Portabilität**

- Die Anwendung läuft direkt vom USB‑Stick, ohne Installation.
- Die zugehörige Datenbank liegt ebenfalls auf dem Stick.
- Der Nutzer kann ihre Inhalte jederzeit auf einen anderen Stick kopieren.

## **Funktionsweise**

1. USB‑Stick anschließen.
2. EXE ausführen → Avalonia‑UI startet.
3. Benutzer gibt Master‑Key ein.
4. LOCKnet entschlüsselt die Datenbank im Speicher und lädt die Einträge.
5. Bei 60 Sekunden Inaktivität → automatische Sperre oder Shutdown.
6. Daten bleiben niemals unverschlüsselt auf dem Stick.


## **Links**

| | Link |
|-|-|
| 📊 Coverage Report | <https://alhasan-ramadan.github.io/LOCKnet/coverage/> |
| 📖 API Documentation | <https://alhasan-ramadan.github.io/LOCKnet/doxygen/> |
| 🔍 Code Scanning | <https://github.com/alhasan-ramadan/LOCKnet/security/code-scanning> |

---

| Projekt          | Typ           | Wozu                                                                                                                           |
| ---------------- | ------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| **LOCKnet.Core** | Class Library | **Kernlogik**, z. B. wie Passwörter gespeichert, verschlüsselt und verwaltet werden. Keine UI, keine Datenbankdetails.         |
| **LOCKnet.Data** | Class Library | **Datenbankzugriff**, z. B. SQLite/SQLCipher. Speichert die Passwörter verschlüsselt auf dem USB-Stick. Kommuniziert mit Core. |
| **LOCKnet.App**  | WPF App       | **Grafische Benutzeroberfläche**. Alles, was der Benutzer sieht und klickt, läuft hier. Nutzt Core & Data.                     |
| **LOCKnet.CLI**  | Console App   | **Kommandozeilen-Interface**. Wie App, aber ohne Fenster – nur Terminal. Ebenfalls nutzt Core & Data.                          |
