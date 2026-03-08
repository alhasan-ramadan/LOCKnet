# **LOCKnet.Core**

Das Projekt **LOCKnet.Core** bildet die sicherheitskritische Geschäftslogik der Anwendung und stellt sämtliche Funktionen bereit, die zur Verarbeitung des Master-Keys, zur Ver- und Entschlüsselung sensibler Daten, zur Verwaltung sicherer Arbeitssitzungen und zur Kommunikation mit der Data-Schicht erforderlich sind. Core arbeitet vollständig unabhängig von der Benutzeroberfläche und stellt ausschließlich wohldefinierte Services und Schnittstellen bereit, die von LOCKnet.App und LOCKnet.CLI konsumiert werden.

Zu den Kernaufgaben gehören die Ableitung kryptografischer Schlüssel aus dem Master-Passwort (Argon2 oder PBKDF2), die Verwaltung eines internen AES-256-Schlüssels, die sichere Entschlüsselung und Verschlüsselung aller Credential-Passwörter, der Schutz des Master-Passworts über SecureString-Verarbeitung sowie die Verwaltung eines Sitzungsstatus, der nach einem Inaktivitätszeitraum automatisch abläuft. Darüber hinaus integriert Core Mechanismen zur Integritätsprüfung der Programmdateien, stellt die zentrale Credential-Verwaltung bereit, orchestriert alle Datenzugriffe über Repository-Interfaces und kapselt sämtliche sensiblen Operationen in wohldefinierten Services.
Core fungiert damit als zentrale Sicherheits- und Logikschicht, die vollständig UI-unabhängig ist und garantiert, dass keine sicherheitsrelevanten Daten unverschlüsselt den Anwendungskern verlassen.

---

# **Struktur von LOCKnet.Core (nur Architektur)**

## **1. Kryptografische Services**

### **1.1 IKeyDerivationService**

* Verantwortlich fuer die Ableitung des KEK aus dem Master-Passwort.
* Unterstützt Argon2 oder PBKDF2.
* Fuehrt Salt-Generierung, Parameter-Persistenz und Validierung durch.

### **1.2 IEncryptionService**

* Führt AES-256-Verschlüsselung und -Entschlüsselung aus.
* Nutzt den vom KeyDerivationService gelieferten Schlüssel.
* Verantwortlich für IV-Handhabung, Padding, sichere Rückgabe.

### **1.3 ISecureStringService**

* Kapselt den Umgang mit SecureString.
* Bereitstellung sicherer Konvertierungen in Bytearrays.
* Löscht Speicherbereiche nach Nutzung.

---

## **2. Master-Key-Management**

### **IMasterKeyManager**

* Leitet aus dem Master-Passwort einen KEK ab.
* Entpackt bzw. verpackt damit den persistierten VaultKey.
* Speichert und aktualisiert den VaultHeader in der Data-Schicht.
* Verwaltet die Entsperrung und Sperrung der Sitzung.

### Verantwortliche Klassen:

* **MasterKeyManager** (Implementierung)
* nutzt:

  * IKeyDerivationService
  * IEncryptionService
  * MasterKeyRepository (ueber Interface, speichert `VaultHeader`)

---

## **3. Credential-Management**

### **ICredentialService**

* Vergibt IDs, erstellt, aktualisiert und löscht Credentials.
* Verschlüsselt Passwörter beim Speichern.
* Entschlüsselt Passwörter nur auf expliziten Aufruf.
* Validiert Eingaben (Titel, URL, Pflichtfelder).
* Orchestriert Kommunikation mit CredentialsRepository.

### Verantwortliche Klassen:

* **CredentialService**
* verwendet:

  * IEncryptionService
  * IKeyDerivationService
  * IActivityMonitor
  * ICredentialRepository

---

## **4. Einstellungen und Konfiguration**

### **ISettingsService**

* Zugriff auf generische Key-Value-Settings.
* Kapselt Lesen/Schreiben gegenüber SettingsRepository.
* Prüft auf existierende Schlüssel.
* Verwaltet Startparameter, UI-Einstellungen, Lock-Timeout etc.

---

## **5. Sicherheits- und Session-Mechanismen**

### **5.1 ISessionManager**

* Verwalten des Authentifizierungsstatus der Anwendung.
* Speichert den entschluesselten VaultKey im Speicher.
* Entfernt Schlüssel bei Sperre oder Timeout.
* Gibt nur defensive Kopien des Session-Schluessels aus.
* Gewaehrleistet, dass nur im entsperrten Zustand auf Daten zugegriffen wird.

### **5.2 IActivityMonitor**

* Verfolgt Benutzeraktivität.
* Löst automatische Sperre nach 60 Sekunden aus.
* Informiert SessionManager bei Timeout.

---

## **6. Integritätsschutz**

### **IIntegrityService**

* Prüft Hash der EXE-Dateien.
* Vergleicht mit im Repository hinterlegten Hashwerten.
* Schutz vor Manipulation der Anwendung auf dem USB-Stick.
* Meldet Auffälligkeiten an App/UI.

---

## **7. Orchestrierung und Abstraktion gegenüber LOCKnet.Data**

### **IRepositoryProvider**

* Zentraler Zugangspunkt für alle Repository-Interfaces.
* Verhindert, dass Core von konkreten Implementierungen der Data-Schicht abhängig ist.

### Repositories in Core (Interfaces):

* **ICredentialRepository**
* **IMasterKeyRepository**
* **ISettingsRepository**

Diese werden von LOCKnet.Data bereitgestellt und über Dependency Injection in Core injiziert.

---

# **Gesamtarchitektur von LOCKnet.Core**

```
LOCKnet.Core
│
├── Crypto
│   ├── IKeyDerivationService
│   ├── IEncryptionService
│   └── ISecureStringService
│
├── Security
│   ├── IMasterKeyManager
│   ├── ISessionManager
│   ├── IActivityMonitor
│   └── IIntegrityService
│
├── Services
│   ├── ICredentialService
│   └── ISettingsService
│
├── DataAbstractions
│   ├── ICredentialRepository
│   ├── IMasterKeyRepository
│   └── ISettingsRepository
│
└── Infrastructure
    └── IRepositoryProvider
```

