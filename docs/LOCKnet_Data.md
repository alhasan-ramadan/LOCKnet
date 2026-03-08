# **LOCKnet.Data**

Die Data-Schicht ist fuer den tatsaechlichen SQLite-Rewrite, die Dateiartefakte und die Wiederherstellung nach Unterbrechungen verantwortlich. Seit dem Storage-Hardening-Slice verwendet LOCKnet keinen reinen `VACUUM`-only-Cleanup mehr, sondern einen Fresh-File-Rewrite mit expliziter Recovery-Logik.

---

## **1. Warum Fresh-File-Rewrite statt nur `VACUUM`**

* Fruehere Klartext-scrubbende Migrationen konnten historische SQLite-Seiten im alten Dateilayout zuruecklassen, selbst wenn die aktuellen Rows bereits auf Envelope-Formate umgestellt waren.
* Der neue Cleanup-Pfad erstellt deshalb zuerst eine neue Datenbankdatei per `VACUUM INTO` im selben Verzeichnis und ersetzt die Hauptdatei erst, wenn die Rewrite-Kopie verwendbar ist.
* Das verbessert die Storage-Hygiene vor allem auf USB- und Flash-Medien, weil die aktive Vault-Datei aus dem aktuellen logischen Datenbestand neu aufgebaut wird.
* Diese Massnahme ist bewusst als Hygiene-Verbesserung dokumentiert, nicht als forensische Loeschung. Wear-Leveling, Betriebssystem-Caches und bereits kompromittierte Hosts bleiben Restrisiken.

---

## **2. Relevante Komponenten**

### **2.1 Database**

* `Database.Initialize()` ruft `StorageRewriteArtifacts.Recover(_databasePath)` auf, bevor irgendeine SQLite-Verbindung geoeffnet wird.
* Wenn die Recovery nur noch eine erfolgreiche Backup-Finalisierung abschliessen musste, loescht `StorageRewriteArtifacts.ClearPendingState(connection)` danach den Pending-Status im `MasterKey`-Datensatz.

### **2.2 RepositoryBase**

* `RepositoryBase` und `Database` loesen aus dem Connection-String einen echten Dateipfad auf.
* Nur dateibasierte SQLite-Vaults koennen den Rewrite-Pfad nutzen. In-Memory-Verbindungen liefern absichtlich einen expliziten Pending-Fehler statt eines stillen No-ops.

### **2.3 VaultMigrationRepository**

* `ApplyMigration(...)` schreibt migrierte Credentials und den `VaultHeader` weiterhin atomar in einer exklusiven SQLite-Transaktion.
* `CompactStorage()` finalisiert zunaechst vorhandene Rewrite-Artefakte, baut dann den Rewrite-Kandidaten, validiert ihn und ersetzt erst danach die Hauptdatei.
* `HasPendingStorageArtifacts()` ermoeglicht Core, Artefakt-Finalisierung von echter Vorpruefung zu unterscheiden.

### **2.4 StorageRewriteArtifacts**

* Verwaltet die stabilen Artefakt-Namen `<db>.rewrite.tmp` und `<db>.rewrite.bak`.
* Kapselt Recovery, Dateiaustausch, Datei-Promotion, Backup-Wiederherstellung und Retry-Loops fuer Dateisperren.
* Nutzt `SqliteConnection.ClearAllPools()` plus kurze Retry-Schleifen, damit Windows-Handles nach SQLite-Operationen den Austausch nicht blockieren.

---

## **3. Rewrite-Cleanup-Verhalten**

### **3.1 Wann der Rewrite ausgeloest wird**

* Rewrite-Cleanup laeuft nur, wenn `VaultHeader.RequiresStorageCompaction` gesetzt ist.
* Typischer Ausloeser ist eine Migration, die Legacy-Secrets oder Klartext-Metadaten auf aktuelle Envelope-Formate umstellt.
* Beim Unlock versucht Core die Bereinigung automatisch, ausser eine frische Fehlerspur erzwingt noch die 10-Minuten-Backoff-Zeit.
* Ein manueller Retry ist ueber `RetryPendingStorageCompaction()` moeglich, aber nur bei entsperrter Session.

### **3.2 Was in die neu aufgebaute DB gelangt**

