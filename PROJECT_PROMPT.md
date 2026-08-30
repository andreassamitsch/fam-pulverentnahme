# Projekt-Prompt: FAM Pulverentnahme

Verwende diesen Prompt als Richtlinie fuer ChatGPT, Codex oder andere Coding Agents, die an diesem Repository arbeiten.

---

Du arbeitest am Projekt **FAM Pulverentnahme / Pulverwechsel** im GitHub-Repository:

`andreassamitsch/fam-pulverentnahme`

## Oberste Regel: Repository zuerst

Behandle das Repository als primaere, dauerhafte Wissensquelle des Projekts.

Bevor du eine fachliche Aussage triffst, Code schreibst, eine Architektur aenderst, einen Fehler analysierst oder einen neuen Umsetzungsvorschlag machst, musst du zuerst den aktuellen Stand im Repository ermitteln.

Arbeite nicht nur aus Chat-Verlauf, Modellgedaechtnis oder Annahmen.

## Vorgehen bei jeder neuen Aufgabe

1. Lies zuerst `AGENTS.md`.
2. Lies danach mindestens:
   - `docs/PROJECT_CONTEXT.md`
   - `docs/ARCHITECTURE.md`
   - `docs/OPEN_POINTS.md`
3. Bei Buchungslogik lies zusaetzlich:
   - `docs/BOOKING_SCENARIOS.md`
4. Bei Fehlern, Netzwerkausfall, Retry, Idempotenz, Oxaion-Sperren oder unklarem Buchungsstatus lies zusaetzlich:
   - `docs/ERROR_HANDLING.md`
5. Suche anschliessend im gesamten Repository gezielt nach Begriffen aus der aktuellen Aufgabe, zum Beispiel:
   - Funktions- oder Klassennamen
   - Oxaion-Programme
   - Tabellen- oder Feldnamen
   - HTTP-Endpunkte
   - Buchungsschluessel
   - Fehlermeldungen
   - Maschinen-IDs
   - QR-Code-Formate
   - Transaktionsstatus
   - Prozessnamen
6. Wenn die Aufgabe vom aktuellen Implementierungsstand abhaengt, pruefe den vorhandenen Code und die Tests. Wenn sinnvoll, beruecksichtige auch aktuelle Commits, Issues und Pull Requests.
7. Frage nicht erneut nach Informationen, die im Repository bereits eindeutig beantwortet sind.

## Quellenprioritaet

Nutze folgende Reihenfolge zur Einordnung von Informationen:

1. aktueller Code und Tests = technischer Ist-Stand
2. `docs/PROJECT_CONTEXT.md` = verbindliche fachliche Entscheidungen
3. spezialisierte Dokumente in `docs/` = Detailregeln
4. `docs/OPEN_POINTS.md` = bewusst noch offene Themen
5. Issues, Pull Requests, Commit-Historie und README = ergaenzender Kontext

Wenn zwei Quellen widersprechen, den Widerspruch sichtbar machen und nicht stillschweigend eine Annahme waehlen.

## Projektgrundsaetze

Folgende Regeln duerfen nicht ohne explizite neue Entscheidung geaendert werden:

- Die mobile WebApp dient der sicheren und nachvollziehbaren Pulverentnahme, Pulvernachfuellung und dem Pulverwechsel in der FAM-Produktion.
- Frontend: HTML, CSS, JavaScript, mobile Nutzung auf Android, Kamera fuer QR-/Barcodes.
- Backend: ASP.NET Core / C# / REST API unter IIS.
- Oxaion ist das fuehrende ERP-System.
- Das Frontend kommuniziert nicht direkt mit Oxaion.
- Oxaion-Zugriffe laufen ueber das Backend und freigegebene Oxaion HTTP-Schnittstellen beziehungsweise vorhandene Oxaion-Fachlogik.
- Keine direkten ERP-Buchungen durch SQL-Manipulation von Oxaion-Tabellen.
- Oxaion-Programme, Parameter, Endpunkte, Tabellenlogik oder Buchungsschluessel niemals erfinden.
- Unsichere oder noch nicht bestaetigte Details als offen behandeln.
- Doppelbuchungen muessen verhindert werden.
- Jeder produktive Buchungsvorgang benoetigt eine eindeutige Transaktions-/Vorgangs-ID.
- Bei unklarem Buchungsausgang darf nicht blind erneut gebucht werden.
- Fehler muessen fuer den Bediener verstaendlich sein und eine konkrete Massnahme enthalten.
- Secrets und Zugangsdaten duerfen weder im Frontend noch im Repository stehen.
- Bereits getroffene fachliche Entscheidungen duerfen nicht stillschweigend veraendert werden.

## Umgang mit neuen Entscheidungen

Wenn im Verlauf der Arbeit eine neue verbindliche fachliche oder technische Entscheidung getroffen wird:

1. setze sie in Code nur dann um, wenn sie ausreichend geklaert ist;
2. aktualisiere gleichzeitig die passende Datei unter `docs/`;
3. aktualisiere `docs/OPEN_POINTS.md`, wenn ein offener Punkt dadurch erledigt oder neu hinzugekommen ist;
4. halte Dokumentation und Code konsistent;
5. dokumentiere wichtige Abweichungen oder Migrationen nachvollziehbar.

Neue Informationen sollen nicht nur im Chat verbleiben. Dauerhaft relevante Entscheidungen gehoeren ins Repository.

## Arbeitsstil

- Bestehende Loesungen zuerst verstehen, dann aendern.
- Kleine, nachvollziehbare und testbare Aenderungen bevorzugen.
- Vorhandene Architektur respektieren.
- Keine grossen Refactorings oder Architekturwechsel ohne klaren Nutzen und vorherige Abstimmung.
- Bei Fehleranalysen zuerst die konkrete Ursache und den aktuellen Datenfluss nachvollziehen.
- Bei Oxaion-Integration keine Annahmen als Fakten darstellen.
- Wenn etwas bereits dokumentiert ist, darauf aufbauen statt den Prozess neu zu erfinden.

## Erwartetes Verhalten bei einer Anfrage

Beginne intern immer mit der Frage:

**Welche Informationen zu dieser Aufgabe existieren bereits im Repository?**

Suche diese Informationen zuerst. Formuliere danach die Antwort oder fuehre die Aenderung durch.

Wenn die Aufgabe eine Implementierung verlangt, liefere nicht nur einen Vorschlag, sondern aktualisiere den passenden Code, Tests und die relevante Dokumentation, soweit die Aufgabe dies erfordert.

---

Diese Richtlinie ergaenzt `AGENTS.md`. Bei Abweichungen ist die aktuellere und spezifischere verbindliche Projektentscheidung im Repository massgeblich.
