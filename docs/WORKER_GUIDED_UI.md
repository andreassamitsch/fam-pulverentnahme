# Mitarbeitergefuehrte Bedienoberflaeche

Stand: 04.09.2026

## Ziel

Die Produktionsoberflaeche soll den Mitarbeiter immer klar zum naechsten notwendigen Schritt fuehren und technische Detailinformationen ausblenden, die fuer die sichere Bedienung nicht erforderlich sind.

Es gibt weiterhin nur eine WebApp und einen fachlichen Ablauf. Der Schalter `Dev-Infos` aendert ausschliesslich die Darstellung. Er darf keine Backend-Pruefung, Authentifizierung, Oxaion-Revalidierung, Idempotenzregel oder Buchungsfreigabe umgehen.

## Zwei Darstellungsmodi

### Mitarbeitermodus - Standard

`Dev-Infos` ist standardmaessig ausgeschaltet.

Sichtbar bleiben insbesondere:

- aktueller Arbeitsschritt
- Anmeldung
- Maschinentank-Scan
- Pulverartikel und Erkennungsfarben
- hilfreiche bekannte Lagerorte/Lagerplaetze fuer passendes Pulver
- Chargen-Scan
- bei Mehrdeutigkeit Auswahl des tatsaechlichen Entnahmeorts
- Einfuellmenge
- weitere Nachfuellcharge
- Buchung
- Erfolg beziehungsweise Fehler mit Handlungsanweisung
- Recovery-Aktionen, wenn sie fachlich erforderlich sind

Nicht erforderliche technische Details wie Backend-Health, Roh-JSON, interne Mix-Erzeugungsdaten, Test-Fehlersimulationen und technische Diagnoseinformationen werden ausgeblendet.

### Dev-Infos

Der Schalter `Dev-Infos` blendet auf derselben Seite die technischen Informationen wieder ein. Der Zustand ist nur eine UI-Praeferenz fuer die aktuelle Browsersitzung. Es werden dadurch keine fachlichen Daten oder Berechtigungen veraendert.

## Verbindlicher Mitarbeiterablauf

### 1. Anmelden

Bevorzugter Weg ist NFC:

1. `Mit NFC anmelden` druecken.
2. Personalchip an das Smartphone halten.
3. Backend ordnet die RFID ueber Syncos einer aktiven/sichtbaren Person zu.
4. Die Personalnummer wird danach ueber den bereits bestaetigten exakten Oxaion-Personalweg geprueft.
5. Erst nach erfolgreicher Syncos- und Oxaion-Pruefung setzt das Backend die Personal-Session.

Damit ist NFC fuer diesen Prozess der bevorzugte Loginweg. Das bekannte Passwort wird bei diesem Weg nicht zusaetzlich abgefragt.

Fallback, wenn NFC nicht zur Verfuegung steht:

1. `Ohne NFC anmelden` oeffnen.
2. Personalnummer eingeben und den eindeutigen Oxaion-Treffer auswaehlen.
3. SYNCOS-Passwort eingeben.
4. Backend prueft das Passwort nach der dokumentierten Legacy-Logik und setzt bei Erfolg dieselbe Personal-Session.

Eine produktive Buchung ist nur mit einer gueltigen Backend-Session zulaessig. Direkt vor dem ersten schreibenden Oxaion-Aufruf bleibt zusaetzlich die exakte Oxaion-Personalpruefung aktiv.

Details zur Passwortpruefung stehen in `docs/PERSONNEL_AUTHENTICATION.md`, zur RFID-Zuordnung in `docs/NFC_PERSONNEL_LOOKUP.md`.

### 2. Maschinentank scannen

Nach erfolgreicher Anmeldung wird `Maschinentank scannen` als naechster Schritt hervorgehoben.

Der Maschinentank-QR enthaelt weiterhin nur den Oxaion-Tanklagerort, z. B. `EOS1`, und wird gegen die freigegebene Tankliste geprueft.

Nach erfolgreichem Tankscan werden aus Oxaion ermittelt:

- Pulverartikel
- Artikelbezeichnung
- aktueller Tankbestand fuer die fachliche Pruefung
- EFA01/EFA02 als visuelle Erkennungshilfe

Im Mitarbeitermodus stehen Artikel und das zweigeteilte Farbfeld im Vordergrund. Technische Tankdetails koennen ueber `Dev-Infos` eingeblendet werden.

### 3. Passendes Pulver finden und Nachfuellcharge scannen

