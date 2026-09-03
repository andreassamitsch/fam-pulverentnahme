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

Der QR enthält ausschließlich den Oxaion-Tanklagerort.

Verbindlicher aktueller STAGING-Ablauf:

1. Bediener drückt `Maschinentank QR scannen`.
2. Die PWA liest einen QR-Code über die Kamera.
3. Codes mit `+++` werden in diesem Schritt abgelehnt.
4. Der gelesene Wert muss exakt einem Eintrag der gepflegten Maschinen-/Tankliste entsprechen, aktuell beispielsweise `EOS1` oder `EOS2`.
5. Erst danach liest das Backend den aktuellen Tankbestand aus Oxaion.
6. Artikel, Artikelbezeichnung, aktuelle Mix-Charge und kompletter Tankbestand werden aus Oxaion übernommen und bleiben nicht editierbar.

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
2. Der Bediener geht zur physischen Nachfüllcharge und scannt ausschließlich deren Chargen-QR.
3. Die Artikelnummer aus dem QR muss dem aus dem Maschinentank abgeleiteten Artikel entsprechen. Bei Abweichung wird die Charge gesperrt.
4. Die PWA verwendet die bereits bestätigten lesenden Oxaion-Auskunftswege, um alle positiven Quell-Lagerorte zum Artikel zu bestimmen.
5. Für diese Lagerorte werden die exakten positiven Lagerplatz-/Chargenpositionen gelesen.
6. Es werden ausschließlich Positionen mit der gescannten Charge berücksichtigt. Der Maschinentank selbst ist als Quelle ausgeschlossen.
7. Genau ein Treffer: Lagerort und gegebenenfalls interner Lagerplatz werden automatisch übernommen.
8. Mehrere Treffer: Der Bediener muss den tatsächlich verwendeten Entnahmeort bewusst bestätigen. Bei mehreren Lagerplätzen gilt dieselbe Regel.
9. Kein Treffer oder nicht vollständig lesbarer Oxaion-Bestand: keine automatische Zuordnung und keine Buchungsfreigabe.
10. Die Einfüllmenge bleibt eine Bedienereingabe und darf den aktuell verfügbaren Bestand der bestätigten Position nicht überschreiten.
11. Unmittelbar vor der ersten schreibenden Oxaion-Buchung validiert das Backend die gewählte Bestandsposition erneut anhand von Artikel, Lagerort, internem Lagerplatz, Charge und verfügbarer Menge.

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

## Scan-Sicherheit

Die Scan-Schritte akzeptieren bewusst unterschiedliche Codeformen:

- Maschinentank: nur einzelner Wert ohne `+++`.
- Nachfüllcharge: exakt zwei Teile `Artikel+++Charge`.
- FA: drei Teile.
- alter Entnahmeschein: vier Teile.

Dadurch wird beispielsweise ein Fertigungsauftrag oder Entnahmeschein im Chargen-Schritt nicht stillschweigend als Charge akzeptiert.

Kamera- und NFC-Funktionen benötigen auf Android einen sicheren Browserkontext. Für den vorgesehenen PWA-Betrieb gilt deshalb weiterhin HTTPS.
