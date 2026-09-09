# Android-/Bedienoptimierungen 09.09.2026

Diese Datei dokumentiert die am 09.09.2026 getroffenen UI- und Prozessentscheidungen fuer den aktuellen STAGING-Stand. Die bestehenden Regeln zu Oxaion-Revalidierung, Personal-Session, Idempotenz, Recovery und Kein-Blind-Retry bleiben unveraendert.

## Navigation und Kopfzeile

- Auf der Vorgangsuebersicht steht als App-Titel nur `Pulververwaltung`.
- Innerhalb eines Vorgangs steht eine kurze einzeilige Bezeichnung des aktiven Vorgangs in der Kopfzeile.
- Die Kopfzeile bleibt auf Android einzeilig und moeglichst niedrig; der angemeldete volle Name bleibt ueber das bestaetigte Benutzer-Popup erreichbar beziehungsweise sichtbar.
- Der sichtbare Button `Vorgaenge` entfaellt.
- Die Android-/Browser-Zurueck-Funktion wechselt aus einem Vorgang auf die Vorgangsuebersicht.
- Eine bereits laufende Buchungsverarbeitung oder ein blockierender Dialog darf durch Zurueck nicht stillschweigend verlassen werden.
- Die aktuelle Schrittinformation wird mit dynamischem Sticky-Abstand unterhalb der realen Kopfzeilenhoehe positioniert.
- Sichtbare Schrittueberschriften tragen keine Nummerierung mehr.

## Ziel-Lagerort und Lagerplatz bei Tankauslagerung

Die bereits umgesetzte AJAX-Auswahl aus den rein lesenden Oxaion-SQL-Zielorten/-Lagerplaetzen bleibt verbindlich. Die kanonischen Buchungsschluessel entstehen weiterhin erst durch die Auswahl eines Backend-Treffers und werden unmittelbar vor dem Schreiben ueber die bestaetigten Oxaion-F4-Wege validiert.

Android-spezifisch gilt zusaetzlich:

- Nach einem erfolgreichen Maschinentank-Scan wird der Schritt `Ziel im Pulverlager` automatisch in den sichtbaren Bereich gescrollt und der Lagerort-Fokus dorthin verschoben.
- Beim Einstieg in die Zielauswahl bleibt die Bildschirmtastatur geschlossen.
- Fokus beziehungsweise Antippen des Feldes oeffnet die AJAX-Trefferliste, aber nicht automatisch die Tastatur.
- Wurde bereits ein Lagerort oder Lagerplatz gewaehlt und das Feld erneut angetippt, wird wieder die vollstaendige verfuegbare Trefferliste fuer den aktuellen Kontext angezeigt; der bereits eingetragene Wert darf die Liste nicht auf sich selbst einschraenken.
- Jedes Suchfeld besitzt ein Tastatur-Symbol. Erst dessen bewusste Aktivierung schaltet die Texteingabe/Tastatur fuer eine manuelle Suchzeichenfolge frei.
- Der Tastaturmodus bleibt nach der bewussten Aktivierung offen, bis das Feld tatsaechlich verlassen wird. Ein interner Blur/Refocus zum Oeffnen der Android-Tastatur darf den Modus nicht unmittelbar wieder sperren.
- Die Tastaturhilfe darf die bestehende Regel nicht aufweichen, dass frei getippter Text kein kanonischer Oxaion-Buchungsschluessel ist.

## Neue Befuellung eines leeren Tanks

### Mengenvorschlag

Nach Auswahl einer eindeutigen aktuellen Oxaion-Bestandsposition wird deren aktuell verfuegbare Menge als **Vorschlag** in das Mengenfeld uebernommen. Bei mehreren Positionen erfolgt der Vorschlag erst nach Auswahl der konkreten Position.

Der Wert bleibt editierbar. Das Backend revalidiert Bestand und verwendete Menge unmittelbar vor der Buchung; der Vorschlag ist keine Reservierung und keine Buchungswahrheit.

### Mix-Charge

Die alte Regel `immer neue Mix-Charge` ist durch die dynamische Regel aus `docs/FILL_NEW_OXAION_SEQUENCE.md` ersetzt:

- genau eine bereits eingelagerte Mix-Charge -> vorhandene Mix-Charge bleibt;
- vorhandene Mix-Charge + weitere Charge -> neue Mix-Charge;
- genau eine Nicht-Mix-Charge -> neue Mix-Charge;
- massgeblich ist ausschliesslich die finale Quellenliste beim Buchen, auch nach Hinzufuegen und wieder Entfernen einer Quelle.

## Pulververbrauch auf Fertigungsauftrag

- Wichtige Bedienfelder und Buchungsbestaetigungen schreiben `Fertigungsauftrag` aus; `FA` wird dort nicht als Kurzform verwendet.
- Das Eingabefeld bezeichnet den **Ist-Verbrauch** als kumulierten tatsaechlichen Verbrauch, nicht eine zusaetzliche Menge.
- Nach dem Lesen der Materialposition wird das Feld mit dem Oxaion-Sollwert (`AMMATB`) vorbelegt.
- Diese Vorbelegung ist nur ein Vorschlag. Direkt am Feld steht klar, dass der Bediener den Wert mit dem tatsaechlichen Ist-Verbrauch pruefen und bei Abweichung korrigieren muss.
- Der aktuelle Tankbestand wird aus der tatsaechlichen gelesenen Tankmengenposition verwendet. Er darf nicht aus der Artikelnummer beziehungsweise dem ersten Zahlenwert der Darstellungszeile abgeleitet werden.
- Die neu zu buchende Differenz bleibt `Ist-Verbrauch - bereits gebuchtes AMMATV` und darf den aktuellen Tankbestand nicht ueberschreiten.

## Buchungsverarbeitung und Rueckmeldungen

- Nach der finalen Buchungsbestaetigung erscheint ein blockierender Lade-Spinner mit dem Hinweis, dass die Buchung verarbeitet wird und nicht erneut ausgeloest werden darf.
- Der Spinner verschwindet erst, wenn eine Ergebnis-/Recovery-Meldung vorliegt oder ein technischer Frontendfehler die Verarbeitung beendet.
- Erfolgsmeldungen fuer die sichtbaren Prozesse werden auf Deutsch ausgegeben.
- Fuer den Fertigungsauftrag lautet die Kernaussage sinngemaess, dass der Pulververbrauch auf den Fertigungsauftrag in Oxaion erfolgreich gebucht und bestaetigt wurde.
- Wenn der Tankbestand beim erneuten Lesen nicht eindeutig ist, soll die Bedienermeldung nicht nur technisch `nicht eindeutig` sagen. Bei mehreren Chargen/Bestandspositionen lautet die Handlungsanweisung sinngemaess: `Im EOSx Tank sind laut System mehr als eine Charge bzw. Bestandsposition vorhanden. Daher kann die Buchung nicht durchgefuehrt werden. Bitte nichts mehr buchen und die Produktionsleitung informieren.`
- `UNCERTAIN` und `MANUAL_REVIEW_REQUIRED` bleiben davon unberuehrt: keine automatische Erfolgsaussage und kein Blind-Retry.
