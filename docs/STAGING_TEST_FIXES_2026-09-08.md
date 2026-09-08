# STAGING-Korrekturen fuer separate Pulvervorgaenge – 08.09.2026

## Zweck und Gueltigkeit

Dieses Dokument beschreibt den aktuellen technischen Stand der Korrekturen nach den Android-STAGING-Tests vom 08.09.2026. Es ist fuer die hier beschriebenen Punkte spezifischer und neuer als aeltere Implementierungsstandsangaben in `docs/SEPARATE_TANK_PROCESSES.md`, `docs/FA_CONSUMPTION_PROCESS.md`, `docs/FILL_NEW_OXAION_SEQUENCE.md`, `docs/INVENTORY_VIEW.md`, `docs/PROJECT_CONTEXT.md` und `docs/ARCHITECTURE.md`.

Die fachlichen Sicherheitsregeln bleiben unveraendert: keine direkten ERP-Buchungen per SQL, keine erfundenen Oxaion-Aufrufe, eindeutige Vorgangs-IDs, keine Blind-Retries und `SUCCESS` nur nach belastbarer Bestaetigung.

## 1. Ziel-Lagerort und Ziel-Lagerplatz bei Tankauslagerung

Der Bediener gibt Lagerort und Lagerplatz nicht mehr als freie Buchungsschluessel ein.

Die PWA verwendet eine AJAX-Suche mit Trefferliste:

- `GET /api/target-locations/warehouses`
- `GET /api/target-locations/storage-bins`

Die Suchvorschlaege werden serverseitig und rein lesend aus der Oxaion-Datenbank gelesen:

- Lagerorte aus `OXAION.ULGSTP`
- interne Lagerplatzschluessel aus `OXAION.LLPLAP`
- nur fuer die konfigurierte Firma
- nur `SELECT`, keine schreibenden SQL-Anweisungen

Diese neuere spezifische technische Entscheidung erweitert die bisher in `PROJECT_CONTEXT`/`ARCHITECTURE` nur fuer die RP.*-Bestandsansicht beschriebene Oxaion-SQL-Ausnahme **ausschliesslich um diese rein lesende Zielort-/Lagerplatzsuche**.

Wichtig: Die SQL-Suche ist nur Bedienhilfe und keine Buchungsfreigabe. Unmittelbar vor der wirksamen `LF`-Buchung prueft das Backend die ausgewaehlten Schluessel weiterhin ueber die bereits bestaetigten Oxaion-F4-Wege:

- Ziel-Lagerort: `LB20115J *F4` -> `US16601R`
- Ziel-Lagerplatz: `LB20115J *F4` -> `LB13210R`

Ein frei eingetippter Wert wird im Frontend nicht als kanonischer Buchungsschluessel uebernommen. Erst die bewusste Auswahl eines Backend-Treffers setzt den versteckten Zielwert.

## 2. Tank auslagern – BEL1422

Der Fehler `BEL1422 – Fuer den Beleg kann ein Datensatz nicht geladen werden` entstand, weil die erste `LF`-Position eines leeren Materialbelegs wie eine normale Folgeposition initialisiert wurde.

Der erfolgreiche JET-Mitschnitt vom 08.09.2026 belegt fuer Position 1 einen eigenen Ablauf. `MaterialTransferBookingService.BookAsync` verwendet deshalb bei `Position=1` und `BookingKey=LF` die dedizierte `AddFirstLfPositionAsync`-Sequenz:

1. `LB20115J *LOAD`
2. `LB20115J *NEW` mit vollstaendigem Beleg-/Positionskontext
3. Quellvalidierung; bestaetigter Zwischenzustand `LAG1515`
4. Ziel-Lagerort/-platz ueber die bestaetigten F4-Listen validieren
5. `LB20115J *PUTNEW` mit Ziel; bestaetigtes `TCODE=WIN3`
6. `LB20115J *LOADWIN3`
7. finaler `LB20115J *PUTNEW`
8. `LB20110R *UPD`
9. exakte Verifikation des `LF/LE`-Bewegungspaars

Damit nutzt auch der Vorgang `Pulver aus Tank auslagern` die live bestaetigte Initialisierung fuer die erste LF-Position.

## 3. Neues Pulver in leeren Tank – BEL1422 und Recovery

Auch `Neues Pulver in Tank fuellen` startet auf einem leeren Materialbeleg mit einer ersten `LF`-Position und verwendet deshalb dieselbe dedizierte First-LF-Sequenz.

Die fachlich vorgesehene Kette bleibt:

1. Position 1: `LF -> LE`, erste reale Quellcharge in den leeren Tank, Charge bleibt zunaechst erhalten.
2. Position 2: `LM -> LN`, Umchargung der ersten Charge im Tank auf die neue automatisch erzeugte Mix-Charge.
3. Position 3 ff.: weitere Quellen mit `LM -> LN` in dieselbe neue Mix-Charge.

Die einzelnen Bausteine sind aus realen Oxaion-Mitschnitten bestaetigt. Die kombinierte Kette `LF/LE -> LM/LN` in genau einem Beleg bleibt bis zum erneuten Android-STAGING-Test als End-to-End-Testpunkt offen.