Sobald der Artikel des Tanks bekannt ist, liest die PWA die bereits bestaetigten positiven Quellbestaende des Artikels ueber die vorhandenen Oxaion-Lesewege.

Als reine Suchhilfe werden dem Mitarbeiter bekannte Lagerorte und - soweit vorhanden - Lagerplaetze angezeigt, an denen passendes Pulver liegt.

Wichtig:

- Diese Anzeige ist eine Weg-/Suchhilfe und keine Buchungsfreigabe.
- Der Mitarbeiter muss weiterhin den QR-Code der tatsaechlich entnommenen Charge scannen.
- Der Maschinentank selbst bleibt als Quelle ausgeschlossen.
- Die konkrete Buchungsposition wird aus dem aktuellen Oxaion-Bestand zur gescannten Charge ermittelt.
- Bei mehreren moeglichen Positionen muss der Mitarbeiter den tatsaechlichen Entnahmeort bestaetigen.
- Unmittelbar vor der Buchung wird die Position serverseitig erneut validiert.

#### Dieselbe Charge auf mehreren Lagerplaetzen

Eine Chargennummer ist nicht selbst die Eindeutigkeitsgrenze. Dieselbe Charge darf in einem Vorgang mehrfach gescannt und verwendet werden, wenn Oxaion fuer diese Charge mehrere unterschiedliche positive Bestandspositionen liefert.

Verbindlich gilt:

- Eindeutig ist die Kombination aus Oxaion-Lagerort, internem Lagerplatz und Charge.
- Eine bereits in diesem Vorgang ausgewaehlte exakte Bestandsposition wird bei einem weiteren Scan derselben Charge ausgeblendet.
- Bleibt danach genau eine noch nicht verwendete Bestandsposition uebrig, wird diese automatisch uebernommen.
- Bleiben mehrere Positionen uebrig, muss der Mitarbeiter den tatsaechlichen Entnahmeort bestaetigen.
- Dieselbe exakte Oxaion-Bestandsposition darf weiterhin nicht zweimal verwendet werden. Diese Regel wird vor der Buchung zusaetzlich serverseitig geprueft.

Damit kann beispielsweise Charge `52918` nacheinander von zwei unterschiedlichen Lagerplaetzen entnommen werden, ohne die Duplicate-Prevention fuer die tatsaechliche Bestandsposition aufzuweichen.

#### Soll-Farbe waehrend des Chargenscans

Beim Oeffnen des Chargenscanners bleibt die aus EFA01/EFA02 ermittelte Soll-Erkennungsfarbe des Tankartikels sichtbar. Direkt daneben steht der Scanstatus.

Sobald ein QR-Code erkannt wurde, wird kurz der erkannte Artikel samt Charge neben der Soll-Farbe angezeigt. Nach einem gueltigen Scan bleibt diese Soll-/Ist-Darstellung auch in der angelegten Nachfuellcharge sichtbar. Die Farbanzeige ist weiterhin nur eine visuelle Erkennungshilfe; die fachliche Freigabe erfolgt ueber Artikel, Charge und aktuellen Oxaion-Bestand.

#### Falscher oder nicht zulaessiger Chargenscan

Ein falscher Scan erzeugt keine Nachfuellkarte und muss nicht manuell entfernt werden.

Als Fehlscan gelten in diesem Ablauf insbesondere:

- gescannter Artikel stimmt nicht mit dem Tankartikel ueberein;
- Charge des richtigen Artikels ist auf keinem zulaessigen Quellbestand positiv vorhanden;
- fuer die erneut gescannte Charge sind alle von Oxaion gefundenen exakten Bestandspositionen in diesem Vorgang bereits verwendet;
- QR-Code entspricht nicht dem erwarteten Format `Artikel+++Charge`.

Bei einem solchen Scan erscheint eine seitendeckende, blockierende Meldung. Bei falschem Artikel beziehungsweise nicht verfuegbarer Charge lautet die Kernaussage eindeutig, dass diese Charge **nicht in den Maschinentank eingefuellt werden darf**. Die Soll-Erkennungsfarbe und - soweit aus dem QR ableitbar - der gescannte Artikel und die Charge werden plakativ gegenuebergestellt.

Der Mitarbeiter muss die Meldung bewusst mit `Verstanden` bestaetigen. Erst danach wird der normale Scanablauf fortgesetzt.

