# Chargenherkunft ueber Oxaion HTTP

Stand: 08.10.2026

## Ziel

Die FAM-WebApp ermittelt die Grundchargen einer Mixcharge nicht durch eine eigene SQL-Rekonstruktion, sondern ueber die vorhandene Oxaion-Fachlogik fuer die Chargenherkunft.

Oxaion bleibt fachlich fuehrend. Das Backend liest den von Oxaion erzeugten Herkunftsbaum und reduziert ihn auf die fuer FAM benoetigten eindeutigen Grundchargen.

## Bestaetigte Quelle

Grundlage sind die am 07./08.10.2026 aufgezeichneten Oxaion-Transaktionsprotokolle:

- Chargenherkunft oeffnen
- Herkunftsbaum mehrfach aufklappen

Bestaetigt ist folgende Folge:

1. `US17490J *LOADUSGI` mit `KEYTYPE=UPOST` und Artikel/Charge.
2. `US17490J *USGPARAMS` mit `TX_USAGE=CH`.
3. Antwort liefert `PGMN=US17476R`, `ANWG=UST`, `SSID` und die Schluesselfelder `PESSID PEMPOS`.
4. `US17476R *GETHDR`.
5. `US17476R *FIRSTLIST` mit `mode=reset` liefert den Wurzelknoten.
6. Zeilen mit `ROW SUBTREES="TRUE"` werden mit einem weiteren `US17476R *FIRSTLIST` aufgeloest.
7. Der Unterbaum wird dabei ueber `PESSID` und `PEMPOS` adressiert. Die weiteren Aufrufe verwenden `NoHeader=true`.
8. Dieser Vorgang wird rekursiv fortgesetzt, bis keine Zeile mehr `SUBTREES="TRUE"` besitzt.

Die Listen im Mitschnitt enden mit `<STOP />`. Das Backend behandelt einen fehlenden STOP-Marker bewusst nicht als vollstaendiges Ergebnis.

## Ergebnisfelder

Aus `UPOVEP` werden aktuell gelesen:

- `PESTCK` - Aufloesungs-/Baumebene
- `PEPONR` - Charge
- `PEIDNR` - Artikel
- `PELINR` - Lieferant/Firma wie von Oxaion geliefert
- `PEBENR` - Bestellung
- `PELFNR` - Lieferschein
- `PEFAUN` - Fertigungsauftrag
- `PEWEGN` - Wareneingang
- `PESSID` und `PEMPOS` aus `KEY` - Unterbaum-Schluessel
- `ROW/@SUBTREES` - Kennzeichen fuer weitere Herkunftsebenen

Die Oxaion-Spalte `PEIDNR` wurde im Mitschnitt als zusammengesetzter Anzeigewert wie
`RP.00010              RP.00010` geliefert. Fuer die API wird daraus die erste Artikel-ID verwendet.

## Filterung auf Grundchargen

Eine Charge gilt fuer die FAM-Ausgabe als Zwischen-/Mixcharge, wenn fuer dieselbe Kombination aus Artikel und Charge mindestens eine Oxaion-Zeile `SUBTREES=TRUE` besitzt.

Damit werden auch die im Mitschnitt sichtbaren Faelle korrekt behandelt, bei denen dieselbe Mixcharge zusaetzlich in normalen Fertigungsauftragszeilen ohne `SUBTREES` vorkommt.

Grundchargen sind danach die eindeutigen Artikel-/Chargen-Kombinationen, fuer die im vollstaendig expandierten Baum kein Unterbaum existiert.

Mehrfach vorkommende Zeilen derselben Grundcharge werden zusammengefasst. Fuer die sichtbaren Zusatzdaten wird bevorzugt die Zeile mit den meisten von Oxaion gelieferten Herkunftsfeldern verwendet. Im Mitschnitt ist dies zum Beispiel fuer Charge `84671` die Zeile mit Lieferant `3001399 000`, Bestellung `FA24BE00022` und Wareneingang `FA24WE00027`.

## API

Read-only Endpoint:

`GET /api/charge-origin?article=<Artikel>&batch=<Charge>&objectId=<optionale-UPOST-Objekt-ID>`

Beispiel:

`/api/charge-origin?article=RP.00010&batch=RP00010MIX_20261006_144459&objectId=14089546`

Die interne Oxaion-`POOBID/FIOBID` ist optional. Wenn sie bekannt ist, wird sie exakt als `POOBID` und `FIOBID` weitergegeben.

Ohne `objectId` wird kein Ersatzwert erfunden. Das Backend sendet dann nur die durch den Mitschnitt bestaetigten Artikel-/Chargenfelder. Ob Oxaion diesen Einstieg in FAM-STAGING ohne interne Objekt-ID akzeptiert, ist noch durch einen Live-Test zu bestaetigen.

Die Antwort enthaelt:

- Eingabeartikel
- Eingabecharge
- eindeutige `baseBatches`
- Anzahl gelesener Oxaion-Zeilen
- Anzahl automatisch expandierter Unterbaumknoten
- maximale gefundene Ebene
- die verwendete optionale Objekt-ID

