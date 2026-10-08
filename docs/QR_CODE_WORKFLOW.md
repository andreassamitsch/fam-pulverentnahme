# QR-Code-Formate und Scan-Ablauf

Stand: 03.09.2026

## Ziel

Die mobile PWA soll im produktionsnahen Ablauf möglichst wenig manuelle Auswahl verlangen. QR-Codes identifizieren deshalb den Maschinentank und die tatsächlich verwendete Nachfüllcharge. Oxaion bleibt für Artikel, Bestände, Lagerorte, Lagerplätze und die Buchungsvalidierung führend.

Der Kameraaufbau basiert technisch auf dem erprobten Aufbau der früheren Offline-HTML: native Android-Kamera über `getUserMedia`, `BarcodeDetector`, sichtbarer Kamerabereich, Zoom über die Kamerafähigkeiten und browsergenerierte Tonsignale. Es werden keine externen Scannerbibliotheken benötigt.

## 1. Maschinentank-QR

Aktuelles Format:

```text
EOS1
```

Der QR enthält ausschließlich den Oxaion-Tanklagerort. Welche Lagerorte Maschinentanks sind, wird dynamisch aus Oxaion `ULGSTP` über `LGFIRM = aktive Firma` und `LGLGART = '02'` ermittelt.

Verbindlicher aktueller STAGING-Ablauf:

1. Bediener drückt `Maschinentank QR scannen`.
2. Die PWA öffnet das Kamerabild, startet aber noch keine QR-Erkennung.
3. Der Bediener kann das Kamerabild ausrichten und den Zoom einstellen.
4. Erst nach bewusstem Druck auf `Scannen` wird `BarcodeDetector` zyklisch ausgeführt und der rote Scan-Laser eingeblendet.
5. Codes mit `+++` werden in diesem Schritt abgelehnt.
6. Vor der Validierung lädt die PWA die aktuelle Tankliste aus dem Backend neu. Der gelesene Wert muss einem Oxaion-Lagerort der aktiven Firma entsprechen, der in `ULGSTP` mit `LGLGART = '02'` definiert ist. Es gibt keine statische Tankliste in der PWA.
7. Erst danach liest das Backend den aktuellen Tankbestand aus Oxaion.
8. Artikel, Artikelbezeichnung, aktuelle Mix-Charge und kompletter Tankbestand werden aus Oxaion übernommen und bleiben nicht editierbar.

Die bisher sichtbare manuelle Auswahl des Tanklagerorts ist damit im normalen Nachfüllablauf ersetzt.

## 2. Chargen-QR

Aktuelles Format:

```text
RP.00006+++88688
```

oder beispielsweise für eine Mix-Charge:

```text
RP.00010+++RP00010MIX_20260903_132212
```

Bedeutung:

```text
Artikel+++Charge
```

Verbindlicher aktueller STAGING-Ablauf:

1. Der Maschinentank wurde bereits gescannt und sein eindeutiger Oxaion-Bestand wurde gelesen.
2. Der Bediener geht zur physischen Nachfüllcharge und öffnet den Chargen-Scanner.
3. Auch hier öffnet die PWA zunächst nur das Kamerabild. Die QR-Erkennung startet erst nach bewusstem Druck auf `Scannen`.
4. Die Artikelnummer aus dem QR muss dem aus dem Maschinentank abgeleiteten Artikel entsprechen. Bei Abweichung wird die Charge gesperrt.
5. Die PWA verwendet die bereits bestätigten lesenden Oxaion-Auskunftswege, um alle positiven Quell-Lagerorte zum Artikel zu bestimmen.
6. Für diese Lagerorte werden die exakten positiven Lagerplatz-/Chargenpositionen gelesen.
7. Es werden ausschließlich Positionen mit der gescannten Charge berücksichtigt. Der Maschinentank selbst ist als Quelle ausgeschlossen.
8. Genau ein Treffer: Lagerort und gegebenenfalls interner Lagerplatz werden automatisch übernommen.
9. Mehrere Treffer: Der Bediener muss den tatsächlich verwendeten Entnahmeort bewusst bestätigen. Bei mehreren Lagerplätzen gilt dieselbe Regel.
10. Kein Treffer oder nicht vollständig lesbarer Oxaion-Bestand: keine automatische Zuordnung und keine Buchungsfreigabe.
11. Die Einfüllmenge bleibt eine Bedienereingabe und darf den aktuell verfügbaren Bestand der bestätigten Position nicht überschreiten.
12. Unmittelbar vor der ersten schreibenden Oxaion-Buchung validiert das Backend die gewählte Bestandsposition erneut anhand von Artikel, Lagerort, internem Lagerplatz, Charge und verfügbarer Menge.

Damit ersetzt der Chargen-Scan die bisherige Bedienfolge `Lagerort auswählen -> Bestandsposition auswählen`. Die Buchungsschlüssel selbst stammen weiterhin ausschließlich aus Oxaion.

## 3. Fertigungsauftrag-QR

Aktuelles vorhandenes Format:

```text
RP.00010+++FA24FI00118+++EP-M650-1
```

Bedeutung:

```text
Rohmaterialartikel+++Fertigungsauftrag+++Maschinen-ID
```

