# Separate Tankvorgaenge

Stand: 04.09.2026

Diese Datei dokumentiert die neue fachliche Entscheidung, den Pulverwechsel und die weiteren Pulverbewegungen nicht als einen einzigen automatisch verketteten Buchungsvorgang zu behandeln. Die Bedienoberflaeche bietet stattdessen separate Vorgaenge. Die allgemeinen Regeln zu Personal-Session, QR-Scanner, Oxaion-Revalidierung, Idempotenz, Fehlerbehandlung und `Dev-Infos` bleiben unveraendert.

**Diese neuere und spezifischere Entscheidung ersetzt fuer den aktuellen Entwicklungsstand die aeltere monolithische Bezeichnung beziehungsweise Ablaufbeschreibung `Pulver tauschen` sowie die aeltere Zweier-Liste der Hauptfunktionen in `docs/PROJECT_CONTEXT.md`.** Die dort beschriebenen Sicherheitsziele und noch offenen Oxaion-Schreibdetails bleiben weiterhin gueltig; die Bedien- und Transaktionsgrenzen sind jetzt genauer in separate Vorgaenge aufgeteilt. Bei der naechsten Konsolidierung der Hauptdokumentation sind diese aelteren Abschnitte entsprechend nachzuziehen.

## Sichtbare Vorgaenge

Nach erfolgreicher Mitarbeiter-Anmeldung waehlt der Bediener einen Vorgang:

1. `Pulver nachfuellen` - bereits live bestaetigter bestehender Ablauf.
2. `Pulver aus Tank auslagern`.
3. `Neues Pulver in Tank fuellen`.
4. `Pulver auf Fertigungsauftrag buchen`.

Die Vorgaenge sind fachlich eigenstaendig. Ein Pulverwechsel kann organisatorisch aus `Pulver aus Tank auslagern` und danach `Neues Pulver in Tank fuellen` bestehen, die WebApp darf beide aber nicht als eine atomare Oxaion-Transaktion vortaeuschen. Die spaetere Verbrauchsbuchung auf einen Fertigungsauftrag ist ebenfalls ein eigener Vorgang mit eigener Transaktionsgrenze.

## Vorgang: Pulver aus Tank auslagern

Bedienablauf:

1. Mitarbeiter ist angemeldet.
2. Maschinentank per Tank-QR scannen.
3. Aktuellen Tankbestand vollstaendig und eindeutig aus Oxaion lesen.
4. Artikel, aktuelle Mix-Charge, Erkennungsfarben und komplette Systemmenge anzeigen.
5. Bediener gibt Ziel-Lagerort und Ziel-Lagerplatz ein.
6. Vor der spaeteren Buchung muss der aktuelle Tankbestand erneut aus Oxaion gelesen und exakt mit dem vorbereiteten Zustand verglichen werden.
7. Die gesamte zum Buchungszeitpunkt bestaetigte Tankmenge wird als Auslagerungsmenge verwendet; es gibt in diesem Vorgang keine freie Teilmengen-Eingabe.

Sicherheitsregeln:

- Ein leerer Tank kann nicht ausgelagert werden.
- Mehrdeutiger, negativer oder in unerwarteter Einheit gefuehrter Tankbestand blockiert den Vorgang.
- Ziel-Lagerort und Lagerplatz sind Bedienereingaben gemaess aktueller fachlicher Entscheidung; bevor produktiv gebucht wird, muss geklaert werden, wie diese Zielwerte gegen Oxaion sicher validiert werden.
- Mitarbeiter wird direkt vor einem spaeteren schreibenden Oxaion-Aufruf wie beim Nachfuellen erneut exakt in Oxaion validiert.
- Jeder produktive Auslagerungsvorgang benoetigt eine eigene `clientOperationId` und Backend-Transaktions-ID.

### Noch offene Oxaion-Schreiblogik

Der schreibende Ablauf `Maschinentank -> Pulverlager` ist im Repository noch nicht durch einen realen Oxaion/JET-Datenstrom bestaetigt. Insbesondere duerfen Buchungsschluessel, Parameter, Chargenbehandlung oder Ergebnisverifikation nicht aus dem bestaetigten Nachfuell-/Mix-Ablauf geraten werden.

