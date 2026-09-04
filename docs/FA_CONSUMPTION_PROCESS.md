# Pulververbrauch auf Fertigungsauftrag

Stand: 04.09.2026

Diese Datei dokumentiert die aktuelle spezifische fachliche Entscheidung fuer den vierten Bedienvorgang `Pulver auf Fertigungsauftrag buchen`.

Der Vorgang ist bewusst eigenstaendig und wird nicht mit Nachfuellen, Tankauslagerung oder Neubefuellung verkettet. Die allgemeinen Regeln aus `AGENTS.md`, `docs/ERROR_HANDLING.md`, `docs/QR_CODE_WORKFLOW.md` und der Mitarbeiter-Authentifizierung gelten unveraendert.

## Fachlicher Zweck

Nach dem realen Verbrauch von Pulver fuer einen Fertigungsauftrag soll der Bediener den tatsaechlich verbrauchten Anteil aus dem aktuell verwendeten Maschinentank erfassen und spaeter ueber den noch zu bestaetigenden Oxaion-BDE-/PPS-Ablauf auf den Fertigungsauftrag buchen.

Der Bedienablauf ist verbindlich:

1. Mitarbeiter anmelden.
2. Maschinentank scannen.
3. Aktuellen Tankbestand eindeutig aus Oxaion lesen.
4. Fertigungsauftrag scannen.
5. Rohmaterialartikel aus dem FA-QR gegen den aktuell im Tank vorhandenen Artikel pruefen.
6. Tatsaechlichen Pulververbrauch in kg bewusst eingeben.
7. Verbrauch gegen den aktuellen Tankbestand pruefen.
8. Vor einer spaeteren produktiven Buchung Tankbestand, Mitarbeiter, Fertigungsauftrag und Oxaion-Buchungsvoraussetzungen erneut serverseitig validieren.
9. Erst nach technisch bestaetigtem Oxaion-Schreibablauf die FA-Materialrueckmeldung ausloesen.

## Maschinentank

Der Tank wird wie in den anderen Prozessen physisch per Tank-QR identifiziert.

Der QR enthaelt nur den Oxaion-Tanklagerort, beispielsweise:

```text
EOS1
```

Der gescannte Wert muss gegen die gepflegte Tankliste validiert werden. Danach wird der vorhandene bestaetigte `LB30230R`-Leseablauf verwendet.

Fuer diesen Vorgang ist nur ein eindeutig positiver Tankbestand zulaessig:

- genau eine positive Position;
- Mengeneinheit `KGM`;
- kein negativer Bestand;
- eindeutiger Artikel;
- eindeutige aktuelle Mix-Charge.

Ein leerer, mehrdeutiger oder fachlich unplausibler Tank blockiert den Vorgang.

Mindestens angezeigt werden:

- tatsaechlicher Tanklagerort;
- Pulverartikel und Artikelbezeichnung;
- aktuelle Mix-Charge;
- aktueller Tankbestand;
- EFA01/EFA02-Erkennungsfarben, soweit aus dem bestaetigten Leseweg verfuegbar.

## Fertigungsauftrag-QR

Das bereits dokumentierte Format bleibt unveraendert:

```text
Rohmaterialartikel+++Fertigungsauftrag+++Maschinen-ID
```

Beispiel:

```text
RP.00010+++FA24FI00118+++EP-M650-1
```

Im vorbereiteten Ablauf werden genau drei nichtleere Teile verlangt. Eine weitergehende syntaktische Fertigungsauftrags-Regel wird nicht erfunden, solange sie im Repository nicht als verbindlich bestaetigt ist.

### Artikelpruefung

Der Rohmaterialartikel aus dem FA-QR muss exakt zum aus Oxaion gelesenen Tankartikel passen.

Beispiel:

```text
Tank:   RP.00010
FA-QR:  RP.00010+++FA24FI00118+++EP-M650-1
```

ist fuer die Vorbereitung zulaessig.

Bei

```text
Tank:   RP.00010
FA-QR:  RP.00006+++FA24FI00118+++EP-M650-1
```

wird der FA-Scan abgelehnt und nicht in den Vorgang uebernommen.

Die Artikelpruefung ist eine Schutzpruefung gegen eine offensichtliche falsche Materialzuordnung. Sie ersetzt nicht die spaetere serverseitige Oxaion-Pruefung des Fertigungsauftrags und seiner Materialpositionen.

## Planmaschine und tatsaechlicher Tank

Die Maschinen-ID im FA-QR ist weiterhin die Plan-/Auftragsinformation und nicht der Oxaion-Tanklagerort.

Die verbindliche Zuordnung beispielsweise

```text
EP-M650-1 <-> EOS1
```

ist weiterhin offen und wird nicht erfunden.

