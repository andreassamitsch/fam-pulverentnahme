# Chargenherkunft ueber Oxaion HTTP

Stand: 08.10.2026

## Ziel

Die FAM-WebApp soll fuer eine Oxaion-Charge die zugrunde liegenden Grundchargen ermitteln koennen, ohne die Oxaion-Chargenherkunft per eigener SQL-Logik nachzubauen.

Oxaion bleibt fachlich fuehrend fuer die Herkunftsauflösung. Das Backend ruft die vorhandene Oxaion-Fachlogik ueber den bereits verwendeten App-Tunnel auf und reduziert den gelieferten Herkunftsbaum auf die fuer FAM relevanten eindeutigen Grundchargen.

Es findet keine ERP-Aenderung und keine SQL-Buchung statt.

## Nachgewiesener Oxaion-Ablauf

Die Transaktionsmitschnitte vom 07./08.10.2026 bestaetigen fuer `TX_USAGE=CH` folgenden Leseweg:

1. `US17490J / *USGPARAMS`
2. Rueckgabe unter anderem:
   - `SSID`
   - `PGMN=US17476R`
   - `ANWG=UST`
3. `US17476R / *GETHDR`
4. `US17476R / *FIRSTLIST` mit `mode=reset`
5. Die erste Baumzeile liefert unter `KEY`:
   - `PESSID`
   - `PEMPOS`
6. Zeilen mit `ROW SUBTREES="TRUE"` besitzen weitere Herkunftsebenen.
7. Das Aufklappen erfolgt erneut ueber `US17476R / *FIRSTLIST` mit demselben `SSID` sowie dem `PESSID/PEMPOS` des Knotens und `NoHeader=true`.
8. Dieser Schritt wird wiederholt, bis keine aufklappbaren Knoten mehr vorhanden sind.

Damit ist die rekursive Baumlogik technisch nachgewiesen. Es wird kein eigener SQL-Nachbau der Oxaion-Verknuepfungen verwendet.

## Relevante Ergebnisfelder

Aus der durch Oxaion gelieferten `UPOVEP`-Struktur werden aktuell verwendet:

- `UPOVEP.PESTCK` - Aufloesungs-/Baumebene
- `UPOVEP.PEPONR` - Charge
- `UPOVEP.PEIDNR` - Artikel
- `UPOVEP.PELINR` - Lieferanteninformation
- `UPOVEP.PEBENR` - Bestellung
- `UPOVEP.PELFNR` - Lieferschein
- `UPOVEP.PEFAUN` - Fertigungsauftrag
- `UPOVEP.PEWEGN` - Wareneingang
- `KEY/PESSID`
- `KEY/PEMPOS`
- `ROW/@SUBTREES`

## Filterregel fuer Grundchargen

Oxaion liefert eine Grundcharge im Herkunftsbaum mehrfach, wenn dieselbe Charge beispielsweise als Wareneingang und zusaetzlich in spaeteren Fertigungsauftraegen vorkommt.

Die FAM-Ausgabe wird deshalb nicht aus einzelnen Baumzeilen abgeleitet.

Verbindliche Filterregel:

1. Herkunftsbaum vollstaendig rekursiv lesen.
2. Alle Zeilen nach `Artikel + Charge` betrachten.
3. Wenn dieselbe `Artikel + Charge`-Kombination irgendwo mit `SUBTREES=TRUE` vorkommt, ist sie eine weiter aufloesbare Zwischen-/Mixcharge und wird nicht als Grundcharge ausgegeben.
4. Kombinationen ohne eigenen Unterbaum gelten als terminale, von Oxaion gelieferte Grundchargen.
5. Gleiche terminale `Artikel + Charge`-Kombinationen werden auf genau einen Eintrag verdichtet.
6. Vorhandene Lieferanten-/Bestell-/Wareneingangsinformationen werden aus den zugehoerigen Oxaion-Zeilen uebernommen.

Die Logik filtert ausdruecklich nicht nach einem Namensbestandteil wie `MIX`. Entscheidend ist die Oxaion-Baumstruktur.

## Backend-Implementierung

Service:

`ChargeOriginService`

Read-only API:

`GET /api/charge-origin/base-batches?article=<Artikel>&batch=<Charge>`

Der Endpoint setzt eine gueltige Mitarbeiter-Session voraus.

Die Antwort enthaelt:

- Ausgangsartikel
- Ausgangscharge
- eindeutige Grundchargen
- soweit von Oxaion vorhanden: Lieferant, Bestellung, Lieferschein, Wareneingang
- Anzahl gelesener Baumzeilen
- Anzahl aufgeklappter Herkunftsknoten

Zur technischen STAGING-Diagnose existiert vorlaeufig der optionale Queryparameter `objectId`. Er ist keine Bedienereingabe und soll nicht in eine spaetere Produktionsoberflaeche uebernommen werden.

## Vollstaendigkeit und Fehlerverhalten

Eine Herkunftsseite wird nur verarbeitet, wenn die Oxaion-Antwort den im Mitschnitt bestaetigten `STOP`-Marker enthaelt.

Fehlt `STOP`, liefert das Backend bewusst kein moeglicherweise unvollstaendiges Ergebnis. Eine nicht bestaetigte Pagination fuer `US17476R` wird nicht erfunden.

Zum Schutz gegen fehlerhafte oder unerwartet zyklische Baeume ist die Anzahl automatisch aufklappbarer Knoten serverseitig begrenzt.

Transport-, Oxaion- oder Strukturfehler fuehren zu `CHARGE_ORIGIN_UNAVAILABLE`. Es findet dabei keine Buchung statt.

## Noch offener Einstiegspunkt

Die aufgenommenen Oxaion-UI-Transaktionen enthalten beim Einstieg zusaetzlich `POOBID` und `FIOBID`, zum Beispiel fuer die untersuchte Mixcharge eine konkrete UPOST-Objekt-ID.

Noch nicht nachgewiesen ist, ob `US17490J / *USGPARAMS` fuer `TX_USAGE=CH` mit Artikel und Charge sowie `POOBID/FIOBID=0` denselben Herkunftsbaum startet.

Der aktuelle Backend-Stand verwendet ohne technischen Override den Wert `0` und muss damit in STAGING getestet werden.

Falls Oxaion diesen Einstieg ablehnt, wird nicht auf eigene SQL-Herkunftslogik ausgewichen. Dann ist als naechster Schritt der freigegebene Oxaion-HTTP-Weg zur Ermittlung der UPOST-Objekt-ID zu bestimmen und zu dokumentieren.
