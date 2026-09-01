# Oxaion-Maschinenbestand: Chargen pro Lagerort

## Zweck

Diese Datei dokumentiert die am 01.09.2026 im Oxaion-JET-Datenstrom beobachtete, rein lesende Bestandsabfrage fuer den aktuellen Pulverbestand eines Maschinen-Lagerorts.

Der konkrete Referenzfall war:

- Firma `103`
- erwarteter Artikel `RP.00010`
- Artikelbezeichnung `AlSi10Mg`
- Maschinen-Lagerort `EOS1`
- Lagerortbezeichnung `EOS 1 -Tank`

Die Abfrage dient im Prozess **Pulver nachfuellen** dazu, den tatsaechlichen Maschinenbestand vor der Buchung automatisch zu ermitteln. Alte Mix-Charge und Bestandsmenge werden nicht manuell vorgegeben.

## Bestaetigte Filterbedingung

Die nachtraeglich aufgezeichnete Selektionsmaske zeigt die fachliche Bedingung des frueher verwendeten Filters eindeutig:

- Selektionsfeld: `LLAWEP.LALABE` (`Lagerbestand`)
- Vergleichsoperation: `<>`
- Vergleichswert: `0`

Im Datenstrom erscheint die Auswahl als `LB30230 *SAVLST` fuer `LLAWEP.LALABE`; in Zeile `LFNU=0` steht `OPER=<>`. Die Rueckgabe fuehrt `IVLABE=0` und `IBLABE=0`.

Der Backend-Prototyp ist deshalb **nicht mehr von einem gespeicherten oder freigegebenen Oxaion-Filter wie `mit Bestand` abhaengig**. `LB30230 *GETFILTER` und `*LOADSET` werden fuer diese Funktion nicht mehr verwendet.

## Technisch verwendete Folge

Der fuer das Backend relevante rein lesende Ablauf ist:

1. `US30600J` mit Startkontext fuer den Maschinen-Lagerort
2. `LB30230R *GETHDR`
3. `LB30230R *FIRSTLIST` mit `mode=reset`
4. falls die Antwort noch kein `<STOP/>` enthaelt: `LB30230R *NEXTLIST` mit derselben `SSID`, bis `<STOP/>` geliefert wird
5. alle Zeilen des angeforderten Lagerorts auswerten
6. exakt die bestaetigte Bedingung `LLAWEP.LALABE != 0` anwenden

Der ungefilterte Referenzaufruf fuer `EOS1` lieferte 25 Zeilen verschiedener Artikel und Chargen inklusive Nullbestaenden und endete mit `<STOP/>`. Damit ist technisch nachgewiesen, dass die ungefilterte Liste in diesem Referenzfall nicht nur den erwarteten Artikel enthielt.

Das seitenweise Lesen ueber `*FIRSTLIST` und anschliessend `*NEXTLIST` bis `<STOP/>` entspricht der dokumentierten Oxaion-HTTP-Listenlogik. Der Backend-Code besitzt zusaetzlich eine Sicherheitsobergrenze fuer die Anzahl der Seiten; ohne bestaetigtes `<STOP/>` wird kein Maschinenbestand als vollstaendig akzeptiert.

## Relevante Rueckgabefelder

Die `LB30230R`-Tabelle liefert unter anderem:

- `KEY/LALAGO`: Lagerort
- `KEY/LAIDNR`: Artikel
- `KEY/LAPONR`: Charge
- `IDNR.TLBEZG`: Artikelbezeichnung
- `PONR.POCHNL`: Herstellercharge, soweit vorhanden
- `LLAWEP.LALABE`: Lagerbestand inklusive Mengeneinheit, zum Beispiel `164,330 KGM`
- `LLAWEP.LALADM`: Lagerwert
- `LLAWEP.LAYZLBU`: Datum/Uhrzeit der letzten Lagerbuchung
- `LLAGEP.LAYEAN`: Erstanlagedatum

Im Referenzfall verbleibt nach `LLAWEP.LALABE != 0` genau eine Zeile:

```text
Lagerort: EOS1
Artikel:  RP.00010
Charge:   RP10WEB_20260901_085443
Bestand:  164,330 KGM
```

