# FAM Pulverentnahme - Bedienerhandbuch

Stand: 02.10.2026

## Zielgruppe

Dieses Handbuch richtet sich an Produktionsmitarbeiter, Application Engineers, Produktionsleitung und andere berechtigte Bediener der FAM-Pulverentnahme.

Technische Server-/Admin-Themen stehen in `docs/ADMIN_GUIDE.md`.

## 1. Was die App macht

Die App unterstuetzt aktuell folgende Vorgaenge:

1. Tank nachfuellen
2. Pulververbrauch erfassen
3. Tank entleeren
4. Leeren Tank befuellen
5. Jobabbruch. Verbrauch korrigieren
6. Bestaende anzeigen
7. Etiketten nachdrucken

Die App liest aktuelle Daten aus Oxaion und prueft vor einer Buchung den Zustand nochmals serverseitig.

## 2. App starten und Umgebung pruefen

Die App wird auf dem Android-Smartphone als WebApp/PWA verwendet.

In der Kopfzeile wird die aktive Umgebung angezeigt:

- `STG` = Testumgebung / STAGING
- `PROD` = Produktivumgebung

Vor produktiven Arbeiten immer kontrollieren, ob die richtige Umgebung angezeigt wird.

Nach einer serverseitigen Umschaltung zwischen STAGING und PRODUCTION wird die bestehende Anmeldung verworfen. Danach ist eine neue Anmeldung notwendig.

## 3. Verbindungsanzeige

Die Kopfzeile zeigt den aktuellen Verbindungszustand.

Moegliche typische Anzeigen:

- Verbindung wird geprueft
- Backend erreichbar
- Oxaion-Verbindung aktiv
- Oxaion offline
- Offline

Eine gruen dargestellte Verbindung bedeutet nur, dass Backend und Oxaion erreichbar sind. Die eigentliche Buchungsfreigabe erfolgt erst nach der fachlichen Pruefung unmittelbar vor der Buchung.

Bei `Offline` oder `Oxaion offline` keine produktive Buchung als erfolgreich annehmen.

## 4. Anmeldung

Bevor ein Buchungsvorgang gestartet wird, muss ein Mitarbeiter angemeldet sein.

Moeglichkeiten:

- `Mit NFC anmelden` und Personalchip verwenden;
- alternativ `Ohne NFC anmelden` und Personalnummer plus Passwort eingeben.

Nach erfolgreicher Anmeldung wird der Mitarbeitername kompakt in der Kopfzeile angezeigt.

Wenn fuer die am Server eingestellte Zeit keine Bedienereingabe erfolgt, wird der Mitarbeiter automatisch abgemeldet. Die App zeigt dann `Wegen Inaktivität automatisch abgemeldet` und verlangt eine erneute Anmeldung per NFC oder Passwort. Automatische Hintergrundabfragen halten die Anmeldung nicht kuenstlich aktiv.

Auf einer geoeffneten Vorgangsseite ist die Abmeldung gesperrt. Zuerst zur Vorgangsuebersicht zurueckgehen und dort abmelden.

## 5. QR-Scanner bedienen

Der Scanner startet in zwei Schritten:

1. Scanner oeffnen.
2. Kamera ausrichten und bei Bedarf Zoom verwenden.
3. Erst mit `Scannen` die eigentliche QR-Erkennung aktivieren.

Das verhindert Fehlscans beim Oeffnen oder Ausrichten der Kamera.

### Tank-QR

Der Tank-QR enthaelt nur den Oxaion-Tanklagerort, zum Beispiel:

`EOS1`

Die App prueft den Code gegen die aktuell in Oxaion definierten Tanklagerorte. Es gibt keine statische Tankliste in der App.

### Chargen-QR

Format:

`Artikel+++Charge`

Beispiel:

`RP.00010+++87911`

Bei Kundenbeistellpulver entsprechend beispielsweise `PB.00001+++<Charge>`.

Die App prueft Artikel, Charge, Lagerort/Lagerplatz und aktuellen Bestand gegen Oxaion.