Fehlerbehandlung nach bereits erzeugter Belegnummer wurde verschaerft: Liefert Oxaion nach vorhandener `KOBGNR` noch eine fachliche Ablehnung, wird nicht mehr automatisch `REJECTED` als sicherer Nicht-Buchungsnachweis angenommen. Der Vorgang geht in `MANUAL_REVIEW_REQUIRED`; der Bediener darf nicht blind neu buchen und muss zuerst die lesende Statuspruefung verwenden.

## 4. FA-Verbrauch – Endzustand nach MK sicher pruefen

Die normale FA-Materialrueckmeldung verwendet den bestaetigten Weg:

- `PW22000J *LOADNEW`
- `PW22000J *CHK`
- `PW22031J *LOAD`
- `PW22031J *NEW`
- `PW22031J *SNPFLICHT`
- `PW22031J *PUTNEW`

Vor `PUTNEW` wird jetzt zusaetzlich der im erfolgreichen Referenzfluss bestaetigte Dialogzustand `TCODE=ELSE` verlangt. Liefert `SNPFLICHT` einen anderen Dialogzustand, wird vor dem schreibenden `PUTNEW` gestoppt.

Nach erfolgreicher `PUTNEW`-Antwort wird die Buchung **nie erneut gesendet**. Stattdessen wird die Materialposition ausschliesslich lesend ueber frische Oxaion-Sessions erneut gelesen. Wegen moeglicher kurzer Sichtbarkeitsverzoegerung erfolgen mehrere begrenzte Leseversuche mit kurzen Wartezeiten.

`SUCCESS` ist nur zulaessig, wenn exakt gilt:

- Materialposition unveraendert
- `AMMATV = erwarteter bisheriger Ist-Verbrauch + zusaetzlicher Verbrauch`
- `AMMPST = 9`

Kann dieser Endzustand nicht exakt bestaetigt werden, bleibt der Vorgang `MANUAL_REVIEW_REQUIRED`. Die Bedienermeldung fordert auf: nicht erneut buchen, zuerst `Status in Oxaion pruefen`, danach gegebenenfalls Produktionsleitung informieren.

Wichtig: Ein spaeter zufaellig passender Materialpositionszustand beweist ohne eindeutige Oxaion-Transaktionsreferenz weiterhin nicht sicher, dass genau dieser WebApp-Vorgang die Aenderung erzeugt hat. Die Recovery-Regel aus `ERROR_HANDLING.md` bleibt daher bestehen.

## 5. RP.* Lagerbestand – SQL-Readerfehler

Der Android-Test zeigte:

`Ungueltiger Versuch, aus der Spaltenordinalzahl '1' zu lesen. Mit CommandBehavior.SequentialAccess kann nur aus der Spaltenordinalzahl '3' oder groesser gelesen werden.`

Ursache war `CommandBehavior.SequentialAccess`, obwohl der Mapper die SELECT-Spalten bewusst per Namen und damit nicht streng in Projektionsreihenfolge liest.

`InventoryService` verwendet deshalb jetzt `CommandBehavior.Default`. Die SQL-Abfrage selbst bleibt unveraendert rein lesend und auf RP.*-Positionen mit Bestand ungleich 0 begrenzt.

## 6. Frontend und PWA

Die Tankauslagerung erhaelt die AJAX-Zielortauswahl als separate Frontend-Hilfe `target-location.js`. Der Service-Worker cached diese Datei mit einer neuen Cache-Version. Die CI prueft auch ihre JavaScript-Syntax.

Die Prozessauswahl bleibt waehrend normaler Refreshes stabil; die neue Zielortsuche aendert die bestehende Prozess-/Authentifizierungslogik nicht.

## 7. Automatisierte Tests und CI

Ergaenzt beziehungsweise angepasst wurden Tests fuer:

- SQL-Reader ohne `SequentialAccess`
- rein lesende Lagerort-/Lagerplatz-SQL-Suche
- SQL-LIKE-Escaping der AJAX-Suche
- exakte `LF/LE`-Verifikation
- exakte FA-MK-Endzustandspruefung
- bestehende `LF/LE -> LM/LN`-Transfer-Spezifikationen

Der Code-Stand `4aa53a13dcdfae7069163f58cb55abd9901d65c9` wurde im GitHub-Actions-Workflow `Build` erfolgreich gebaut, getestet und als self-contained Windows-STAGING-Paket publiziert.

## 8. Noch live zu testen

Vor einer produktiven Freigabe bleiben insbesondere folgende STAGING-Tests offen:

- Tankauslagerung mit AJAX-ausgewaehltem Lagerort/Lagerplatz bis zur erfolgreichen `LF/LE`-Verifikation.
- Neue Befuellung eines leeren Tanks mit der kombinierten Kette `LF/LE -> LM/LN` bis zur finalen exakten Belegverifikation.
- FA-MK-Buchung mit dem neuen `TCODE=ELSE`-Gate und der verzögerten rein lesenden Endzustandsverifikation.
- RP.* Lagerbestandsansicht auf Android nach Entfernung von `SequentialAccess`.

Bei allen Tests gilt: Ein alter unklarer Vorgang darf nicht als neuer Versuch wiederverwendet oder blind erneut gebucht werden.