## Backend-API und fachliche Ergebnisse

Der STAGING-Prototyp stellt bereit:

```text
GET /api/machine-stock?warehouse=EOS1&article=RP.00010&warehouseText=EOS%201%20-Tank&articleText=AlSi10Mg
```

Der Parameter `article` ist der fuer den Nachfuellvorgang erwartete Pulverartikel. Die Bestandsentscheidung wird jedoch ueber **alle aus der Lagerortliste gelesenen Zeilen** getroffen.

Moegliche Ergebnisse:

- `UNIQUE`: genau ein Bestand ungleich 0, Menge positiv, Einheit `KGM`, Artikel entspricht dem erwarteten Artikel
- `EMPTY`: auf dem Lagerort wurde in der vollstaendig gelesenen Liste kein Bestand ungleich 0 gefunden
- `WRONG_ARTICLE`: genau ein positiver Bestand, aber anderer Artikel; Pulverwechsel erforderlich
- `INVALID_STOCK`: negativer Bestand oder unerwartete Mengeneinheit; Klaerung erforderlich
- `AMBIGUOUS`: mehrere Bestaende ungleich 0; keine automatische Auswahl

Nur `UNIQUE` gibt den aktuellen Nachfuell-Prototyp frei.

Negative Bestaende werden bewusst nicht wie `0` ignoriert: Die bestaetigte Oxaion-Selektion lautet `<> 0`, daher muessen auch negative Werte als nicht normaler Maschinenzustand sichtbar bleiben und den Vorgang sperren.

## Vor-Buchungs-Revalidierung

Beim Oeffnen des Nachfuell-Prototyps liest das Frontend den Bestand automatisch und zeigt Charge plus gesamte Restmenge an. Die Felder fuer alte Mix-Charge und Restmenge sind nicht manuell editierbar.

Unmittelbar vor dem Senden liest das Frontend den Bestand erneut. Zusaetzlich fuehrt das Backend **vor dem Start der schreibenden Materialbuchung** eine eigene neue Bestandsabfrage durch.

Nur wenn Lagerort, Artikel, Charge und Menge noch mit dem Request uebereinstimmen, wird der bestehende reale Mix-Buchungsablauf gestartet. Bei Abweichung antwortet das Backend mit `CONFLICT` / `MACHINE_STOCK_VALIDATION` und startet keine Materialbuchung.

Diese Pruefung ersetzt noch keine spaetere transaktionale Sperr-/Concurrency-Strategie fuer gleichzeitig arbeitende Endgeraete. Dieser Punkt bleibt offen.

## Oxaion-Laufzeitbenutzer

Die Bestandsabfrage verwendet denselben Oxaion-Laufzeitbenutzer wie die restliche Backend-Session. Es gibt keinen fuer diese Funktion hart codierten Benutzer. `scripts/start-staging-published.ps1` und `scripts/start-staging.ps1` fragen den Benutzer beim Serverstart interaktiv ab; `appsettings.json` enthaelt fuer `Oxaion.User` keinen Vorgabewert.

## Noch live zu bestaetigen

Der aufgezeichnete interaktive Aufruf an `US30600J` enthielt die `SSID` des aufrufenden JET-Bildschirms; `US30600J` lieferte anschliessend eine neue `SSID` fuer `LB30230R`.

Das ASP.NET-Backend besitzt keinen JET-Elternbildschirm. Der STAGING-Prototyp sendet deshalb bei diesem rein lesenden Startaufruf `SSID` explizit leer und verlangt eine neue `SSID` in der Antwort.

**Dieser konkrete Backend-Start mit leerer `SSID` muss noch live in STAGING bestaetigt werden.** Bis dahin ist die Programmlogik aus dem Datenstrom bestaetigt, aber der serverseitige Einstieg noch nicht als produktiv freigegeben zu betrachten.

Keine schreibende Oxaion-Buchung wird ausgefuehrt, wenn die Bestandsabfrage fehlschlaegt oder die Liste nicht vollstaendig bis `<STOP/>` gelesen werden kann.