`EP-M650-1` ist die Produktionsmaschinen-ID und **nicht** der Oxaion-Lagerort des Maschinentanks.

Noch offen ist die verbindliche Referenz zwischen:

- Produktionsmaschinen-ID, z. B. `EP-M650-1`
- tatsächlichem Maschinentank-/Oxaion-Lagerort, z. B. `EOS1`

Dabei muss weiterhin möglich sein, dass die Produktionsmaschine kurzfristig gegenüber der ursprünglichen FA-Planung geändert wird. Bis diese Referenz fachlich und technisch festgelegt ist, wird keine automatische Zuordnung erfunden.

Mögliche später zu prüfende Datenquelle ist der Soll-Entnahmelagerort der Materialposition im Fertigungsauftrag. Ob dieser Wert für kurzfristige Maschinenänderungen geeignet und sicher aktualisierbar ist, ist noch nicht bestätigt.

## 4. Entnahmeschein-QR

Bisheriges Offline-Format:

```text
RP.00010+++EOS1+++87911+++RP00010MIX_20260903_132212
```

Dieser Code entstand für den ursprünglichen Offline-Ablauf und enthält bereits Tanklagerort sowie teilweise vorgegebene Chargeninformationen.

Für die aktuelle online mit Oxaion verbundene PWA ist noch festzulegen, wie Produktionsleitung beziehungsweise Stellvertretung eine Tanknachfüllung oder einen Tankwechsel beauftragt.

Bis zu dieser Entscheidung gilt als Übergangsprozess:

- Beauftragung mündlich beziehungsweise nach dem bestehenden organisatorischen Übergangsprozess.
- Maschinentank wird in der PWA physisch per Tank-QR identifiziert.
- Tatsächlich entnommene Charge wird physisch per Chargen-QR identifiziert.
- Lagerort/Lagerplatz werden aus dem aktuellen Oxaion-Bestand abgeleitet und nicht aus einem alten Offline-Auftrag als Buchungswahrheit übernommen.

Der Entnahmeschein-QR wird daher aktuell nicht als verbindlicher Startcode des neuen Online-Nachfüllprozesses verwendet.

## Scanner-Bedienung und Fehlscan-Schutz

Die Kamera und die eigentliche QR-Erkennung sind bewusst getrennt:

1. Ein Prozessbutton wie `Maschinentank QR scannen` oder `Chargen QR scannen` öffnet nur den Scanner-Dialog und startet die Kamera.
2. Der rote Scan-Laser bleibt aus und `BarcodeDetector.detect(...)` wird noch nicht ausgeführt.
3. Der Bediener richtet das Smartphone aus und kann `+` / `−` für den optischen Kamera-Zoom verwenden, sofern das Gerät Zoom unterstützt.
4. Erst der zusätzliche Button `Scannen` aktiviert die laufende QR-Erkennung.
5. Während aktiver Erkennung zeigt der Button `Scannen stoppen`; damit kann die Erkennung wieder pausiert werden, ohne die Kamera zu schließen.
6. Bei erfolgreicher QR-Erkennung erzeugt die PWA ein kurzes browsergeneriertes Tonsignal und eine kurze Vibration.
7. Danach wird der Scanner geschlossen und der gelesene Code an den jeweiligen Prozessschritt übergeben.

Diese Bedienung entspricht bewusst dem erprobten Verhalten der früheren Offline-Version und reduziert Fehlscans beim Öffnen, Ausrichten oder Zoomen der Kamera.

## Scan-Sicherheit

Die Scan-Schritte akzeptieren bewusst unterschiedliche Codeformen:

- Maschinentank: nur einzelner Wert ohne `+++`.
- Nachfüllcharge: exakt zwei Teile `Artikel+++Charge`.
- FA: drei Teile.
- alter Entnahmeschein: vier Teile.

Dadurch wird beispielsweise ein Fertigungsauftrag oder Entnahmeschein im Chargen-Schritt nicht stillschweigend als Charge akzeptiert.

Kamera- und NFC-Funktionen benötigen auf Android einen sicheren Browserkontext. Für den vorgesehenen PWA-Betrieb gilt deshalb weiterhin HTTPS.

## 5. Chargenherkunft als reine Auskunft (0.1.11)

Der eigenstaendige Vorgang `Chargenherkunft anzeigen` verwendet denselben bestehenden Kamera-Scanner und exakt das bereits dokumentierte Chargenetikett-Format `Artikel+++Charge`:

```text
RP.00010+++84671
PB.00001+++KUNDENCHARGE
```

Ein Fertigungsauftrag-QR mit drei Teilen und ein historischer Entnahmeschein-QR mit vier Teilen werden in diesem Auskunftsschritt abgelehnt. Die Kamera startet wie gewohnt ohne sofort aktive Erkennung; der Bediener stellt den Zoom ein und startet bewusst `Scannen`. Manuelle Artikel-/Chargeneingabe ist gleichwertig verfuegbar.

Der Auskunftsschritt fragt ausschliesslich `GET /api/charge-origin` ab und erzeugt keine Materialbuchung. Er funktioniert nur mit Oxaion-Verbindung. Bei der Chargenherkunft aus den Maschinentank-/Lagerplatzdetails ist kein erneuter Scan noetig.

