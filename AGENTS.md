# FAM Pulverentnahme - Agent Instructions

Diese Datei ist fuer Codex und alle anderen Coding Agents verbindlich.

## Repository-first: Pflicht vor jeder Arbeit

Dieses Repository ist die primaere und dauerhafte Wissensquelle fuer das Projekt.

Bevor du eine fachliche Antwort gibst, Code aenderst, Architektur vorschlaegst oder eine neue Aufgabe beginnst:

1. Ermittle zuerst den aktuellen Stand im Repository.
2. Lies `AGENTS.md` und die fuer die Aufgabe relevanten Dateien unter `docs/`.
3. Suche zusaetzlich im Repository nach den konkreten Begriffen, Funktionen, Oxaion-Programmen, Fehlermeldungen, Tabellen, Endpunkten oder Prozessnamen, die fuer die Aufgabe relevant sind.
4. Wenn der aktuelle Implementierungsstand wichtig ist, pruefe auch den vorhandenen Code sowie relevante aktuelle Commits, Pull Requests oder Issues.
5. Verwende bereits dokumentierte Entscheidungen als Ausgangspunkt und frage nicht erneut nach Informationen, die im Repository eindeutig beantwortet sind.
6. Bei Widerspruechen gilt: aktuelle, explizit als entschieden dokumentierte Projektinformation hat Vorrang vor aelteren Annahmen. Widersprueche nicht stillschweigend aufloesen, sondern sichtbar machen.
7. Fehlt eine Information, pruefe zuerst `docs/OPEN_POINTS.md`. Ist sie dort offen, darf sie nicht erfunden werden.
8. Nach einer neuen verbindlichen fachlichen oder technischen Entscheidung aktualisiere die passende Dokumentation im selben Arbeitsschritt.

Wichtig: Nicht nur Dateinamen lesen oder aus Erinnerung arbeiten. Den tatsaechlichen Inhalt der relevanten Dateien und den aktuellen Codebestand pruefen.

## Pflichtlektuere

Vor jeder Implementierung oder Aenderung muessen mindestens folgende Dateien gelesen werden:

1. `AGENTS.md`
2. `docs/PROJECT_CONTEXT.md`
3. `docs/ARCHITECTURE.md`
4. bei Buchungslogik zusaetzlich `docs/BOOKING_SCENARIOS.md`
5. bei Fehlerbehandlung zusaetzlich `docs/ERROR_HANDLING.md`
6. bei PWA, Offline-Betrieb, lokalem Cache, Outbox, Synchronisation oder App-Updates zusaetzlich `docs/OFFLINE_PWA.md`
7. `docs/OPEN_POINTS.md`

## Quellenprioritaet

Bei der Ermittlung des aktuellen Projektstands gilt grundsaetzlich folgende Reihenfolge:

1. aktuell vorhandener Code und Tests fuer den technischen Ist-Stand
2. `docs/PROJECT_CONTEXT.md` fuer verbindliche fachliche Entscheidungen
3. spezialisierte Dokumente wie `ARCHITECTURE.md`, `BOOKING_SCENARIOS.md`, `ERROR_HANDLING.md` und `OFFLINE_PWA.md`
4. `docs/OPEN_POINTS.md` fuer bewusst noch nicht entschiedene Themen
5. README, Issues, Pull Requests und Commit-Historie als ergaenzender Kontext

Wenn Code und Dokumentation voneinander abweichen, nicht automatisch einen der beiden Staende als richtig annehmen. Die Abweichung benennen und anhand der juengsten expliziten Entscheidung beziehungsweise des Projektziels klaeren.

## Grundregeln

- Die fachliche Wahrheit liegt in `docs/PROJECT_CONTEXT.md`.
- Bereits getroffene Entscheidungen duerfen nicht stillschweigend geaendert werden.
- Neue fachliche Entscheidungen muessen anschliessend in den Dokumentationsdateien ergaenzt werden.
- Keine direkte Manipulation von Oxaion-Datenbanktabellen.
- Buchungen muessen ueber freigegebene Oxaion-Logik beziehungsweise Oxaion HTTP-Schnittstellen erfolgen.
- Unbekannte Oxaion-Programme, Endpunkte, Parameter, Tabellenlogik oder Buchungsschluessel niemals erfinden.
- Unsichere Annahmen mit `TODO` kennzeichnen und rueckfragen.
- Buchungsprozesse muessen transaktionssicher und nachvollziehbar gestaltet werden.
- Bei unklarem Ausgang einer Oxaion-Buchung darf niemals automatisch angenommen werden, dass die Buchung fehlgeschlagen ist.
- Doppelbuchungen muessen technisch verhindert werden.
- Jeder lokal angelegte Offline-Vorgang benoetigt eine eindeutige `clientOperationId`.
- Jeder serverseitig angenommene produktive Buchungsvorgang benoetigt eine eigene Transaktions-/Vorgangs-ID.
- Wiederholte Uebertragung derselben `clientOperationId` muss serverseitig idempotent behandelt werden.
- Ein offline erfasster Vorgang darf niemals wie eine erfolgreich bestaetigte Oxaion-Buchung dargestellt werden.
- Nach Reconnect muss vor produktiver Buchung eine serverseitige Revalidierung des fachlichen Zustands erfolgen.
- Benutzer muessen bei Fehlern eine verstaendliche Meldung und eine konkrete Massnahme erhalten.
- Das Frontend darf keine Oxaion-Zugangsdaten enthalten.
- Zugangsdaten und Secrets gehoeren niemals in Git.
- Das Backend ist die zentrale Vermittlungs- und Kontrollschicht zwischen WebApp und Oxaion.
- Aenderungen klein, nachvollziehbar und testbar durchfuehren.
- Keine grundlegenden Architekturaenderungen ohne vorherige Abstimmung.

## Technologie

Geplanter Stack:

### Frontend

- HTML
- CSS
- JavaScript
- PWA mit Web App Manifest und Service Worker
- `IndexedDB` fuer lokale fachliche Zwischenspeicherung und Outbox
- mobile Nutzung auf Android
- QR-/Barcode-Scanner ueber die Smartphone-Kamera

### Backend

- ASP.NET Core
- C#
- REST API
- Hosting ueber IIS

### ERP

- Oxaion
- Kommunikation ueber die Oxaion HTTP-Schnittstelle
- wenn moeglich Nutzung der vorhandenen Oxaion BDE-/PPS-Fachlogik

### Datenbank

- keine direkten ERP-Buchungen per SQL
- eventuell eigene SQL-Datenbank beziehungsweise Tabellen ausschliesslich fuer WebApp-Protokollierung, Transaktionen und Status