Zusaetzlich ist bereits fachlich entschieden, dass die tatsaechliche Produktionsmaschine kurzfristig von der urspruenglichen FA-Planung abweichen kann. Deshalb gilt fuer diesen vorbereiteten Vorgang:

- der physisch gescannte Tank ist die tatsaechlich verwendete Quelle;
- die Maschinen-ID aus dem FA-QR wird sichtbar mitgefuehrt;
- ein Unterschied beziehungsweise eine noch nicht aufloesbare Zuordnung zwischen FA-Maschine und Tank darf derzeit nicht automatisch als fachlicher Fehler interpretiert werden;
- die WebApp trifft keine organisatorische Maschinenfreigabeentscheidung.

## Verbrauch

Der Verbrauch ist eine bewusste Bedienereingabe in kg.

Verbindlich:

- keine Vorbelegung;
- Wert muss groesser als `0` sein;
- Wert darf den aktuell gelesenen Tankbestand nicht ueberschreiten;
- die PWA zeigt den rechnerischen Restbestand als Bedienhilfe an;
- der rechnerische Restbestand ist vor der spaeteren Oxaion-Buchung keine neue ERP-Wahrheit.

Beispiel:

```text
Tankbestand:  82,500 kg
Verbrauch:     4,250 kg
Rechnerisch:  78,250 kg Rest
```

Direkt vor einer spaeteren produktiven Buchung muss der aktuelle Tankbestand erneut aus Oxaion gelesen werden. Hat sich Artikel, Mix-Charge oder Menge gegenueber der Vorbereitung geaendert, darf nicht mit dem alten Zustand blind weitergebucht werden.

## Vor einer spaeteren produktiven Buchung

Mindestens folgende serverseitigen Sicherheitspruefungen sind erforderlich:

1. Personal-Session muss zum Vorgang passen.
2. Mitarbeiter wird erneut ueber den bestaetigten Oxaion-Personalweg gelesen.
3. Tanklagerort wird erneut gelesen.
4. Tankartikel und aktuelle Mix-Charge muessen weiterhin zum vorbereiteten Vorgang passen.
5. Der aktuelle Tankbestand muss fuer den Verbrauch ausreichen.
6. Der Fertigungsauftrag muss ueber einen noch zu bestaetigenden Oxaion-Lese-/BDE-PPS-Weg fachlich validiert werden.
7. Rohmaterialartikel beziehungsweise relevante Materialposition des Fertigungsauftrags muss zum gebuchten Pulver passen.
8. Eine bestaetigte Oxaion-Sperre muss als `LOCKED` behandelt werden.
9. Jeder produktive Vorgang erhaelt eine eindeutige `clientOperationId` und eine serverseitige Transaktions-ID.
10. Ein unklarer Buchungsausgang fuehrt zu `UNCERTAIN` beziehungsweise `MANUAL_REVIEW_REQUIRED`; kein blinder Retry.

## Noch offene Oxaion-Schreiblogik

Im Repository sind weiterhin ausdruecklich offen:

- konkrete Oxaion HTTP-Aufrufe fuer die FA-Materialrueckmeldung;
- konkretes Oxaion BDE-/PPS-Programm fuer diese Materialbuchung;
- benoetigte Parameter und Buchungsschluessel;
- belastbare Ergebnis-/Statusabfrage fuer diese Buchungsart;
- genaue Sperrerkennung fuer den relevanten Fertigungsauftrag beziehungsweise Fachdatensatz.

Diese Details werden nicht aus dem bestaetigten Lagerbeleg-/Mix-Ablauf abgeleitet.

Bis ein realer Oxaion-/JET-Datenstrom fuer die FA-Materialrueckmeldung analysiert und bestaetigt wurde, endet die PWA an der sicheren Vorbereitungsschwelle. Der Button `Verbrauch auf Fertigungsauftrag buchen` bleibt deaktiviert.

## Aktueller STAGING-Implementierungsstand

Auf `feature/separate-processes` ist der Vorgang bis zur sicheren Vorbereitung umgesetzt:

- vierte Prozessauswahl `Pulver auf Fertigungsauftrag buchen`;
- Tank-QR scannen und aktuellen eindeutigen Tankbestand lesen;
- FA-QR mit exakt drei Teilen scannen;
- FA-Rohmaterial gegen Tankartikel pruefen;
- Fertigungsauftrag und Maschinen-ID aus dem QR anzeigen;
- tatsaechlichen Tank separat anzeigen;
- Verbrauch ohne Vorbelegung erfassen;
- Verbrauch gegen Tankbestand pruefen;
- rechnerischen Rest anzeigen;
- schreibenden Button bewusst deaktiviert lassen, bis die Oxaion-FA-Materialrueckmeldung technisch bestaetigt ist.
