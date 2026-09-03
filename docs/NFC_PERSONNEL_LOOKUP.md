# NFC-Personalidentifikation über Syncos

## Ziel

Die vorhandene manuelle Oxaion-Personalsuche bleibt bestehen. Zusätzlich kann der Bediener in der Android-PWA über einen Button seinen Personal-NFC-Chip lesen. Bei erfolgreicher Zuordnung wird derselbe Mitarbeiterzustand gesetzt wie bei einer bewusst ausgewählten manuellen Personalsuche.

## Datenfluss

```text
Android PWA / Web NFC
  -> NDEFReadingEvent.serialNumber (Chip-UID)
  -> POST /api/personnel/nfc
  -> ASP.NET Core Backend
  -> Syncos STAGING SQL: RFID -> ObjectKey
  -> ObjectKey als Oxaion-Personalnummer normalisieren
  -> bestehende exakte Oxaion-Prüfung über IPENU
  -> PEPENU + PEPENA
  -> Mitarbeiter im Frontend automatisch auswählen
```

Das Frontend greift niemals direkt auf SQL oder Oxaion zu.

## Bestätigte Syncos-Zuordnung

Vom Benutzer am 03.09.2026 bestätigter Beispieldatensatz:

```text
RFID      ObjectKey    Name  Description       IsEnabled  IsVisible
54320466  0000000446   ANSA  Samitsch Andreas  -1         -1
```

Verbindliche Zuordnung für diesen Ablauf:

- `RFID`: numerische Chip-/RFID-ID in Syncos.
- `ObjectKey`: zehnstellig mit führenden Nullen gespeicherte Personalnummer.
- Beispiel: `0000000446` wird für Oxaion zu `446` normalisiert.
- `Name` und `Description` aus Syncos sind nur Diagnose-/Kontextfelder. Sie ersetzen nicht den kanonischen Oxaion-Namen `PEPENA`.
- Es werden ausschließlich aktive und sichtbare Datensätze der `ClassID = 47` akzeptiert.

Verwendete lesende STAGING-Abfrage:

```sql
SELECT TOP (2)
    t0.ObjectKey,
    t0.Name,
    t0.Description
FROM syncos_stg_102.ITSDEV.ITSUSER t0
WHERE t0.ClassID = 47
  AND t0.IsEnabled = -1
  AND t0.IsVisible = -1
  AND t0.RFID = @rfid
ORDER BY t0.ObjectKey
```

`@rfid` wird parametrisiert übergeben. Es gibt keine SQL-Stringverkettung mit Chipwerten.

## NFC-UID und RFID-Zahl

Web NFC liefert die Chip-Seriennummer/UID als Hex-Bytes, üblicherweise durch `:` getrennt. Das Backend interpretiert diese Bytes in derselben Reihenfolge als Hex-Zahl und verwendet deren Dezimaldarstellung für den Vergleich mit `ITSUSER.RFID`.

Regressionstest:

```text
03:3C:DD:52 -> 0x033CDD52 -> 54320466
```

Für API-/Diagnosetests akzeptiert das Backend zusätzlich bereits dezimal übergebene RFID-Werte.

Die reale UID-Darstellung der vorhandenen FAM-Personalchips ist im nächsten Android-STAGING-Test zu bestätigen. Es wird keine alternative Byte-Reihenfolge geraten. Falls der gelesene Web-NFC-Wert nicht zur Syncos-RFID passt, wird die konkrete UID angezeigt und die Zuordnung bleibt gesperrt, bis die tatsächliche Kodierung geklärt ist.

## Sicherheitskette

Ein Treffer in Syncos allein bestätigt den Mitarbeiter noch nicht.

1. RFID muss genau einen aktiven und sichtbaren Syncos-Datensatz der `ClassID = 47` liefern.
2. `ObjectKey` muss rein numerisch sein.
3. Führende Nullen werden entfernt und ergeben die Personalnummer für Oxaion.
4. Das Backend ruft die bereits bestätigte exakte Oxaion-Personalprüfung über `IPENU` auf.
5. Erst wenn Oxaion genau einen passenden `PEPENU`/`PEPENA`-Datensatz liefert, erhält das Frontend `personnelNo` und `fullName`.
6. Unmittelbar vor einer Materialbuchung bleibt die bereits bestehende erneute Oxaion-Personalprüfung unverändert bestehen.

Damit ist NFC nur eine schnellere Identifikation, keine Umgehung der Oxaion-Sicherheitsprüfung.

## Web-NFC-Grenzen

Die PWA verwendet `NDEFReader` und `NDEFReadingEvent.serialNumber`.

Verbindlich für den Testbetrieb:

- Web NFC benötigt einen sicheren Browserkontext (`HTTPS`).
- Der Scan wird ausschließlich durch eine bewusste Benutzeraktion über den Button `NFC-Chip lesen` gestartet.
- Die manuelle Personalnummernsuche bleibt als Fallback bestehen.
- Liefert der Browser keine Seriennummer, wird niemand automatisch ausgewählt.
- Unterstützt der verwendete Personalchip Web NFC/NDEF nicht, wird dies als technischer NFC-Lesefehler angezeigt; es wird keine niedrigere NFC/RFID-Protokollebene im Browser erfunden.

## Backend-Konfiguration

Der SQL-Connection-String ist ein Secret bzw. Laufzeitwert und steht nicht im Repository und nicht im Frontend.

Konfigurationsschlüssel:

```text
Syncos__ConnectionString
```

Das STAGING-Startskript fragt den Connection-String verdeckt ab, falls die Umgebungsvariable noch nicht gesetzt ist.

## Noch live zu bestätigen

- vorhandene FAM-Personalchips sind auf dem eingesetzten Android-/Browser-Setup über Web NFC lesbar;
- gelieferte `serialNumber` wird in der erwarteten Byte-Reihenfolge zur Syncos-Spalte `RFID` abgebildet;
- Beispiel `RFID 54320466 -> ObjectKey 0000000446 -> Oxaion 446` funktioniert Ende-zu-Ende;
- automatische Auswahl im Frontend ersetzt korrekt eine eventuell vorherige manuelle Auswahl;
- Verhalten bei unbekannter RFID, doppelter RFID, deaktiviertem/unsichtbarem Datensatz, SQL-Ausfall und Oxaion-Prüffehler ist im STAGING nachvollziehbar.
