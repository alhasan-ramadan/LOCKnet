# LOCKnet
Local Offline Credential Keeper

| Projekt          | Typ           | Wozu                                                                                                                           |
| ---------------- | ------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| **LOCKnet.Core** | Class Library | **Kernlogik**, z. B. wie Passwörter gespeichert, verschlüsselt und verwaltet werden. Keine UI, keine Datenbankdetails.         |
| **LOCKnet.Data** | Class Library | **Datenbankzugriff**, z. B. SQLite/SQLCipher. Speichert die Passwörter verschlüsselt auf dem USB-Stick. Kommuniziert mit Core. |
| **LOCKnet.App**  | WPF App       | **Grafische Benutzeroberfläche**. Alles, was der Benutzer sieht und klickt, läuft hier. Nutzt Core & Data.                     |
| **LOCKnet.CLI**  | Console App   | **Kommandozeilen-Interface**. Wie App, aber ohne Fenster – nur Terminal. Ebenfalls nutzt Core & Data.                          |
