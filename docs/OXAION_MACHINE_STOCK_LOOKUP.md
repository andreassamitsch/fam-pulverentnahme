# Oxaion-Maschinenbestand: Chargen pro Lagerort

## Zweck

Diese Datei dokumentiert die am 01.09.2026 im Oxaion-JET-Datenstrom beobachtete, rein lesende Bestandsabfrage fuer den aktuellen Pulverbestand eines Maschinen-Lagerorts.

Der konkrete Referenzfall war:

- Firma `103`
- Artikel `RP.00010`
- Artikelbezeichnung `AlSi10Mg`
- Maschinen-Lagerort `EOS1`
- Lagerortbezeichnung `EOS 1 -Tank`

Die Abfrage dient im Prozess **Pulver nachfuellen** dazu, die alte Mix-Charge und deren vollstaendigen positiven Oxaion-Bestand automatisch zu ermitteln. Diese Werte werden nicht mehr manuell vorgegeben.

## Technisch im Datenstrom bestaetigte Folge

Der aufgezeichnete JET-Ablauf lautet:

1. `US30600J` mit leerer Aktion und Startkontext fuer Artikel/Lagerort
2. `LB30230R *GETHDR`
3. `LB30230R *FIRSTLIST` mit `mode=reset`
4. `LB30230 *GETFILTER`
5. Filteroption `SET=mit Bestand` aus der Antwort ermitteln
6. `LB30230 *LOADSET` mit der von Oxaion gelieferten Filter-ID (`KIDN`) sowie `FLTY`, `TYPE` und `NEW_MODE=J`
7. `LB30230R *GETU01`
8. `LB30230R *FIRSTLIST` mit `mode=replace`

Die Filter-ID wird im Backend **nicht hart codiert**. Der Prototyp sucht in der Antwort von `LB30230 *GETFILTER` nach `SET=mit Bestand` und verwendet die zugehoerigen, von Oxaion gelieferten Schluesselwerte.

## Relevante Rueckgabefelder

Die gefilterte `FIRSTLIST`-Tabelle liefert unter anderem:

- `KEY/LALAGO`: Lagerort
- `KEY/LAIDNR`: Artikel
- `KEY/LAPONR`: Charge
- `IDNR.TLBEZG`: Artikelbezeichnung
- `PONR.POCHNL`: Herstellercharge, soweit vorhanden
- `LLAWEP.LALABE`: Lagerbestand inklusive Mengeneinheit, zum Beispiel `164,330 KGM`
- `LLAWEP.LALADM`: Lagerwert
- `LLAWEP.LAYZLBU`: Datum/Uhrzeit der letzten Lagerbuchung
- `LLAGEP.LAYEAN`: Erstanlagedatum

Im aufgezeichneten Referenzfall lieferte der Filter `mit Bestand` genau eine positive Zeile:

```text
Lagerort: EOS1
Artikel:  RP.00010
Charge:   RP10WEB_20260901_085443
Bestand:  164,330 KGM
```

## Backend-API

Der STAGING-Prototyp stellt dafuer bereit:

```text
GET /api/machine-stock?warehouse=EOS1&article=RP.00010&warehouseText=EOS%201%20-Tank&articleText=AlSi10Mg
```

Moegliche fachliche Ergebnisse der **artikelbezogenen** Abfrage:

- `UNIQUE`: genau eine positive Charge fuer den erwarteten Artikel auf dem Lagerort
- `NO_STOCK_FOR_ARTICLE`: kein positiver Bestand fuer den erwarteten Artikel gefunden
- `AMBIGUOUS`: mehrere positive Chargen fuer den erwarteten Artikel gefunden

Nur `UNIQUE` gibt den aktuellen Nachfuell-Prototyp frei.

## Sicherheitsgrenze: artikelbezogene Abfrage

Der aktuell aufgezeichnete Datenstrom wurde mit `TIDF=RP.00010` ausgefuehrt und ist damit artikelbezogen.

Deshalb gilt verbindlich:

- `NO_STOCK_FOR_ARTICLE` darf **nicht** als sicherer Nachweis interpretiert werden, dass die Maschine leer ist.
- Es koennte ein anderer Artikel auf dem Maschinen-Lagerort liegen.
- Bis eine artikelunabhaengige Abfrage des gesamten positiven Maschinenbestands technisch bestaetigt ist, wird dieser Fall gestoppt und geklaert.

Damit wird die bereits festgelegte Regel eingehalten, dass die App unterschiedliches Pulver niemals aufgrund einer Annahme vermischen darf.

## Vor-Buchungs-Revalidierung

Beim Oeffnen des Nachfuell-Prototyps liest das Frontend den Bestand automatisch und zeigt Charge plus gesamte Restmenge an. Die Felder fuer alte Mix-Charge und Restmenge sind nicht mehr manuell editierbar.

Unmittelbar vor dem Senden liest das Frontend den Bestand erneut. Zusaetzlich fuehrt das Backend **vor dem Start der schreibenden Materialbuchung** eine eigene neue Bestandsabfrage durch.

Nur wenn Lagerort, Artikel, Charge und Menge noch mit dem Request uebereinstimmen, wird der bestehende reale Mix-Buchungsablauf gestartet. Bei Abweichung antwortet das Backend mit `CONFLICT` / `MACHINE_STOCK_VALIDATION` und startet keine Materialbuchung.

Diese Pruefung ersetzt noch keine spaetere transaktionale Sperr-/Concurrency-Strategie fuer gleichzeitig arbeitende Endgeraete. Dieser Punkt bleibt offen.

## Noch live zu bestaetigen

Der aufgezeichnete interaktive Aufruf an `US30600J` enthielt die `SSID` des aufrufenden JET-Bildschirms; `US30600J` lieferte anschliessend eine neue `SSID` fuer `LB30230R`.

Das ASP.NET-Backend besitzt keinen JET-Elternbildschirm. Der STAGING-Prototyp sendet deshalb bei diesem rein lesenden Startaufruf `SSID` explizit leer und verlangt eine neue `SSID` in der Antwort.

**Dieser konkrete Backend-Start mit leerer `SSID` muss noch live in STAGING bestaetigt werden.** Bis dahin ist die Programmlogik aus dem Datenstrom bestaetigt, aber der serverseitige Einstieg noch nicht als produktiv freigegeben zu betrachten.

Keine schreibende Oxaion-Buchung wird ausgefuehrt, wenn diese Bestandsabfrage fehlschlaegt.