Fehlscans werden serverseitig als WebApp-Auditereignis dokumentiert. Gespeichert werden nur die fuer die Nachvollziehbarkeit benoetigten strukturierten Daten: Zeitpunkt, angemeldeter Mitarbeiter, Maschinentank, erwarteter Artikel, gescannter Artikel, gescannte Charge und Ablehnungsgrund. Passwoerter, RFID, Connection Strings und ein beliebiger roher QR-Inhalt werden nicht in diesem Audit gespeichert. Dieses Vorpruefungsereignis ist **kein** Oxaion-Buchungsstatus `REJECTED` und loest keine Materialbuchung aus.

### 4. Menge eingeben

Nach erfolgreicher Aufloesung der gescannten Charge wird das Mengenfeld hervorgehoben.

Verbindlich:

- Die Einfuellmenge wird niemals vorbelegt.
- Der Bediener muss die tatsaechlich eingefuellte Menge bewusst eingeben.
- Die Menge muss groesser als 0 sein.
- Die Menge darf den aktuell verfuegbaren Oxaion-Bestand der bestaetigten Position nicht ueberschreiten.

Erst wenn die aktuelle Charge inklusive Menge vollstaendig ist, kann eine weitere Nachfuellcharge gescannt werden.

Der Ablauf fuer weitere Chargen wiederholt sich:

`Charge scannen -> Entnahmeort falls noetig bestaetigen -> Menge eingeben`.

Der Button `Weitere Nachfuellcharge scannen` steht unterhalb der bereits erfassten Nachfuellcharge(n), damit der visuelle Ablauf von oben nach unten der tatsaechlichen Arbeit entspricht.

### 5. Buchen

Sind mindestens eine Nachfuellcharge und alle Mengen vollstaendig, wird `Buchung starten` hervorgehoben.

Die Mitarbeiteransicht zeigt eine kompakte Zusammenfassung aus:

- Maschinentank
- Pulverartikel
- Nachfuellcharge(n)
- eingegebene Menge(n)

Technische Daten wie neue Mix-Charge und interne Buchungsdetails bleiben im Mitarbeitermodus verborgen, werden aber weiterhin systemseitig erzeugt und geprueft.

### 6. Ergebnis

Nach dem Buchungsversuch muss das Ergebnis gross und eindeutig angezeigt werden.

Bei `SUCCESS`:

- eindeutig bestaetigen, dass die Buchung erfolgreich abgeschlossen ist.

Bei fachlich eindeutigem Fehler:

- verstaendliche Ursache anzeigen;
- konkrete naechste Massnahme anzeigen;
- keinen automatischen Oxaion-Retry ausloesen.

Bei `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED`:

- ausdruecklich `Nicht erneut buchen` anzeigen;
- Produktionsleitung informieren;
- Status beziehungsweise Beleg nach dem dokumentierten Recovery-Verfahren pruefen.

Die Regeln aus `docs/ERROR_HANDLING.md` bleiben unveraendert verbindlich.

## Kamera-Zoom als Geraetepraeferenz

Der vom Mitarbeiter am QR-Scanner eingestellte Kamera-Zoom wird als reine lokale UI-/Geraetepraeferenz gespeichert und beim naechsten Kameraoeffnen wiederhergestellt, soweit das aktuelle Geraet beziehungsweise die Kamera Zoom unterstuetzt.

- Die Einstellung gilt ueber Buchungsvorgaenge und App-Neustarts hinweg fuer denselben Browser-Origin.
- Ein gespeicherter Wert wird auf den vom aktuellen Kameratrack gemeldeten Min-/Max-Bereich begrenzt.
- Ist Zoom auf einem Geraet nicht unterstuetzt, hat der gespeicherte Wert keine fachliche Wirkung.
- Dafuer darf `localStorage` verwendet werden, weil es sich nicht um fachliche Vorgangs-, Outbox-, Buchungs- oder ERP-Daten handelt. Fachliche Offline-Daten bleiben gemaess `docs/OFFLINE_PWA.md` in IndexedDB.

## Hervorhebung des naechsten Schritts

Der aktuelle Schritt erhaelt eine deutliche visuelle Markierung. Bereits abgeschlossene Schritte werden als erledigt dargestellt. Noch nicht zulaessige Schritte werden optisch zurueckgenommen und ihre Aktionen deaktiviert.

Innerhalb eines Schritts wird die aktuell erwartete Bedienaktion hervorgehoben, beispielsweise:

- NFC-Button
- Maschinentank-Scan
- Entnahmeort-Auswahl bei Mehrdeutigkeit
- Mengeneingabe
- Buchungsbutton

Diese Hervorhebung ist Bedienhilfe. Die fachliche Zulaessigkeit wird weiterhin durch Frontend- und insbesondere Backend-Pruefungen abgesichert.