* Der Rewrite verwendet `VACUUM INTO` und kopiert damit den aktuellen logischen SQLite-Zustand in eine neue Datei.
* Vorher stellt Core sicher, dass der Header aktuell ist, alle aktuellen Credentials gueltige UUIDs besitzen, Secret- und Metadaten-Envelopes vorhanden sind und sich mit dem aktiven VaultKey erfolgreich authentifizieren lassen.
* Dadurch wird nur der bereits migrierte, aktuelle Datenbestand in die frische Datei uebernommen.

### **3.3 Was bewusst nicht weitergetragen werden darf**

* Klartext-Metadatenreste in aktuellen Rows duerfen nicht stillschweigend mitgeschrieben werden. Sobald Core solche Residuen findet, wird der Rewrite als Korruptionsfall abgebrochen.
* Legacy-Secret- oder Legacy-Metadaten-Formate in vermeintlich aktuellen Rows werden ebenfalls nicht "best effort" kopiert.
* Alte freie Seiten, die nur im bisherigen Dateilayout existierten, gehoeren nicht zum Ziel des frischen SQLite-Files.

### **3.4 Fehlgeformte aktuelle Rows**

* Fehlende Envelopes, ungueltige UUIDs, JSON-/Authentifizierungsfehler oder persistierte Klartext-Metadaten werden als schwerer Datenzustand behandelt.
* In diesem Fall bleibt `RequiresStorageCompaction` gesetzt, `LastStorageCompactionFailureKind` wird auf `Corruption` gesetzt und die bestehende Vault-Datei wird nicht ersetzt.

### **3.5 Wann der Pending-Status geloescht wird**

* `RequiresStorageCompaction` darf erst geloescht werden, wenn `CompactStorage()` echten Erfolg meldet oder der Startup-Pfad eine bereits ersetzte Hauptdatei finalisiert und danach das verbliebene Backup erfolgreich entfernt.
* Ein erfolgreicher Temp-Build allein reicht nicht aus.
* Ein erfolgreicher Dateiaustausch ohne Abschluss der Artefakt-Bereinigung reicht ebenfalls nicht aus.

---

## **4. Startup-Artefakt-Recovery**

### **4.1 Artefakte**

* Temp-Datei: `<db>.rewrite.tmp`
* Backup-Datei: `<db>.rewrite.bak`

### **4.2 Recovery-Regeln**

* Recovery laeuft vor `SqliteConnection.Open()`, damit SQLite nicht versehentlich eine neue leere Hauptdatei erzeugt oder eine unvollstaendige Rewrite-Situation ueberdeckt.
* Wenn die Hauptdatei fehlt oder ungueltig ist und das Backup gueltig ist, wird das Backup wiederhergestellt.
* Wenn weder eine gueltige Hauptdatei noch ein Backup existieren, aber die Temp-Datei gueltig ist, wird die Temp-Datei zur neuen Hauptdatei promoviert.
* Wenn Hauptdatei und Backup gleichzeitig gueltig sind, ist der kritische Austausch bereits vorbei; beim naechsten Start wird nur noch das Backup geloescht und anschliessend der Pending-Status bereinigt.
* Wenn die Hauptdatei gueltig ist und nur noch eine Temp-Datei uebrig ist, wird diese best effort geloescht; blockiert das Loeschen, bleibt der Pending-Zustand bestehen.

### **4.3 Unterbrechungs- und Crash-Punkte**

* **Vor vollstaendigem Temp-Build:** Die Hauptdatei bleibt unberuehrt. Eine unbrauchbare Temp-Datei wird bei spaeterem Start entfernt, solange die Hauptdatei gueltig ist.
* **Nach Temp-Build, vor Austausch:** Es kann eine gueltige Temp-Datei neben einer gueltigen Hauptdatei liegen. Startup entfernt die Temp-Datei oder promotet sie nur dann, wenn die Hauptdatei fehlt und kein Backup existiert.
* **Waerend des Austauschs:** Auf Windows kann `.rewrite.bak` als Rueckfallartefakt entstehen. Fehlt oder versagt die Hauptdatei danach, versucht Startup zuerst die Wiederherstellung aus dem Backup.
* **Nach Austausch, vor abschliessender Artefakt-Loeschung:** Die neue Hauptdatei kann bereits gueltig sein, waehrend `.rewrite.bak` noch existiert. In diesem Zustand bleibt Cleanup pending, bis das Backup wirklich entfernt wurde.

### **4.4 Wann Retry vs. Fehler gilt**