### Fertigungsauftrag-QR

Format:

`Rohmaterial+++Fertigungsauftrag+++Maschinen-ID`

Beispiel:

`RP.00010+++FA24FI00118+++EP-M650-1`

Die Maschinen-ID im Fertigungsauftrag-QR ist nicht automatisch identisch mit dem Oxaion-Tanklagerort.

## Sichtbare Vorgangsbezeichnungen

Die Mitarbeiteransicht verwendet ab Version `0.1.8` bewusst kurze, handlungsorientierte Bezeichnungen:

- `Tank nachfuellen` — vorhandenes Pulver im Tank ergaenzen.
- `Pulververbrauch erfassen` — verbrauchtes Pulver einem Fertigungsauftrag zuordnen.
- `Tank entleeren` — Pulver vollstaendig aus dem Tank ins Pulverlager zurueckgeben.
- `Leeren Tank befuellen` — leeren Tank mit neuem Pulver befuellen.
- `Jobabbruch. Verbrauch korrigieren` — Pulververbrauch nach einem abgebrochenen Druckjob berichtigen.
- `Bestaende anzeigen` — aktuelle Tank- und Pulverlagerbestaende anzeigen.
- `Etiketten nachdrucken` — Etiketten einer abgeschlossenen Tankauslagerung erneut drucken.

Technische Buchungsbegriffe bleiben aus der Vorgangsauswahl heraus, sofern sie fuer die Bedienentscheidung nicht erforderlich sind.

## 6. Tank nachfuellen

Dieser Vorgang wird verwendet, wenn sich bereits Pulver im Tank befindet und weiteres Pulver desselben Artikels nachgefuellt wird.

Bedienablauf:

1. `Tank nachfuellen` waehlen.
2. Maschinentank scannen.
3. Die App liest Artikel, aktuelle Mix-Charge und Tankbestand aus Oxaion.
4. Nachfuellcharge scannen.
5. Die App ermittelt den passenden Oxaion-Entnahmeort beziehungsweise Lagerplatz.
6. Bei mehreren moeglichen Positionen die tatsaechlich verwendete Position bewusst auswaehlen.
7. Einfuellmenge eingeben.
8. Zusammenfassung pruefen.
9. Buchung bewusst bestaetigen.

Die App validiert Tank und Quellbestand direkt vor der Buchung erneut.

Wenn der Tank laut Oxaion eindeutig leer ist, zeigt die App `Tank ist leer.`. In diesem Fall `Tank nachfuellen` nicht verwenden, sondern `Leeren Tank befuellen` waehlen.

## 7. Pulververbrauch erfassen

Dieser Vorgang bucht den tatsaechlichen kumulierten Pulververbrauch auf einen Fertigungsauftrag.

Bedienablauf:

1. `Pulververbrauch erfassen` waehlen.
2. Maschinentank scannen.
3. Fertigungsauftrag-QR scannen.
4. Die App prueft, ob der Rohmaterialartikel des Auftrags zum Tankartikel passt.
5. Die App zeigt Sollverbrauch, bereits gebuchten Ist-Verbrauch und Materialstatus.
6. `Pulver Verbrauch eingeben` pruefen und den tatsaechlichen kumulierten Ist-Verbrauch eintragen.
7. Die App berechnet die neu zu buchende Differenz.
8. Zusammenfassung pruefen.
9. Buchung bestaetigen.

Wichtig:

- Der eingegebene Ist-Verbrauch ist der kumulierte Gesamtverbrauch, nicht nur die neue Differenz.
- Die neu zu buchende Differenz muss positiv sein.
- Die Differenz darf nicht groesser als der aktuelle Tankbestand sein.
- Wenn Artikel, Materialposition oder Tankzustand nicht mehr passen, stoppt die App vor der Buchung.

## 8. Tank entleeren

Dieser Vorgang wird verwendet, wenn Pulver physisch aus einem Tank entnommen und auf einen Lagerort/Lagerplatz zurueckgelagert wird.

Bedienablauf:

1. `Tank entleeren` waehlen.
2. Tank scannen.
3. Die App zeigt Artikel, Mix-Charge und Systemmenge.
4. Pulver physisch aus dem Tank entnehmen und Netto-Pulvermenge wiegen.
5. Gewogene Menge eingeben und mit Enter beziehungsweise Feldwechsel bestaetigen.
6. Systemmenge, gewogene Menge und Differenz kontrollieren.
7. Ziel-Lagerort und Ziel-Lagerplatz aus der App-Auswahl waehlen.
8. Zusammenfassung pruefen.
9. Buchung bestaetigen.

Wenn Systembestand und gewogene Menge voneinander abweichen, korrigiert der Backend-Prozess den Tankbestand nach der bestaetigten Korrekturlogik und liest ihn danach erneut. Erst wenn der Tankbestand exakt zur physisch bestaetigten Menge passt, wird die Auslagerung fortgesetzt.

Nach eindeutig erfolgreicher Auslagerung kann die App fragen:

`Etiketten drucken?`

Bei `Ja` die gewuenschte positive ganze Etikettenanzahl eingeben und bestaetigen.

Ein Druckfehler macht die erfolgreiche Materialauslagerung nicht rueckgaengig.

## 9. Leeren Tank befuellen

Dieser Vorgang ist nur fuer einen Tank vorgesehen, den Oxaion eindeutig als leer bestaetigt.

Bedienablauf:

1. `Leeren Tank befuellen` waehlen.
2. Leeren Tank scannen.
3. Erste Pulvercharge scannen.
4. Die erste gueltige Charge bestimmt den Tankartikel.
5. Weitere Chargen muessen denselben Artikel haben.
6. Die App ermittelt die aktuelle Oxaion-Bestandsposition.
7. Menge pruefen beziehungsweise anpassen.
8. Bei Bedarf weitere Quellen hinzufuegen.
9. Zusammenfassung pruefen.
10. Buchung bestaetigen.

Die App entscheidet anhand der finalen Quellenliste, ob eine vorhandene Mix-Charge erhalten bleibt oder eine neue Mix-Charge erzeugt wird.

Bei einer gescannten Charge mit falschem Artikel wird diese Charge verworfen. Bereits korrekt erfasste Daten bleiben erhalten.

## 10. Jobabbruch. Verbrauch korrigieren

Dieser Vorgang ist fuer einen bereits gebuchten Pulververbrauch vorgesehen, wenn der Druckjob abbricht und der tatsaechliche Verbrauch kleiner ist als die urspruenglich gebuchte Menge.

Bedienablauf:

1. `Jobabbruch. Verbrauch korrigieren` waehlen.
2. Fertigungsauftrag scannen.
3. Die App sucht die eindeutige urspruengliche Oxaion-Materialrueckmeldung.
4. Tanklager und Mix-Charge werden automatisch aus dieser Originalrueckmeldung abgeleitet.
5. Der Bediener scannt den Tank in diesem Prozess nicht mehr manuell.
6. Die App prueft, ob Tank, Artikel und Mix-Charge noch zum Originalvorgang passen.
7. Urspruenglich gebuchten Verbrauch und aktuellen Zustand pruefen.
8. Tatsaechlichen Ist-Verbrauch eingeben.
9. Korrektur bestaetigen.

Die App storniert zuerst die urspruengliche Rueckmeldung und bucht erst danach den korrigierten Ist-Verbrauch neu.

Wenn der Stornoausgang unklar ist, wird die zweite Buchung nicht gestartet.

## 11. Bestaende anzeigen

Die Lagerliste selbst wird weiterhin rein lesend aus Oxaion angezeigt. Eine Pulverlagerposition kann angetippt werden, um zuerst `Lagerplatzdetails` anzuzeigen. Erst in diesem Detailfenster kann bei einer zulaessigen positiven Position mit Lagerplatz bewusst `Umlagern` gestartet werden; erst dieser Vorgang fuehrt eine Materialbuchung ueber Oxaion aus.

Die Ansicht zeigt:

- oben alle aktuell in Oxaion definierten Maschinentanks;
- darunter die RP.*-Pulverlagerbestaende.

Bei Maschinentanks werden je nach Zustand Artikel, Charge, Menge und gegebenenfalls Erkennungsfarben angezeigt.

Im Pulverlager werden pro Position angezeigt:

- Lagerort / Lagerplatz
- Charge
- Menge

### Pulver von Lagerplatz zu Lagerplatz umlagern

1. Gewuenschte Pulverlagerposition antippen.
2. Im Popup `Lagerplatzdetails` Artikel, Bezeichnung, Lagerort, Lagerplatz, Charge und Bestand kontrollieren.
3. Wenn die Position umlagerbar ist, `Umlagern` tippen. Bei Negativbestand, fehlendem Lagerplatz oder nicht freigegebener Einheit wird der Button nicht angeboten und stattdessen ein Hinweis angezeigt.
4. Quelle, Artikel und Charge bleiben aus der Lagerposition fest vorgegeben und koennen nicht geaendert werden.
5. Die App schlaegt die **volle aktuell angezeigte Menge** vor. Bei einer Teilumlagerung die Menge reduzieren.
6. Ziellagerort auswaehlen. Der aktuelle Lagerort wird nach Moeglichkeit vorgeschlagen.
7. Ziellagerplatz auswaehlen. Derselbe Quelllagerplatz kann nicht als Ziel gewaehlt werden.
8. Zusammenfassung pruefen und `Umlagerung buchen` bestaetigen.

Tanklager werden nicht als Ziel angeboten und sind auch serverseitig gesperrt. Direkt vor der Buchung liest die App die Quelle nochmals aus Oxaion und validiert das Ziel. Hat sich der Quellbestand seit der Anzeige geaendert, wird die Umlagerung gestoppt und die Lageruebersicht muss neu geladen werden.

Die Charge bleibt bei dieser Umlagerung unveraendert. Bei unklarem Buchungsausgang **nicht erneut umlagern**, sondern `Status in Oxaion pruefen` verwenden.

Die Tankliste wird dynamisch aus Oxaion gelesen. Neue Tanklagerorte erscheinen automatisch, wenn sie in der aktiven Oxaion-Umgebung als Lagerortart `02` gepflegt sind.

## 12. Erkennungsfarben

Bei Artikeln mit gepflegten Oxaion-Sachmerkmalen kann die App ein zweigeteiltes Farbfeld anzeigen.

- linke Haelfte = `EFA01`
- rechte Haelfte = `EFA02`

Die Farbe dient nur der schnellen visuellen Gegenkontrolle.

Fehlt ein Farbfeld, bedeutet das nicht automatisch, dass der Artikel falsch ist. Wenn fuer einen Artikel keine gueltigen EFA01/EFA02-Werte gepflegt sind, wird bewusst kein leeres Farbfeld angezeigt. Sind Farben gepflegt, muessen dieselben Farben in der Tankkarte und in der Pulverlagerkarte erscheinen. Referenz in PRODUCTION: `RP.00024` = Rot/Braun. Die eigentliche Buchungspruefung erfolgt weiterhin ueber Artikel, Charge und Oxaion-Bestand.

## 13. Etiketten nachdrucken

Dieser Vorgang druckt Etiketten zu einer bereits erfolgreich abgeschlossenen Tankauslagerung nach. Er fuehrt keine neue Materialbuchung aus.

Die technische Karte `Anzeigeproblem erkannt` ist nur fuer einen tatsaechlich inkonsistenten beziehungsweise leeren Prozesszustand vorgesehen. Eine normal sichtbare Nachdruckoberflaeche ist kein Fehlerzustand und darf diese Karte nicht ausloesen.

Bedienablauf:

1. `Etiketten nachdrucken` waehlen.
2. Erfolgreiche Tankauslagerung suchen oder filtern.
3. Gewuenschten Vorgang auswaehlen.
4. Lagerbeleg, Artikel, Charge, Ziel und bisher angeforderte Etiketten kontrollieren.
5. Zusaetzlich benoetigte Etikettenanzahl eingeben.
6. Nachdruck bestaetigen.