Bis dieser Schreibablauf bestaetigt ist, darf die PWA den Vorgang vorbereiten und alle lesenden Sicherheitspruefungen ausfuehren, aber keine produktive Oxaion-Materialbuchung ausloesen.

## Vorgang: Neues Pulver in Tank fuellen

Dieser Vorgang ist fuer eine neue Befuellung eines eindeutig leeren Maschinentanks vorgesehen. Befindet sich bereits Pulver im Tank, ist entweder der vorhandene Vorgang `Pulver nachfuellen` zu verwenden oder das vorhandene Pulver zuerst mit `Pulver aus Tank auslagern` zu entfernen.

Bedienablauf:

1. Mitarbeiter ist angemeldet.
2. Maschinentank per Tank-QR scannen.
3. Oxaion muss den Tank eindeutig als leer bestaetigen. Ein Lese-/Transportfehler darf niemals als Leerstand interpretiert werden.
4. Erste Pulvercharge als `Artikel+++Charge` scannen.
5. Der Artikel der ersten gueltigen Charge wird zum Artikel dieser neuen Tankbefuellung.
6. Weitere gescannte Chargen muessen denselben Artikel haben.
7. Fuer jede Charge werden Lagerort und interner Lagerplatz aus den aktuellen positiven Oxaion-Bestandspositionen ermittelt. Bei mehreren Positionen bestaetigt der Bediener den tatsaechlichen Entnahmeort; bereits verwendete exakte Positionen werden ausgeschlossen.
8. Fuer jede Charge wird die tatsaechliche Einfuellmenge bewusst eingegeben und nicht vorbelegt.
9. Die neue Mix-Charge folgt weiterhin dem bestaetigten Schema `<Artikel ohne Punkt>MIX_<yyyyMMdd>_<HHmmss>`.
10. Vor einer spaeteren schreibenden Buchung muss erneut bestaetigt werden, dass der Tank noch leer ist und alle ausgewaehlten Quellen mit ausreichender Menge unveraendert vorhanden sind.

Sicherheitsregeln:

- Tank nicht eindeutig leer -> keine neue Befuellung.
- Falscher Artikel bei Folgecharge -> Scan wird nicht in den Vorgang uebernommen.
- Quellen stammen aus Oxaion und werden nicht als freie Buchungsschluessel erfunden.
- Dieselbe Charge darf auf verschiedenen Oxaion-Bestandspositionen mehrfach verwendet werden; dieselbe exakte Position Lagerort/Lagerplatz/Charge nicht doppelt.
- Mengen werden nicht vorbelegt und duerfen den aktuellen Quellenbestand nicht ueberschreiten.
- Mitarbeiter wird direkt vor einem spaeteren schreibenden Oxaion-Aufruf erneut exakt validiert.
- Jeder produktive Vorgang benoetigt eigene `clientOperationId` und Backend-Transaktions-ID.

### Noch offene Oxaion-Schreiblogik

Der bereits bestaetigte Nachfuell-/Mix-Ablauf setzt eine vorhandene alte Mix-Charge auf dem Maschinentank voraus und erzeugt Position 1 `alte Mix-Charge -> neue Mix-Charge`. Fuer einen komplett leeren Tank existiert dieser Ausgangszustand nicht.

Deshalb darf nicht angenommen werden, dass die vorhandene Position-1-/Fortsetzungslogik unveraendert fuer `Pulverlager -> leerer Maschinentank` verwendet werden kann. Der konkrete Oxaion/JET-Schreibablauf und die belastbare Abschluss-/Recovery-Verifikation muessen zuerst real bestaetigt werden.

Bis dahin endet auch dieser neue Vorgang an der sicheren Vorbereitungsschwelle ohne Materialbuchung.

## Vorgang: Pulver auf Fertigungsauftrag buchen

Dieser Vorgang bildet die spaetere Verbrauchsbuchung des tatsaechlich verbrauchten Pulvers auf genau einen Fertigungsauftrag ab.

Bedienablauf:

1. Mitarbeiter ist angemeldet.
2. Maschinentank per Tank-QR scannen.
3. Tank muss genau einen plausiblen positiven Pulverbestand liefern.
4. Fertigungsauftrag im bestehenden Format `Rohmaterial+++Fertigungsauftrag+++Maschinen-ID` scannen.
5. Rohmaterial aus dem FA-QR muss dem aus Oxaion gelesenen Tankartikel entsprechen.
6. Die Maschinen-ID aus dem FA wird angezeigt, aber wegen der bereits vorgesehenen kurzfristigen Maschinenabweichung und der weiterhin offenen Maschinen-ID-zu-Tank-Referenz nicht als automatische Sperrregel verwendet.
7. Tatsaechlichen Pulververbrauch in kg bewusst eingeben; keine Vorbelegung.
8. Verbrauch muss groesser als 0 sein und darf den aktuell gelesenen Tankbestand nicht ueberschreiten.
9. Die PWA zeigt den rechnerischen Restbestand nur als Bedienhilfe an.
10. Vor der spaeteren produktiven Buchung muessen Tankbestand, Mitarbeiter, Fertigungsauftrag und die relevante Oxaion-Materialposition erneut serverseitig validiert werden.

Die Detailregeln stehen in `docs/FA_CONSUMPTION_PROCESS.md`.

### Noch offene Oxaion-Schreiblogik

Die konkrete Oxaion-FA-Materialrueckmeldung ist weiterhin nicht technisch bestaetigt. Insbesondere fehlen der bestaetigte BDE-/PPS-Programmweg, Parameter/Buchungsschluessel und eine belastbare Ergebnisverifikation fuer diese Buchungsart.

Bis ein realer Oxaion-/JET-Datenstrom analysiert und bestaetigt wurde, endet auch dieser Vorgang an der sicheren Vorbereitungsschwelle. Der Buchungsbutton bleibt deaktiviert.

## Wiederverwendete bestaetigte Bausteine

Fuer die separaten Vorgaenge werden ohne fachliche Aenderung wiederverwendet:

- NFC-Login beziehungsweise Personalnummer + SYNCOS-Passwort;
- serverseitige Personal-Session;
- Maschinentank-QR und Tank-Whitelist;
- `LB30230R`-basierter Tankbestand;
- EFA01/EFA02 als Erkennungshilfe, wo ein Artikel bekannt ist;
- Chargen-QR `Artikel+++Charge`, soweit der Prozess eine Pulvercharge benoetigt;
- Fertigungsauftrag-QR `Rohmaterial+++Fertigungsauftrag+++Maschinen-ID`, soweit der Prozess einen FA benoetigt;
- Oxaion-geführte Quelllager-/Lagerplatzauflösung `LB30340R`/`LB30430R` und `LAG1626`-Sonderfall;
- Mehrfachcharge auf unterschiedlichen exakten Oxaion-Bestandspositionen;
- bewusste Mengeneingabe ohne Vorbelegung;
- Kamera mit explizitem Scanstart und persistentem Zoom;
- Mitarbeiter-/Dev-Ansicht;
- klare, bestaetigungspflichtige Fehler- und Ergebnismeldungen;
- Kein-Blind-Retry-Regeln aus `docs/ERROR_HANDLING.md`.

## Aktueller Implementierungsstand auf `feature/separate-processes`

Die PWA bietet vier Prozessauswahlen:

- `Pulver nachfuellen`: bestehender, bereits buchbarer und verifizierter STAGING-Ablauf.
- `Pulver aus Tank auslagern`: Tank lesen, kompletten Bestand anzeigen, Ziel-Lagerort und Lagerplatz erfassen.
- `Neues Pulver in Tank fuellen`: leeren Tank bestaetigen, Charge(n) scannen, Oxaion-Quelle aufloesen und Mengen erfassen.
- `Pulver auf Fertigungsauftrag buchen`: Tank lesen, FA scannen, Rohmaterial pruefen, Verbrauch erfassen und gegen den Tankbestand pruefen.

Die drei noch nicht technisch bestaetigten Schreibvorgaenge enden absichtlich an der sicheren Vorbereitungsschwelle und nennen den jeweils noch offenen Oxaion-Schreibablauf. Der bestehende Vorgang `Pulver nachfuellen` bleibt unveraendert buchbar.