* Busy/Locked, zu wenig Platz, I/O und Unknown gelten als retry-faehige Fehler mit pending State.
* Korruption steht fuer ungueltige SQLite-Dateien oder inkonsistente aktuelle Rows und verlangt explizite Diagnose statt stiller Wiederholung.
* Wenn Recovery keine verwendbare Hauptdatenbank mehr herstellen kann, bricht der Start mit einer klaren Ausnahme ab, statt Artefakte still zu verwerfen.

---

## **5. Header-, Status- und Retry-Modell**

### **5.1 Persistierte Felder**

* `RequiresStorageCompaction`
* `LastStorageCompactionAttemptUtc`
* `LastStorageCompactionFailureKind`
* `LastStorageCompactionError`

### **5.2 Zustandsuebergaenge**

1. Migration oder Validierungsproblem setzt `RequiresStorageCompaction = true`.
2. Unlock kann trotzdem erfolgreich sein und liefert degradierten Status.
3. Ein Rewrite-Versuch aktualisiert Attempt/Failure-Felder immer explizit.
4. Erst bei echtem Erfolg werden Pending-Flag und Fehlerfelder geloescht.
5. Password-Change traegt die Felder unveraendert weiter, bis der Rewrite abgeschlossen ist.

### **5.3 Failure-Klassen**

* `BusyOrLocked` - Datei oder Artefakt ist noch gesperrt.
* `InsufficientSpace` - freier Speicher reicht fuer den Rewrite nicht aus.
* `Io` - Dateisystem-/USB-I/O verhindert sicheren Rewrite oder Austausch.
* `Corruption` - ungueltige SQLite-Datei oder inkonsistenter aktueller Datenbestand.
* `Unknown` - nicht eindeutig zuordenbarer Fehler, inklusive nicht-dateibasierter Test-/In-Memory-Verbindungen.

---

## **6. Invarianten fuer kuenftige Aenderungen**

* Recovery muss vor jedem SQLite-Open auf die Hauptdatei laufen.
* Pending-State darf nie vor echtem Rewrite-Erfolg oder erfolgreich abgeschlossener Backup-Finalisierung geloescht werden.
* Fehlgeformte aktuelle Rows duerfen nicht stillschweigend in den Rewrite eingehen.
* Core-Vorpruefung mit aktivem VaultKey muss bestehen bleiben, solange keine reine Artefakt-Finalisierung vorliegt.
* Die alte Hauptdatei darf nicht still verworfen werden, bevor die neue Datei als verwendbar verifiziert wurde.
* Die Artefakt-Suffixe `.rewrite.tmp` und `.rewrite.bak` sind Teil des operativen Recovery-Modells; Aenderungen daran erfordern Migrations- und Recovery-Planung.

---

## **7. Testabdeckung fuer diesen Slice**

### **7.1 Core**

* `tests/LOCKnet.Core.Tests/Security/MasterKeyManagerTests.cs`
* Deckt wiederholte Retry-Fehler, Session-gesteuerten manuellen Retry, Pending-State-Erhalt ueber Password-Change und stabile `GetStorageCompactionInfo()`-Abfragen ab.

### **7.2 Data**

* `tests/LOCKnet.Data.Tests/Repositories/StorageRewriteIntegrationTests.cs`
* Deckt erfolgreichen Rewrite mit Pending-Clear, Korruptionsabbruch bei fehlerhafter Current-Row, Unterbrechung vor Austausch, Unterbrechung nach Temp-Erzeugung, Backup-Wiederherstellung, Backup-Finalisierung und Temp-Promotion ab.
* `tests/LOCKnet.Data.Tests/Repositories/VaultMigrationRepositoryTests.cs`
* Deckt den expliziten Pending-Fehler fuer nicht-dateibasierte/In-Memory-Verbindungen ab.

---

## **8. Restrisiken und Nicht-Ziele**

* Flash-Wear-Leveling kann alte physische Bloecke trotz logisch erfolgreichem Rewrite weiter auf dem Medium belassen.
* Vollstaendige Dateiverschluesselung der gesamten SQLite-DB ist in diesem Slice weiterhin nicht implementiert.
* Ein kompromittierter Host kann weiterhin Klartext waehrend Laufzeit, im RAM oder ueber System-Caches beobachten.
* Das Rewrite-Modell verbessert Storage-Hygiene deutlich, macht sie aber nicht perfekt.