Wenn ein frueherer Druckversuch fuer denselben Vorgang einen unklaren Status hat, blockiert die App einen weiteren Nachdruck. Zuerst Drucker beziehungsweise Oxaion-Druckwarteschlange klaeren.

## 14. Erfolg und Fehlermeldungen

### Erfolg

Ein Vorgang ist erst dann erfolgreich, wenn die App einen eindeutig bestaetigten Erfolg meldet.

Nach erfolgreichem Abschluss kehrt die App zur Vorgangsuebersicht zurueck.

### Konflikt

Beispiele:

- Bestand hat sich geaendert;
- Charge passt nicht mehr;
- Tankzustand stimmt nicht mehr;
- Fertigungsauftrag oder Materialposition hat sich geaendert.

Massnahme: aktuelle Daten neu lesen beziehungsweise Vorgang neu vorbereiten. Die App bucht in diesem Fall nichts blind weiter.

### Gesperrt

Wenn Oxaion einen Datensatz als gesperrt meldet, wurde die Buchung nicht durchgefuehrt.

Massnahme: warten beziehungsweise den verantwortlichen Mitarbeiter oder die Produktionsleitung informieren und danach bewusst neu versuchen.

### Fachlich abgelehnt

Oxaion hat die Buchung eindeutig abgelehnt.

Massnahme: angezeigte Ursache korrigieren. Nicht einfach denselben Vorgang unveraendert wiederholen.

### Buchungsausgang unklar

Wenn die App meldet, dass der Ausgang unklar ist:

- **nicht erneut buchen**;
- angezeigte Vorgangs-/Transaktionsinformation sichern;
- Oxaion beziehungsweise Administrator/Produktionsleitung zur Klaerung heranziehen.

Dies gilt auch fuer einen unklaren Etikettendruck: nicht blind nochmals drucken.

## 15. Offline-Verhalten

Die PWA kann ihre Oberflaeche teilweise lokal laden. Das bedeutet nicht, dass Oxaion erreichbar ist.

Eine produktive Buchung darf offline nicht als erfolgreich dargestellt werden.

Wenn die Verbindung fehlt:

- Verbindungshinweis beachten;
- Vorgang nicht als erfolgreich betrachten;
- nach Wiederherstellung der Verbindung die serverseitige Pruefung abwarten.

## 16. Was der Bediener nicht tun soll

- `PROD` und `STG` nicht verwechseln.
- Bei unklarem Buchungsausgang nicht nochmals denselben Vorgang starten.
- Lagerort, Lagerplatz, Artikel oder Charge nicht aus Vermutung ersetzen.
- Bei falschem Pulver keine Charge trotzdem bestaetigen.
- Einen leeren Tank nicht ueber `Pulver nachfuellen` befuellen.
- Einen unklaren Druck nicht blind nachdrucken.
- Fehlende Erkennungsfarbe nicht als alleinigen Buchungsfehler interpretieren.

## 17. Hilfe / Eskalation

Bei einem Problem moeglichst folgende Informationen weitergeben:

- angezeigte Umgebung `STG` oder `PROD`;
- Vorgang;
- sichtbare Fehlermeldung;
- Tanklagerort;
- Artikel / Charge, soweit sichtbar;
- Fertigungsauftrag, falls betroffen;
- ungefaehre Uhrzeit;
- angezeigte Transaktions-/Vorgangs-ID, falls vorhanden.

Passwoerter oder andere Zugangsdaten niemals in Screenshots, Chats oder Fehlermeldungen weitergeben.

## 18. Dokumentationspflege

Dieses Bedienerhandbuch beschreibt den aktuellen Bedienstand der App.

Wenn sich Prozessreihenfolge, sichtbare Felder, Scannerablauf, Meldungen, Anmeldeverfahren oder Bedienregeln aendern, muss dieses Dokument im selben Entwicklungsschritt aktualisiert werden.

## Kundenbeistellpulver PB.*

