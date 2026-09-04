# Mitarbeiter-UI Verfeinerungen 04.09.2026

Diese Datei ergaenzt `docs/WORKER_GUIDED_UI.md`. Sie beschreibt die nach dem realen Android-/STAGING-Test verbindlich festgelegten Bedienungsdetails. Die fachlichen Buchungs-, Idempotenz- und Recovery-Regeln aus `docs/ERROR_HANDLING.md` bleiben unveraendert.

## Live bestaetigter Multi-Position-Fall

Am 04.09.2026 wurde dieselbe reale Charge `52918` aus zwei unterschiedlichen Oxaion-Lagerplaetzen in einem Nachfuellvorgang verwendet.

Der Vorgang wurde erfolgreich als drei Oxaion-Positionen abgeschlossen: Maschinen-/Mix-Position plus zwei Nachfuellpositionen. Die Abschlusspruefung bestaetigte genau sechs erwartete LM/LN-Bewegungen und ein erfolgreiches explizites Schliessen des Belegs `FA26MB00042`.

Damit ist fuer den aktuellen Nachfuellprozess live bestaetigt:

- dieselbe Chargennummer darf mehrfach gescannt werden;
- die Eindeutigkeitsgrenze ist Lagerort + interner Lagerplatz + Charge;
- beim Folgescan wird eine bereits verwendete exakte Bestandsposition ausgeschlossen;
- die dynamische Position-3+-Buchungs- und Verifikationslogik funktioniert im getesteten STAGING-Fall.

Die separate manuelle Kontrolle, ob der nach der Abschlussverifikation explizit geschlossene Oxaion-Beleg unmittelbar im Oxaion-Dialog nicht mehr gesperrt ist, bleibt weiterhin offen.

## Soll-/Ist-Erkennungsfarben

Beim Chargenscan werden **immer** zwei visuelle Bereiche angezeigt:

- `SOLL`: EFA01/EFA02 des aus dem Maschinentank abgeleiteten Artikels;
- `IST / GESCANNT`: gescannter Artikel und Charge plus ein zweigeteiltes Farbkaestchen.

Bei einem korrekten Artikel zeigt die IST-Seite dieselben bereits aus Oxaion geladenen EFA01/EFA02-Erkennungsfarben. Bei einem falschen Artikel darf die PWA niemals die Sollfarben als vermeintliche Istfarben darstellen. Solange fuer den falschen Artikel kein separater Sachmerkmalsabruf vorliegt, wird das Ist-Farbkaestchen deshalb neutral als `nicht bekannt` dargestellt.

Die Farbe ist weiterhin nur Erkennungshilfe. Freigabe und Ablehnung basieren auf Artikel, Charge und aktuellem Oxaion-Bestand.

## Mengeneingabe ohne Fokusverlust

Solange ein Mengenfeld den Tastaturfokus hat, darf die automatische Schrittsteuerung den Scrollfokus nicht auf `Buchung starten` oder eine andere Aktion verschieben.

Das ist notwendig, damit Dezimalwerte wie `1,012` ohne Unterbrechung eingegeben werden koennen. Ein bereits positiver Zwischenwert wie `1` darf zwar fachlich als positive Zahl erkannt werden, beendet aber nicht automatisch die aktive Texteingabe.

Nach Verlassen des Mengenfeldes wird die normale Schrittsteuerung erneut ausgewertet.

## Buchungszusammenfassung

Die Mitarbeiteransicht in Schritt `4 · Buchen` zeigt fuer jede Nachfuellposition mindestens:

- Charge;
- Einfuellmenge;
- bestaetigten Oxaion-Lagerort;
- internen Lagerplatz, sofern vorhanden.

Damit kann der Mitarbeiter vor dem Start der Buchung die physische Entnahmequelle nochmals direkt gegen die erfasste Position pruefen.

## Seitendeckende Buchungsmeldung

Nach jedem Buchungsversuch wird das Ergebnis im Mitarbeitermodus als seitendeckende, blockierende Meldung dargestellt. Die Meldung muss bewusst bestaetigt werden und darf nicht durch einen automatischen Scrollsprung uebersehen werden.

### SUCCESS

`SUCCESS` darf nur nach der bestehenden vollstaendigen Backend-/Oxaion-Verifikation angezeigt werden.

Die Meldung bestaetigt eindeutig, dass die Pulvernachfuellung gebucht, verifiziert und abgeschlossen wurde. Erst nach `Verstanden` wird der abgeschlossene lokale Bedienvorgang geleert.

Die Mitarbeiter-Session bleibt bestehen. Der naechste Vorgang beginnt wieder mit `Maschinentank scannen`.

### Sicherer Vor-Buchungsfehler

Wenn das Backend eindeutig bestaetigt, dass keine Materialbuchung gestartet wurde, zeigt die PWA Ursache und konkrete Massnahme. Nach Bestaetigung darf der lokale vorbereitete Vorgang entsprechend der bestehenden Terminal-Logik geleert werden.

### REJECTED

Eine eindeutige Oxaion-Ablehnung bleibt ein serverseitig protokollierter Vorgang. Nach der seitendeckenden Meldung wird nicht blind neu gebucht. Die bestehende Recovery-/Neuversuch-Logik mit neuer `clientOperationId` und Bezug auf den abgelehnten Vorgang bleibt verbindlich.

### UNCERTAIN / MANUAL_REVIEW_REQUIRED / unklarer Verbindungsabbruch

Die Meldung muss plakativ `NICHT erneut buchen` enthalten. Der Vorgang bleibt erhalten und wird nicht geleert. Nach `Verstanden` wird auf die Recovery-/Statuspruefung gefuehrt.

Ein Verbindungsabbruch waehrend oder nach dem Versand wird niemals allein wegen einer fehlenden Browserantwort als `nicht gebucht` behandelt.

## Kamera-Zoom

Der gespeicherte Hardware-Zoom wird beim Kameraoeffnen nun angewendet, bevor der Kamerastream an das sichtbare Videoelement gebunden wird. Dadurch soll der zuvor beobachtete sichtbare Sprung vom Kamera-Standardzoom auf den gespeicherten Zoom vermieden beziehungsweise auf das vom Geraet technisch unvermeidbare Minimum reduziert werden.

Die Zoomstufe bleibt eine nicht-fachliche lokale Geraetepraeferenz. Sie beeinflusst keine Buchungsentscheidung.
