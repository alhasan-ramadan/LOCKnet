# Architektur von LOCKnet

## Schichtenmodell
- App: GUI, zeigt Passwörter an
- Core: Logik, verschlüsselt Passwörter
- Data: Datenbankzugriff, SQLite/SQLCipher
- CLI: Terminal-Zugang, nutzt Core/Data