Neben den bisherigen `RP.*`-Pulverartikeln kann die PWA auch `PB.*`-Artikel verarbeiten. `PB.*` kennzeichnet Pulver, das ein Kunde fuer seine Auftraege beistellt, beispielsweise `PB.00001` / `AlSi10Mg`.

- Die Lagerliste fuehrt PB-Artikel separat und kennzeichnet sie als `Kundenbeistellung`.
- Beim Scannen gelten dieselben Artikel-/Chargenregeln wie bei RP; insbesondere muss der **exakte Oxaion-Artikel** uebereinstimmen.
- Ein PB-Artikel darf niemals allein wegen gleicher Pulverbezeichnung durch einen RP-Artikel ersetzt werden.
- Kundenbeistellung bedeutet nicht, dass das Pulver fuer alle Auftraege freigegeben ist. Die verbindliche Kunden-/Auftragszuordnung ist vor dem produktiven Einsatz organisatorisch/fachlich zu klaeren.

## Chargenherkunft anzeigen (ab 0.1.11)

Dieser Vorgang ist eine **reine Online-Auskunft**. Es wird weder Material bewegt noch eine Buchung vorbereitet.

**Ohne vorhandene Bestandsposition:** `Vorgang auswaehlen` -> `Chargenherkunft anzeigen`.

1. `Chargenetikett scannen` antippen; die Kamera oeffnet sich. Mit Zoom ausrichten und erst danach `Scannen` antippen.
2. Ein QR-Code muss exakt `Artikel+++Charge` enthalten, z. B. `RP.00010+++84671` oder `PB.00001+++KUNDENCHARGE`.
3. Alternativ `Artikelnummer` und `Chargennummer` von Hand eintragen und `Chargenherkunft ermitteln` antippen.
4. Das System fragt die Oxaion-Herkunft online ab und zeigt die eindeutigen Grundchargen an. Sofern geliefert, stehen darunter Lieferant, Bestellung, Lieferschein, Wareneingang und Fertigungsauftrag.
5. Bei keiner Grundcharge oder einem Fehler die eingebenen Werte bzw. die Oxaion-Verbindung pruefen. Es ist keine Materialbuchung erfolgt.

**Aus Bestaenden:** `Bestaende anzeigen` -> belegten Maschinentank oder Pulverlagerposition antippen -> `Chargenherkunft anzeigen`. Artikel und Charge werden direkt aus der angezeigten Position verwendet; keine neue Eingabe und kein Scan erforderlich. `Zurueck zu Details` fuehrt wieder ins jeweilige Detailfenster.

Bei PB.* handelt es sich um Kundenbeistellpulver. Die reine Herkunftsanzeige bestaetigt **nicht**, dass diese Charge fuer einen beliebigen Auftrag verwendet werden darf.

## Chargenherkunft ab 0.1.12 – Bedienung

**Eigener Vorgang:** `Chargenherkunft anzeigen` oeffnen. Entweder `Chargenetikett scannen` antippen, Kamera ausrichten und bewusst `Scannen` starten oder Artikel und Charge von Hand eingeben und `Chargenherkunft ermitteln` waehlen.

**Aus der Bestandsansicht:** Tank oder Lagerposition antippen, dann `Chargenherkunft anzeigen`. Das Herkunftsfenster bietet `Zurueck zu Details` (zurueck ins Lagerplatz-/Tankdetail) und einen roten `Schliessen`-Button (Dialog vollstaendig schliessen). In den Bestandsdetails ist `Schliessen` ebenfalls rot.

Die Grundchargen werden als einzelne, klar voneinander getrennte Karten mit Artikel/Charge und den vorhandenen Angaben zu Lieferant, Bestellung, Lieferschein und Wareneingang angezeigt. Ohne diese Felder bleibt die Karte leerer; ein **verbrauchender Fertigungsauftrag wird nicht als Herkunft ausgegeben**. Ist Oxaion nicht erreichbar, erscheint eine Fehlermeldung statt eines vermeintlich aktuellen Offline-Ergebnisses.