## Sicherheitsgrenzen

Die Funktion ist rein lesend und fuehrt keine ERP-Buchung aus.

Zum Schutz vor fehlerhaften oder zyklischen Antworten gelten:

- maximal 64 Ebenen
- maximal 256 expandierte Unterbaumknoten
- maximal 10.000 eingelesene Ergebniszeilen
- bereits expandierte `PESSID/PEMPOS`-Knoten werden nicht erneut aufgerufen
- fehlender `STOP`-Marker fuehrt zu einem Fehler statt zu einer unvollstaendigen Ausgabe

## Noch offen

Der Mitschnitt zeigt `POOBID/FIOBID` beim Einstieg, aber nicht den vorherigen technischen Schritt, mit dem die Oxaion-Oberflaeche diese interne UPOST-Objekt-ID aus Artikel und Charge ermittelt.

Daher gilt:

- keine Objekt-ID erfinden oder aus einem nicht bestaetigten Schema ableiten;
- zuerst STAGING testen, ob `US17490J` mit Artikel+Charge ohne `POOBID/FIOBID` funktioniert;
- falls Oxaion die Objekt-ID zwingend benoetigt, den dazugehoerigen bestaetigten Oxaion-Leseweg separat ermitteln und dokumentieren.

## Bedienoberflaeche ab Version 0.1.11

Die im STAGING vom Bediener erfolgreich gepruefte read-only Oxaion-API bleibt die einzige Quelle. Hinzu kommen zwei Einstiege:

1. `Bestaende anzeigen`: Bei eindeutig belegten Maschinentanks ist die Tankkarte anklickbar und zeigt `Maschinentankdetails`. Die Pulverlagerzeile zeigt weiterhin `Lagerplatzdetails`. In beiden Detailansichten gibt es `Chargenherkunft anzeigen`. Artikel und Charge werden unveraenderbar aus der geklickten Oxaion-Bestandsposition uebernommen.
2. Eigenstaendiger Vorgang `Chargenherkunft anzeigen`: ein Chargenetikett `Artikel+++Charge` ueber den vorhandenen Kamera-QR-Scanner scannen oder Artikelnummer und Charge manuell eingeben. Zugelassen sind `RP.*` und `PB.*` (Kundenbeistellung).

Der Scanner oeffnet zuerst die Kamera und aktiviert die Erkennung erst nach bewusster Auswahl `Scannen`. Es erfolgt nur `GET /api/charge-origin`. Das Frontend zeigt die eindeutigen Grundchargen mit Artikel und optionalen von Oxaion gelieferten Metadaten wie Lieferant, Bestellung und Wareneingang. Keine Grundchargen oder ein HTTP-Fehler ergeben eine deutliche Warnung, nicht eine Buchungsfreigabe.

Chargenherkunft ist online-only, erzeugt keine Buchung, keine Outbox und keinen lokalen Herkunfts-Cache. `objectId` wird nicht aus der UI erfunden oder geraten; der Einstieg ohne `objectId` wurde durch den Benutzer am 08.10.2026 fuer den Testfall erfolgreich bestaetigt. Weitere Material- und Artikelkombinationen sind im STAGING zu testen.

## 0.1.12 – Android-Bedienkorrektur und Anzeige

Der 0.1.11-STAGING-Test vom 08.10.2026 zeigte: Aufruf der Herkunft aus einem Pulverlager-/Tankdetail funktioniert, aber Kamera- und Suchbutton im eigenstaendigen Vorgang reagieren nicht. Ursache ist die zeitliche Reihenfolge: `charge-origin-ui.js` bindete am `DOMContentLoaded`-Event, jedoch erzeugt `process-mode.js` sein Panel erst spaeter in `ensureUi()`. Das fuehrte zugleich dazu, dass das CSS fuer die Herkunftskarten im Detaildialog fehlte.

Ab `0.1.12` wird `FamChargeOriginUi.bind()` direkt nach `ensureUi()` aufgerufen, idempotent; die Styles werden unabhaengig davon einmal bei Modulladung registriert. Ein neuer JavaScript-Laufzeittest bildet genau die vorher fehlerhafte asynchrone DOM-Reihenfolge nach und prueft sowohl Kamera-Scan als auch manuelle Abfrage.

UI-Aenderungen: eigenstaendige, deutlich abgegrenzte Grundchargenkarten mit lesbaren Beschriftungen, flexibler Darstellung auf Android und optionalen Beschaffungsdaten. Der mit `ChargeOriginBaseBatch.ProductionOrder` vom Oxaion-Read-Service gelieferte FA wird **in der UI nicht angezeigt**, weil der Datensatz einen verbrauchenden statt den Ursprungs-FA enthalten kann. Ohne beschaffungsbezogene Felder wird kein Ersatzwert erdacht. `Zurueck zu Details` stellt das jeweilige Tank-/Lagerdetail wieder her; der neue rote `Schliessen`-Button schliesst die Herkunft vollstaendig. In den Bestandsdetails ist der bestehende `Schliessen`-Button ebenfalls rot. Es wurden keine Backend-/Buchungsaufrufe geaendert.
