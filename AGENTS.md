# FAM Pulverentnahme - Agent Instructions

Diese Datei ist fuer Codex und alle anderen Coding Agents verbindlich.

## Pflichtlektuere

Vor jeder Implementierung oder Aenderung muessen mindestens folgende Dateien gelesen werden:

1. `AGENTS.md`
2. `docs/PROJECT_CONTEXT.md`
3. `docs/ARCHITECTURE.md`
4. bei Buchungslogik zusaetzlich `docs/BOOKING_SCENARIOS.md`
5. bei Fehlerbehandlung zusaetzlich `docs/ERROR_HANDLING.md`
6. `docs/OPEN_POINTS.md`

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
- Jeder produktive Buchungsvorgang benoetigt eine eigene Transaktions-/Vorgangs-ID.
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
