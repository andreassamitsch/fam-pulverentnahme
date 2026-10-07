# Offene Punkte

Diese Checkliste wird waehrend des Projekts laufend aktualisiert. Offene Details duerfen nicht erfunden oder ohne Bestaetigung als Implementierungsgrundlage verwendet werden.

## Neue Prozessanforderungen 16.09.2026

Details stehen verbindlich in `docs/JOB_ABORT_CORRECTION_AND_TANK_WEIGHING_2026-09-16.md` und `docs/SEPARATE_TANK_PROCESSES.md`.

- [x] Fachlichen Prozess `Korrekturbuchung Fertigungsauftrag (Jobabbruch)` festgelegt: zuerst die urspruengliche fehlerhafte FA-Materialrueckmeldung stornieren und eindeutig verifizieren, danach erst den tatsaechlichen Ist-Verbrauch neu rueckmelden. Ein unklarer Stornoausgang blockiert die neue Rueckmeldung vollstaendig.
- [x] 18.09.2026: Vollstaendigen JET-/HTTP-Mitschnitt eines realen FA-Materialstornos in FAM-STAGING ausgewertet. Bestaetigt sind `PW22000J *LOADNEW STORNO=J` -> `*STON` -> `PW22021R`-Rueckmeldeliste -> exaktes `*STORNO` ueber Rueckmeldenummer/-datum/-uhrzeit sowie die Rueckkehr von `AMMATV=20,160 / AMMPST=9` auf `0 / 0` und die exakte Rueckbuchung auf dieselbe EOS1-Mix-Charge.
- [x] 18.09.2026: Direkt nach dem bestaetigten Storno wurde im selben Mitschnitt eine neue MK mit 10,500 kg erfolgreich gebucht; die bereits bekannte MK-Sequenz ist nach dem Storno wieder zulaessig und der finale Tankbestand wurde exakt bestaetigt.
- [x] 28.09.2026 Android-Live-Test `FA24FI00118`: Die App blockierte den Storno, weil die Mix-Charge der gueltigen Originalrueckmeldung nicht mehr der aktuell im gescannten Tank befindlichen Mix-Charge entsprach. Diese Sperre war fachlich richtig; die zwischenzeitliche Lockerung wurde rueckgaengig gemacht. Neu wird Tank/Mix bereits direkt nach dem FA-Scan gegen die gueltige Oxaion-Stornorueckmeldung geprueft. Bei Abweichung wird die Mengeneingabe gesperrt und der Bediener erhaelt die konkrete Anweisung, den Fall in Oxaion zu pruefen und gegebenenfalls manuell ueber Lagerbelege zu korrigieren.
- [x] Beim Jobabbruch wird der tatsaechlich verbrauchte, abgewogene Ist-Wert eingegeben. Die App storniert zuerst die komplette urspruengliche Buchung und bucht danach genau diesen Ist-Verbrauch neu; bei 0,000 kg erfolgt nach dem bestaetigten Storno keine neue MK.
- [x] Fachliche Wiegungslogik fuer `Pulver aus Tank auslagern` festgelegt: Systembestand `Qsys` und physisch gewogene Menge `Qphys` vergleichen; eine Abweichung muss vor dem `LF`-Transfer auf genau Tankartikel und aktuelle Mix-Charge korrigiert und danach erneut aus Oxaion gelesen werden.
- [x] 18.09.2026: `I2 = Bestandskorr. Abgang (Schwund)` in FAM-STAGING per `US50000J` und realer `LB20115J`-/`LB20110R`-Buchung bestaetigt. Referenz: 0,001 kg auf EOS1/Mix; Tankbestand exakt -0,001 kg. Historischer Mitschnitt verwendete 03/6000; diese Kontierung ist durch die neue verbindliche FAM-Entscheidung vom 29.09.2026 fuer die WebApp abgeloest.
- [x] 28.09.2026 Android-Live-Test Tank-Auslagern: I2 wurde vor Beleganlage wegen der App-Pruefung `LBSKSB=0050000` blockiert. Diese Pruefung war falsch: `LBSKSB` darf leer sein; die fruehere `0050000`-Vorbelegung war nicht als FAM-Pflichtwert gedacht. Buchungsschluessel, Bezeichnung und `LBKLAS=J` bleiben die Vorpruefung. Die zwischenzeitliche Kontierungsanalyse wurde spaeter durch die verbindliche FAM-Entscheidung vom 29.09.2026 ersetzt: I1 und I2 verwenden beide `PSWERK=21` / `PSKSTL=5100`.
- [x] 29.09.2026 Android-Live-Test Tank-Auslagern: Ursache des wiederholten `NullPointerException` in `LB20115M_EntryPoints.entryChkIDNR04`, Zeile 1913, war der fehlende interne Textstruktur-Spiegelwert zu `TX_PCKMS`; analog muessen auch `I_TX_PCKMM` und `I_TX_PCKMZ` vorhanden sein. Nach Mitgabe aller drei leeren `TX_`-/`I_TX_`-Paare (`PCKMS`, `PCKMM`, `PCKMZ`) funktioniert der zuvor blockierende I2-Korrekturpfad im Android-STAGING-Livetest. Der Fix ist damit technisch bestaetigt. Die alten fehlgeschlagenen Belege `FA26MB00088` bis `FA26MB00093` bleiben historische Fehlversuche und duerfen nicht erneut verwendet werden.
- [ ] 29.09.2026 Etikettendruck nach Tank-Auslagern: JET-/HTTP-Mitschnitt `Ettikett Drucken Pulver aus Tank in Lager` analysiert und als separater Post-SUCCESS-Druckpfad umgesetzt. Ziel ist die eindeutige `LE`-Bewegung; Bediener-Stueckzahl wird ueber `EK99103R.MENGE` gesetzt; `MN50100J`-Druckkonfiguration wird aus Oxaion gelesen und nicht hart codiert. Android-STAGING-Livetest des Drucks steht noch aus; bei unklarem `*RUN` kein Blind-Reprint.
- [x] 01.10.2026 Etiketten-Nachdruck: eigenstaendiger Bedienvorgang im Android-STAGING-Test vom Anwender bestaetigt (`Etiketten Nachdruck funktioniert nun auch`). Erfolgreiche Tank-Outs, zusaetzliche Stueckzahl und separate Druckoperation bleiben erhalten. Ein priorer `UNCERTAIN`-/`MANUAL_REVIEW_REQUIRED`-Druck blockiert weiterhin den Nachdruck.
- [x] 05.10.2026 Ursache der falschen Diagnosekarte im sichtbaren Etiketten-Nachdruck gefunden und in `0.1.4` korrigiert: `ui-diagnostics.js` kannte `label-reprint` nicht als gueltigen Prozess und wertete die sichtbare `labelReprintProcess`-Karte daher faelschlich als leeren Prozesszustand. Prozess-Mapping, sichtbare Panel-Liste, Regressionstest und PWA-Cacheversion wurden gemeinsam aktualisiert.
- [x] 05.10.2026 Android/PRODUCTION mit `0.1.4` bestaetigt: `Etiketten nachdrucken` bleibt normal sichtbar und die falsche Karte `Anzeigeproblem erkannt` erscheint nicht mehr. Der Diagnosefix ist damit praxisbestaetigt.
- [x] 18.09.2026: `I1 = Bestandskorrektur Zugang` in FAM-STAGING per `US50000J` und realer `LB20115J`-/`LB20110R`-Buchung bestaetigt. Referenz: 0,002 kg auf EOS1/dieselbe Mix-Charge; Tankbestand exakt +0,002 kg. Im Mitschnitt verwendete Kostenstelle 5100.
- [ ] Optional pruefen und mitschneiden, ob Bestandskorrektur (`I1`/`I2` bzw. lokal bestaetigter Schluessel) und anschliessender `LF`-Transfer in einem einzigen Oxaion-Lagerbeleg sicher moeglich sind. Bis dahin keine gemeinsame Positionsfolge erfinden.
- [ ] Aufloesung/kleinste Anzeigestufe der produktiv eingesetzten Waage ermitteln und daraus die verbindliche Rundungs-/Abweichungstoleranz festlegen. Keine Toleranz hart codieren oder raten.

## Systemdokumentation 02.10.2026

- [x] Zentrale Systemdokumentation `docs/SYSTEM_DOCUMENTATION.md` erstellt.
- [x] Administratorhandbuch `docs/ADMIN_GUIDE.md` erstellt.
- [x] Bedienerhandbuch `docs/OPERATOR_GUIDE.md` erstellt.
- [x] `AGENTS.md` und `PROJECT_PROMPT.md` verpflichten kuenftige Aenderungen zur gleichzeitigen Pflege der betroffenen System-/Admin-/Bedienerdokumentation.
- [ ] Vor der ersten produktiven Freigabe die Bedienertexte mit Produktionsleitung/Key-Usern gegen den realen Ablauf pruefen und gegebenenfalls um Firmenvorgaben/Eskalationskontakte ergaenzen.

## Dynamische Maschinentanks 02.10.2026

- [x] Fachliche Tankdefinition bestaetigt: Oxaion `ULGSTP`, aktive Firma (`LGFIRM`), Lagerortart `LGLGART = '02'`.
- [x] Statische `MachineTanks:Warehouses = EOS1/EOS2`-Whitelist aus dem Backend entfernt.
- [x] `GET /api/machines`, Tankscan-Validierung, Lageruebersicht und Jobabbruch-Tankpruefung verwenden die dynamische Oxaion-Tankliste.
- [x] PWA laedt die Tankliste vor jedem Tankscan erneut, damit STAGING/PRODUCTION und Oxaion-Aenderungen nicht durch eine alte Browserliste verdeckt werden.
- [x] PRD-Direktabfrage 02.10.2026 bestaetigt aktuell fuenf Tanklagerorte: `EOS1`, `EOS2`, `M400-01`, `M400-02`, `M650`.
- [ ] APP-01/PRODUCTION nach MSI-Update pruefen: Lageruebersicht zeigt alle fuenf aktuell in `ULGSTP` definierten Tanklagerorte und die drei neuen Tanks lassen sich per QR als Maschinentank verwenden.

## Lageruebersicht-Darstellung 01.10.2026

- [x] Doppelte Lagerort-/Lagerplatzdarstellung korrigiert: Eine LLAWEP-Lagerortzeile wird unterdrueckt, wenn fuer denselben Artikel/Lagerort/dieselbe Charge konkrete LLPWEP-Lagerplatzbestaende vorhanden sind.
- [x] Pulverlager in der PWA vereinfacht: pro Artikel nur noch flache Bestandszeilen `Lagerort / Lagerplatz` + Charge + Menge; keine zusaetzliche Lagerort-Kopfzeile mit erneut dargestellten Chargenmengen.
- [x] 02.10.2026 SQL-Direktpruefung: `RP.00010` ist in der aktuellen Lagerbestandsabfrage enthalten. Der vorherige Fehlverdacht eines RP.00010-Filters ist damit fuer die SQL-Sicht ausgeraeumt.
- [x] 02.10.2026 SQL-Fehler nach `SELECT DISTINCT` korrigiert: `ORDER BY` verwendet jetzt die projizierten CAST/TRIM-Ausdruecke fuer Artikel, Artikelbezeichnung und Charge.
- [ ] Android-STAGING: Beispiel `RP.00002 / H04PULA / Charge 88445` pruefen; die 10,000-kg-Menge darf nur einmal als konkrete Lagerplatzposition erscheinen.
- [ ] Android-STAGING: `RP.00010` muss nach dem SQL-Fix in der Lagerliste sichtbar sein; SQL-seitig ist der Datensatz bereits bestaetigt.

## Jobabbruch-/Farbanzeige-Korrekturen 01.10.2026

- [x] Code: Manueller Maschinentank-Scan aus der Jobabbruch-Korrektur entfernt. Die eindeutige Oxaion-Originalrueckmeldung liefert Tanklager und Mix-Charge.
- [x] Code: Vor Freigabe der Mengeneingabe wird der automatisch ermittelte Lagerort gegen die FAM-Maschinentankliste geprueft und der aktuelle Tankbestand neu gelesen. Artikel und Mix-Charge muessen weiterhin exakt zur Originalrueckmeldung passen.
- [x] Code: Bei mehreren passenden Originalrueckmeldungen wird die automatische Ableitung fail-closed gesperrt; es wird nichts storniert.
- [x] Code: Jobabbruch-Seite startet direkt beim Fertigungsauftrag-Scan; der bisherige falsche Scroll-/Fokus auf den Tankscan entfaellt.
- [x] Code: EFA01/EFA02-Abruf fuer Maschinentanks in einen frischen Oxaion-App-Tunnel-Kontext verschoben. Die Lageruebersicht loest fehlende/unvollstaendige Artikel-Farben ebenfalls in isolierten Kontexten nach.
- [x] Code: Bei komplett fehlenden/ungueltigen Farben wird kein leeres Farbfeld mehr angezeigt.
- [ ] Android-STAGING: Jobabbruch ohne Tankscan mit einem passenden realen FA testen; automatisch ermittelten Tank/Mix und anschliessenden Storno-/Neubuchungsablauf bestaetigen.
- [ ] Android-STAGING: `RP.00010` in der Lageruebersicht pruefen; erwartet EFA01 Schwarz (`0D0D0D`) und EFA02 Violett (`7030A0`) sowohl am EOS1-Tank als auch beim Artikelblock.

## Bedien-/Lagerkorrekturen 01.10.2026

- [x] Code: Nachfuellen meldet einen eindeutig leeren Tank explizit als leer und verweist auf `Neues Pulver in Tank fuellen`.
- [x] Code: sichtbare Schrittnummern und nummerierte Mitarbeiteranweisungen entfernt.
- [x] Code: Prozessmenue auf die festgelegte Reihenfolge umgestellt; FA-Verbrauch steht auf Position 2, Etiketten-Nachdruck ganz unten.
- [x] Code: `Diagnose` und `Dev-Infos` standardmaessig ausgeblendet und nur ueber `Prototype__DeveloperToolsEnabled=true` serverseitig freischaltbar.
- [x] Code: Tankauslagerung beginnt beim Tankscan; die Wiegemenge loest waehrend der Zifferneingabe keinen automatischen Sprung mehr aus. Zielauswahl wird erst nach Enter/Blur einer gueltigen Menge freigegeben.
- [x] Code: eindeutiger Prozess-SUCCESS steuert den Ruecksprung zur Vorgangsuebersicht; Dialogtitel ist dafuer nicht mehr die Steuerinformation. Die Uebersicht wird am Seitenanfang geoeffnet.
- [x] Code: technische I1/I2-/LF/LE-Arbeitsanweisungen aus der normalen Tank-Out-Bedienoberflaeche entfernt; technische Details bleiben Backend/Diagnose.
- [x] Code: Erfolgsmeldungen der separaten Prozesse werden bedienerfreundlich auf Deutsch erzeugt und geben keine rohe englische Backend-Verifikationsmeldung mehr aus.
- [x] Code: erledigte Schritte koennen auf kleinen Displays kompakt zusammengefasst und zur Kontrolle wieder aufgeklappt werden.
- [x] Code: Lagerbeleg-Kopftexte werden zentral vor dem Oxaion-Aufruf auf `KOBGT1 <= 40` und `KOBGTX <= 35` begrenzt.
- [x] Code: Lageruebersicht um Maschinentanks und EFA01/EFA02-Erkennungsfarben erweitert.
- [x] Code: RP.*-Lagerplatzabfrage liest reale nicht-null Bestandszeilen direkt aus `LLPWEP`; zusaetzliche INNER-JOIN-Filter auf `LLPLAP`/`ULGSTP`, die vorhandene Bestandszeilen ausblenden koennen, wurden aus dieser Informationssicht entfernt. `LLAWEP.LAGRKZ <> 'J'` verhindert weiterhin doppelte Lagerortsummen.
- [ ] Android-STAGING: fehlenden Realfall `RP.00010` nach neuem Build in der Lageruebersicht bestaetigen und gegen Oxaion-Lagerplaetze vergleichen.
- [ ] Android-STAGING: Tank-Out-Fokus/Mengeneingabe, kompakte erledigte Schritte, Ruecksprung nach Erfolg und ausgeblendete Dev-/Diagnosewerkzeuge live bestaetigen.
- [ ] STAGING: neue KOBGT1/KOBGTX-Aufteilung an einem realen Lagerbeleg pruefen; Oxaion darf die bereits vorab auf 40/35 Zeichen begrenzten Werte nicht mehr abschneiden.

## Aktueller STAGING-Korrekturstand 10.09.2026

Details zum neuen Erststart-Befund und zur v31-Korrektur stehen in `docs/STAGING_TEST_FIXES_2026-09-10.md`.

- [x] Neuer Android-Live-Befund reproduzierbar eingegrenzt: Nach geloeschten App-Daten tritt die leere `Nachfuellen`-Seite beim ersten direkten Tankscan auf. Wird dagegen vor der Prozesswahl einmal in der Uebersicht per Pull-to-refresh aktualisiert, funktioniert derselbe Nachfuell-Tankscan ohne leere Seite.
- [x] Erststart normalisiert: Eine Seite ohne aktiven Service-Worker-Controller darf noch keinen Prozess starten. Nach Aktivierung des ersten Workers erfolgt genau ein sicherer automatischer Reload, solange noch kein Prozess, Scanner oder offener Buchungsvorgang aktiv ist. Eine Session-Markierung verhindert Reload-Schleifen.
- [x] Nachfuell-Sichtbarkeitsinvariante verschaerft: Solange `replenish` in der Oberflaeche als aktueller Prozess gilt, darf ein konkurrierender Router-/Auth-Refresh die Legacy-Nachfuellkarten nicht komplett ueber `processModeHidden` entfernen.
- [x] Diagnosezugriff ist im STAGING-Header dauerhaft sichtbar. `Diagnose` oeffnet das lokale Protokoll und `Diagnose kopieren` funktioniert unabhaengig davon, ob der Leerzustand automatisch erkannt wurde. Das Protokoll enthaelt zusaetzlich Service-Worker-Controller-, Navigationstyp- und Erststartinformationen, aber keine Passwoerter, Tokens, Connection Strings, Personalnummern oder Mitarbeiternamen.
- [ ] Exakten problematischen Kaltstart mit v31 live testen: App-Daten loeschen, App neu starten, **nicht** manuell aktualisieren, automatischen Erststart-Reload abwarten, anmelden, `Pulver nachfuellen`, Tank scannen. Falls der Fehler wieder auftritt, direkt im sichtbaren Header `Diagnose` -> `Diagnose kopieren` verwenden und den kompletten Text im Projektchat auswerten.

## Aktueller STAGING-Korrekturstand 09.09.2026

Details zu den aktuellen Android-Korrekturen stehen in `docs/STAGING_TEST_FIXES_2026-09-09.md`.

- [x] AJAX-Zielsuche fuer Tankauslagerung explizit case-insensitive umgesetzt.
- [x] SQL-Begrenzungen `TOP (50)`/`TOP (100)` aus der rein lesenden Lagerort-/Lagerplatzsuche entfernt; die Auswahl darf nicht nach den ersten 100 internen Lagerplaetzen enden.
- [x] Lagerort mit mehr als 100 Lagerplaetzen auf Android live bestaetigt: Fuer `H04KDX` werden im aktuellen STAGING-Stand auch die Lagerplaetze nach dem frueheren Listenende `LL324` vollstaendig angezeigt.
- [x] Alter doppelter Bootstrap von `process-mode.js` entfernt und durch Regressionstest gegen erneute doppelte Einbindung abgesichert. Der erneute Android-Test mit geloeschten App-Daten zeigt jedoch, dass dieser Doppel-Bootstrap nicht die alleinige Ursache der leeren Nachfuellseite war.
- [x] Verbleibenden Nachfuell-Zustand per Android-Video eindeutig eingegrenzt: Header/aktiver Marker bleiben auf `Nachfuellen`, waehrend die Schrittzeile auf `Vorgang auswählen.` springt und alle Legacy-Nachfuellkarten ausgeblendet sind. Dazu passt der aktuelle Codepfad `process-mode.js -> refreshAuth()`, der bei einem temporaer fehlenden Client-Auth-Abgleich seine private Variable `mode` auf `null` setzt.
- [x] Replenishment-Router-Guard umgesetzt: der bewusst gewaehlte UI-Modus `replenish` wird nur als nicht-fachlicher Navigationszustand in `sessionStorage` gemerkt. Wenn Shell/aktiver Marker weiterhin Nachfuellen anzeigen, Auth wieder gueltig ist und gleichzeitig `Vorgang auswählen.` beziehungsweise keine Legacy-Karten sichtbar sind, wird der private Routermodus ohne Buchung und ohne Verlust des bereits gescannten Nachfuellzustands erneut auf `replenish` gesetzt.
- [x] Kopierbares In-App-UI-Diagnoselogging umgesetzt. Es protokolliert technische UI-/Router-/Auth-Boolean-/Sichtbarkeits-/JavaScript-Fehlerzustaende, aber keine Passwoerter, Tokens, Connection Strings, Personalnummern oder Mitarbeiternamen. Bei erkannter leerer Prozessseite erscheint statt nur leerer Flaeche eine Diagnosekarte mit `Anzeige wiederherstellen` und `Diagnose kopieren`.
- [ ] `Nachfuellen` mit Service-Worker-v30-/Guard-/Diagnosestand auf Android mehrfach mit Tankscan testen. Falls der Zustand erneut auftritt, den kompletten Text aus `Diagnose kopieren` in den Projektchat uebernehmen.

## Aktueller STAGING-Korrekturstand 08.09.2026

Details zu den nach den Android-Tests umgesetzten Korrekturen stehen in `docs/STAGING_TEST_FIXES_2026-09-08.md`.

- [x] Ziel-Lagerort und interner Ziel-Lagerplatz fuer `Pulver aus Tank auslagern` werden per AJAX aus serverseitig rein lesenden Oxaion-SQL-Treffern ausgewaehlt; frei getippter Text wird nicht als kanonischer Buchungsschluessel uebernommen. Die wirksame LF-Buchung validiert Lagerort und Lagerplatz weiterhin unmittelbar vor dem Schreiben ueber die bestaetigten Oxaion-F4-Wege `US16601R` beziehungsweise `LB13210R`.
- [x] Die bisher nur fuer die RP.*-Bestandsansicht dokumentierte Oxaion-SQL-Ausnahme wurde fuer diese spezifische rein lesende Zielort-/Lagerplatzsuche erweitert. SQL bleibt eine Bedienhilfe; Materialbuchungen erfolgen weiterhin ausschliesslich ueber Oxaion HTTP/Fachlogik.
- [x] `BEL1422` bei der ersten LF-Position eines leeren Materialbelegs technisch behoben: `BookAsync` und `BookFillNewAsync` verwenden fuer Position 1 den aus dem erfolgreichen JET-Mitschnitt bestaetigten `LB20115J *LOAD/*NEW`-First-LF-Ablauf.
- [x] RP.*-Lagerbestand: `CommandBehavior.SequentialAccess` entfernt, weil die Mapper-Spalten per Namen ausserhalb strenger Projektionsreihenfolge gelesen werden. `CommandBehavior.Default` verhindert den im Android-Test beobachteten Spaltenordinalfehler.
- [x] FA-MK-Endpruefung verschaerft: vor `PW22031J *PUTNEW` wird der bestaetigte `TCODE=ELSE`-Zustand verlangt; nach `PUTNEW` wird nur lesend und begrenzt erneut geprueft. Die MK-Buchung selbst wird dabei niemals wiederholt.
- [ ] Tankauslagerung mit AJAX-ausgewaehltem Ziel bis zur erfolgreichen LF/LE-Verifikation erneut live in STAGING bestaetigen.
- [ ] Neue Tankbefuellung mit der kombinierten Ein-Beleg-Kette `LF/LE -> LM/LN` nach dem First-LF-Fix erneut live in STAGING bestaetigen.
- [ ] FA-MK-Buchung mit `TCODE=ELSE`-Gate und verzoegerter rein lesender Endzustandspruefung erneut live in STAGING bestaetigen.
- [ ] RP.*-Lagerbestandsansicht auf Android nach dem SQL-Reader-Fix erneut live bestaetigen.

## Oxaion-Integration

- [x] HTTP-Buchungsfolge fuer den getesteten Vorgang `alte Mix-Charge + neue Pulvercharge -> neue Mix-Charge` bestaetigt und im STAGING-Prototyp umgesetzt; Details siehe `docs/STAGING_REAL_MIX_PROTOTYPE.md`
- [x] JET-Datenstrom fuer `Chargen pro Lagerort` identifiziert und im Backend als lesender STAGING-Prototyp umgesetzt; Programme/Felder siehe `docs/OXAION_MACHINE_STOCK_LOOKUP.md`
- [x] Filterbedingung technisch aus der Selektionsmaske bestaetigt: `LLAWEP.LALABE <> 0`. Der Backend-Prototyp haengt nicht mehr von einem gespeicherten Filter `mit Bestand`, dessen Freigabe oder Filter-ID ab, sondern liest die vollstaendige Lagerortliste und wertet diese Bedingung direkt aus.
- [x] Artikelunabhaengige Sicht auf den Maschinen-Lagerort im Referenzdatenstrom bestaetigt: die ungefilterte `LB30230R *FIRSTLIST` fuer `EOS1` lieferte 25 Zeilen verschiedener Artikel inklusive Nullbestaenden und `<STOP/>`. Dadurch koennen `Maschine leer`, `anderes Pulver vorhanden` und `mehrere Bestaende` unterschieden werden.
- [x] Serverseitiger Start der Bestandsabfrage mit leerer Eltern-`SSID` in STAGING live bestaetigt: der vom Backend gestartete Lesefluss ermittelt den aktuellen EOS1-Bestand erfolgreich.
- [ ] Maschinengetriebene Artikelableitung live bestaetigen: der neue Ablauf startet dieselbe `LB30230R`-Lagerortauskunft ohne vorgegebenen Artikel (`I_TIDF/TIDF` leer) und leitet Artikel/Bezeichnung aus der einzigen positiven Tankposition ab. Der bisherige Referenzdatenstrom zeigt bereits, dass die Liste selbst artikelunabhaengig ist; der leere Artikel im Startkontext ist noch einmal real in STAGING zu pruefen.
- [x] Mehrere Nachfuellchargen als ein fachlicher Vorgang im Backend/Frontend umgesetzt; Positions-, Verifikations- und Recovery-Logik sind dynamisch. Details siehe `docs/MULTI_BATCH_REPLENISHMENT.md`.
- [x] Multi-Batch-Verallgemeinerung fuer Position 3+ am 04.09.2026 real in Oxaion STAGING bestaetigt: zwei Nachfuellpositionen mit derselben Charge `52918` von zwei unterschiedlichen Lagerplaetzen wurden in einem Vorgang gebucht. Der Lagerbeleg `FA26MB00042` wurde mit genau 3 Positionen und 6 erwarteten LM/LN-Bewegungen vollstaendig verifiziert und anschliessend explizit geschlossen.
- [x] Nachfuellquellen-Auswahl aus Oxaion technisch bestaetigt: `LB30340R` liefert Lagerorte/Chargen pro Artikel, `LB30430R` liefert exakte interne Lagerplatzschluessel/Chargen/Bestaende; Lagerorttexte werden ueber `US00006J *GETPLAIN` gelesen. Details siehe `docs/OXAION_SOURCE_STOCK_LOOKUP.md`.
- [x] RP-Lagerbestandsansicht am 07.09.2026 technisch korrigiert: ein direkter Wildcardwert `RP.*` in `TIDF/I_TIDF` der Unterprogramme wird von Oxaion mit `IDN1823` abgelehnt. `RP.*` darf dort nicht als Identnummer verwendet werden. Der Gesamtbestand steigt deshalb wieder ueber die gefilterte `LB30210R`-Liste `Chargen je Firma` ein und verwendet danach nur konkrete `RP.xxxxx`-Artikelnummern; Details siehe `docs/INVENTORY_VIEW.md`.
- [x] Exakte Filterfolge fuer `Chargen je Firma` am 08.09.2026 per JET-Mitschnitt bestaetigt: `MN10209J *CHKCMD CF` -> `LB30210R *GETHDR/*FIRSTLIST reset` -> `LB30210 *SAVALLSLT` mit `NAME=IDNR.TLIDNR`, `V_TLIDNR=RP.*`, leerem `B_TLIDNR` -> `LB30210R *GETU01` -> `*FIRSTLIST replace`. Die resultierende Liste enthielt 21 konkrete RP-Zeilen, nur Bestand `<> 0` und `<STOP/>`. Der Bestandfilter ist im Referenzkontext als `mit Bestand` aktiv; die erweiterte JET-Filterdarstellung bestaetigt `UPOWEP.POLABE = 0` in der Ausschlussselektion `*SAVLSTA`. Das Backend setzt den scalar uebertragbaren RP-Filter und validiert danach zwingend RP.*, Bestand `<> 0` und `<STOP/>`; Details siehe `docs/INVENTORY_VIEW.md`.
- [x] Chargen-QR-getriebene Quellenwahl umgesetzt: der Bediener scannt `Artikel+++Charge`; die PWA ermittelt die positiven Oxaion-Bestandspositionen ueber die bestaetigten Auskunftswege. Bei genau einer Position wird der Entnahmeort automatisch uebernommen, bei mehreren Positionen muss der tatsaechliche Lagerort beziehungsweise Lagerplatz bestaetigt werden. Die serverseitige Pre-Write-Revalidierung bleibt unveraendert verbindlich.
- [x] Mehrfachscan derselben Charge fachlich auf die exakte Oxaion-Bestandsposition korrigiert: dieselbe Chargennummer darf mehrfach verwendet werden, wenn Lagerort/Lagerplatz unterschiedlich sind. Bereits verwendete exakte Positionen werden beim Folgescan ausgeblendet; `SourceStockService.ValidateSourcesAsync` blockiert weiterhin doppelte exakte Positionen serverseitig.
- [x] Mehrfachscan derselben realen Charge auf zwei Lagerplaetzen am 04.09.2026 im Android-STAGING-Stand live bis zur erfolgreichen Multi-Position-Buchung bestaetigt. Beim zweiten Scan wurde die bereits verwendete exakte Position ausgeschlossen und der verbleibende Lagerplatz verwendet.
- [x] Sachmerkmals-Datenstrom fuer die Artikel-Erkennungsfarben bestaetigt: `US17000J *SAVKEY` -> `US17000J *PROPERTY` -> `US21001J *LOAD` -> `US21000R *GETHDR` -> `US21000R *FIRSTLIST`. Fuer `RP.00010` liefert `UYASMP.ASSMMN/ASSMMA` die Werte `EFA01=0D0D0D` und `EFA02=7030A0`; `_INTERN.SMMABZ` liefert `Schwarz` und `Violett`. Details siehe `docs/OXAION_ARTICLE_RECOGNITION_COLORS.md`.
- [x] Backend-Sachmerkmalsabruf fuer `EFA01/EFA02` am 03.09.2026 live aus der PWA bestaetigt; das zweigeteilte Erkennungsfarbfeld wird nach dem Tankscan korrekt angezeigt. Der aktuelle sichere Ablauf akzeptiert nur einen vollstaendigen `US21000R *FIRSTLIST` mit `<STOP/>`; fuer einen kuenftigen paginierten Sachmerkmalsfall wird ohne realen Mitschnitt kein `*NEXTLIST` erfunden.
- [x] Fehlerursache `LAP1258` fuer den Referenzfall `H04HRL` geklaert: Oxaion erwartet als `PSLAPL` den internen Schluessel `RE1F3`; eine visuell formatierte Eingabe wie `RE1  F 3` darf nicht als Buchungsschluessel verwendet werden.
- [x] Lagerorte ohne Lagerplatzorganisation technisch erkannt: `LAG1626` ist im Datenstrom bestaetigt. Nur fuer diesen eindeutigen Fall bleibt der Lagerplatz leer und der bestaetigte `LB30230R`-Lagerortbestand verwendet.
- [x] Neue Nachfuellquellen-Auswahl in STAGING live bestaetigt: Oxaion-geführte Lagerort-/Lagerplatz-/Chargenauswahl funktioniert im realen STAGING-Test.
- [x] Personalpruefung aus realen JET-Datenstroemen rekonstruiert: fuer die WebApp sind `PEPENU` als Personalnummer und `PEPENA` als vollstaendiger Name bestaetigt. Zusaetzlich ist der feldbezogene exakte Filterweg `US14001R *GETFILTER` -> `US14001 *SAVCURSET` -> `US14001R *GETSLTV/*GETSLTATR/*CHKSLTV` mit `IPENU=0000000450` -> `US14090J *FIRSTLIST` mit `FROM_PGMN=MAINFILTER` bestaetigt. `PESAKZ` und `PENLAE` werden nicht verwendet. Details siehe `docs/OXAION_PERSONNEL_LOOKUP.md`.
- [x] Neue AJAX-Personalsuche in STAGING live bestaetigt: Eingabe `45` liefert nur normalisierte `PEPENU` mit Praefix `45`; die Anzeige verwendet `PEPENU - PEPENA`. Treffer ueber Kostenstelle/andere Felder sowie `PESAKZ`/`PENLAE` werden nicht fuer die Anzeige verwendet.
- [x] Exakte erneute serverseitige Personalpruefung ueber `IPENU` vor einer Buchung am 03.09.2026 live in STAGING bestaetigt. Der bekannte Sonderfall bei `US14001 *SAVCURSET` bleibt bestehen: nach erfolgreichem HTTP-Aufruf kann die Antwort nicht als XML parsebar sein. Der Backend-Fix toleriert nur diesen spezifischen XML-Parsefehler bei `SAVCURSET`; danach muessen `GETSLTV`, `GETSLTATR`, `CHKSLTV`, `FIRSTLIST` und der exakte `PEPENU`/`PEPENA`-Abgleich erfolgreich sein. Transport-/HTTP-Fehler sowie alle Fehler in den nachfolgenden Pruefschritten bleiben sperrend. Die native Oxaion-Syntax fuer einen Praefixfilter direkt in `IPENU` ist weiterhin nicht bestaetigt und wird nicht erfunden.
- [x] Syncos-Zuordnung fuer NFC-Personal fachlich festgelegt: `ITSUSER.RFID` ist die alphanumerische Chip-ID und `ITSUSER.ObjectKey` die zehnstellige Personalnummer mit fuehrenden Nullen; Beispiel `RFID 54320466 -> ObjectKey 0000000446 -> Personalnummer 446`. Reader-Trennzeichen wie `:`, `-` oder Leerzeichen werden entfernt, der RFID-Inhalt wird aber weder numerisch noch als Hex-Wert interpretiert. Nur `ClassID=47`, `IsEnabled=-1`, `IsVisible=-1` werden akzeptiert. Details siehe `docs/NFC_PERSONNEL_LOOKUP.md`.
- [x] NFC-Personalidentifikation am 03.09.2026 live im eingesetzten Android-/Browser-Testaufbau bestaetigt: Personalchip wird gelesen, gegen Syncos aufgeloest, anschliessend ueber den bestaetigten Oxaion-`IPENU`-Ablauf bestaetigt und im Frontend automatisch ausgewaehlt.
- [x] Manueller Login-Fallback mit Personalnummer und SYNCOS-Passwort am 04.09.2026 im aktuellen gefuehrten Android-Teststand live bestaetigt.
- [ ] NFC-Login im kombinierten Stand mit direkt gesetzter Backend-Personal-Session nach der bereits bestaetigten RFID-/Oxaion-Pruefung nochmals live bestaetigen.
- [ ] Zusaetzlich mindestens einen realen Syncos-RFID-Wert mit Buchstaben `A-F` im End-to-End-Test bestaetigen; die Implementierung behandelt RFID bereits verbindlich als alphanumerischen String ohne Hex-Konvertierung.
- [ ] Sperrfreigabe nach erfolgreicher Abschlussverifikation live bestaetigen: am 02.09.2026 blieb der von der App erfolgreich gebuchte Lagerbeleg nach dem erneuten `*OPEN` zur Verifikation gesperrt. Der Backend-Fix sendet nach der finalen `FIRSTLIST`-Pruefung ein zweites explizites `LB20100J *END` und setzt erst danach `SUCCESS`. Der erfolgreiche 3-Positions-Test vom 04.09.2026 bestaetigt, dass das explizite Schliessen ohne Fehler zurueckkam; weiterhin separat pruefen, dass der Beleg unmittelbar danach im Oxaion-Dialog tatsaechlich nicht mehr gesperrt ist.
- [x] Normale FA-Materialrueckmeldung `MK` technisch identifiziert und im Backend umgesetzt: `PW22000J`/`PW22031J`, Pre-Write-Revalidierung von Tank, `AMMATV` und `AMMPST`, Statusgate 0/1/8 und exakte Endzustandspruefung. Details siehe `docs/FA_CONSUMPTION_PROCESS.md` und `docs/STAGING_TEST_FIXES_2026-09-08.md`.
- [ ] Vollstaendig erfolgreichen `MU`-Mitschnitt aufnehmen und Pflichtfelder/Recovery bestaetigen, bevor ungeplanter Materialverbrauch schreibend implementiert wird.
- [x] Tanklager -> Pulverlager fuer den bestaetigten Tankauslagerungsfall technisch als `LF` mit automatisch erzeugter `LE`-Gegenbewegung identifiziert; Position 1 verwendet den bestaetigten First-LF-Ablauf.
- [x] Pulverlager -> leerer Tank fuer die erste Befuellposition technisch als `LF` mit automatisch erzeugter `LE`-Gegenbewegung identifiziert; die Charge bleibt in diesem ersten Schritt erhalten.
- [ ] Kombinierte Neubefuellung `LF/LE -> LM/LN` in einem Beleg nach dem First-LF-Fix live End-to-End bestaetigen; danach bei Mehrfachquellen Position 3 ff. ebenfalls live bestaetigen.
- [x] Chargenumbuchung fuer den getesteten Nachfuell-/Mix-Vorgang mit `LM` und automatisch erzeugtem `LN` bestaetigt
- [x] Geeignete WebApp-/Backend-Transaktionsreferenz fuer den STAGING-Prototyp in den vorhandenen Oxaion-Freitextfeldern dokumentiert; finale produktive Referenz-/Suchstrategie noch bewerten
- [x] Belastbare Ergebnisabfrage fuer den getesteten Mix-Beleg ueber erneutes Oeffnen und `LB20110R *FIRSTLIST` umgesetzt; fuer die FA-MK-Buchung bleibt mangels eindeutiger WebApp-Transaktionsreferenz bei unklarem Ausgang weiterhin manuelle Klaerung erforderlich.
- [x] Bewusster neuer Versuch nach eindeutigem `REJECTED` umgesetzt: neue `clientOperationId`, identische Buchungsdaten und Verknuepfung ueber `retryOfClientOperationId`; kein Retry derselben abgelehnten Transaktion

## Fachliche Entscheidungen

- [x] Mix-Chargenschema fuer neue Mix-Chargen festgelegt: `<Artikel ohne Punkt>MIX_<yyyyMMdd>_<HHmmss>`, z. B. `RP00010MIX_20260902_162312`. Die Nummer wird automatisch erzeugt und nicht frei editiert.
- [x] Nachfuell-Buchungstext festgelegt: `Pulver nachfuellen <Maschinen-Lagerort>`, dynamisch aus der ausgewaehlten Maschine.
- [x] Buchungsdatum beim neuen Nachfuellvorgang ist immer das aktuelle Datum und keine Bedienereingabe; `Mix Charge erstellt am` wird aus dem aktuellen Erzeugungszeitpunkt der neuen Mix-Charge gebildet.
- [x] Artikel und Artikelbezeichnung werden beim Nachfuellen aus dem eindeutigen aktuellen Maschinenbestand abgeleitet und sind keine Bedienereingaben.
- [x] Die Sachmerkmale `EFA01` und `EFA02` des aus dem Maschinentank abgeleiteten Artikels werden als visuelle Erkennungshilfe angezeigt: quadratisches Farbfeld, linke Haelfte `EFA01`, rechte Haelfte `EFA02`; Farbbezeichnung und HEX-Wert werden zusaetzlich als Text ausgegeben. Die Farben sind keine Buchungsschluessel und ihr Ausfall blockiert keine ansonsten valide Materialbuchung.
- [x] Der aktuelle Maschinen-Tanklagerort darf nie als Quelllager einer Nachfuellcharge verwendet werden.
- [x] Mitarbeiter-Anmeldung: NFC ist der bevorzugte Loginweg. Nach erfolgreicher Syncos-RFID-Zuordnung und exakter Oxaion-Personalpruefung setzt das Backend direkt die Personal-Session. Fallback ist Personalnummer-Auswahl in Oxaion plus SYNCOS-Passwortpruefung. Beide Wege fuehren in dieselbe serverseitige Session; Details siehe `docs/PERSONNEL_AUTHENTICATION.md`.
- [x] 05.10.2026 User-Idle-Timeout technisch umgesetzt: lokal am Server einstellbar (5-1440 Minuten, Default 480), serverseitige Ablaufpruefung vor Requests, Verlaengerung nur durch explizite Bedieneraktivitaet ueber `/api/personnel/activity`, lokale PWA-Abmeldung und Session-Pruefung bei Rueckkehr aus dem Hintergrund.
- [x] 06.10.2026 Android/PRODUCTION mit `0.1.5` live bestaetigt: der serverseitig eingestellte User-Timeout funktioniert im Praxistest; automatische Abmeldung bei Inaktivitaet und Verlaengerung bei Bedieneraktivitaet sind bestaetigt.
- [x] 06.10.2026 Nachfuellen/Leertank technisch korrigiert: Backendstatus `EMPTY` wird im Vorgang `Pulver nachfuellen` nicht mehr als mehrdeutiger Tankbestand dargestellt, sondern mit `Tank ist leer.`. Auch die spaeteren Buchungs-Guards unterscheiden `EMPTY` explizit von `AMBIGUOUS`.
- [x] 06.10.2026 Android-Test von `0.1.6` ausgewertet: `Tank ist leer.` erschien noch nicht. Ursache technisch nachgewiesen: `refreshMachineStock()` setzte `machineStock` ueber `clearMachineInfo()` auf `null`, bevor der `EMPTY`-Status fuer die Bedienermeldung ausgewertet wurde. Der Diagnoseexport zeigte deshalb ebenfalls `machineStockStatus:""`.
- [x] `0.1.7` technisch umgesetzt: gelesene Tankantwort wird vor dem UI-Clear als `resolvedStock` erhalten; `EMPTY` kann dadurch sicher `Tank ist leer.` anzeigen. Diagnose erweitert um aktive Script-/Service-Worker-Version, Cachegeneration, sichtbaren `stockStatus` und letzte bereinigte Tank-API-Antwort.
- [x] 07.10.2026 Android/STAGING mit `0.1.7` praxisbestaetigt: der Leertank-Fall funktioniert wie vorgesehen; `Tank ist leer.` wird korrekt angezeigt. Der Stand ist fuer die Uebernahme nach `main` freigegeben.
- [x] 07.10.2026 Menuebezeichnungen und Kurzbeschreibungen fuer Produktionspersonal fuer `0.1.8` umgesetzt:
  - `Tank nachfuellen` — `Vorhandenes Pulver im Tank ergaenzen.`
  - `Pulververbrauch erfassen` — `Verbrauchtes Pulver einem Fertigungsauftrag zuordnen.`
  - `Tank entleeren` — `Pulver vollstaendig aus dem Tank ins Pulverlager zurueckgeben.`
  - `Leeren Tank befuellen` — `Leeren Tank mit neuem Pulver befuellen.`
  - `Jobabbruch. Verbrauch korrigieren` — `Pulververbrauch nach einem abgebrochenen Druckjob berichtigen.`
  - `Bestaende anzeigen` — `Aktuelle Tank- und Pulverlagerbestaende anzeigen.`
  - `Etiketten nachdrucken` — `Etiketten einer abgeschlossenen Tankauslagerung erneut drucken.`
  - Die Aenderung betrifft nur sichtbare Bedienertexte; interne Prozess-IDs und Buchungslogik bleiben unveraendert.
- [x] Syncos-RFID wird als alphanumerischer String behandelt. Die Web-NFC-/Reader-Darstellung darf lediglich von Trennzeichen wie `:`, `-` oder Leerzeichen bereinigt werden; keine Dezimal-, Hex- oder Byte-Reihenfolgen-Konvertierung.
- [x] Bei NFC-Chiperkennung erzeugt die PWA unmittelbar ein kurzes Tonsignal ueber die Browser-WebAudio-API; es wird keine Audio-Datei benoetigt. Das Tonsignal bestaetigt nur die Chiperkennung, nicht bereits die erfolgreiche Syncos-/Oxaion-Zuordnung.
- [x] Maschinentankwahl im aktuellen STAGING-Nachfuellprozess erfolgt per QR. Der Tank-QR enthaelt ausschliesslich den Oxaion-Tanklagerort, z. B. `EOS1`, und muss gegen die gepflegte Maschinen-/Tankliste validiert werden. Die sichtbare manuelle Tankauswahl wurde aus dem normalen Ablauf entfernt.
- [x] Chargen-QR im aktuellen Nachfuellprozess hat das Format `Artikel+++Charge`. Lagerort und interner Lagerplatz werden daraus nicht frei abgeleitet, sondern aus den aktuellen positiven Oxaion-Bestandspositionen ermittelt. Bei Mehrdeutigkeit bestaetigt der Bediener den tatsaechlichen Entnahmeort.
- [x] Dieselbe Chargennummer darf in einem Nachfuellvorgang mehrfach gescannt werden, wenn sie auf unterschiedlichen positiven Oxaion-Bestandspositionen liegt. Duplicate Prevention bezieht sich auf die exakte Kombination Lagerort/Lagerplatz/Charge, nicht allein auf die Chargennummer.
- [x] Ein falscher beziehungsweise nicht zulaessiger Chargenscan wird nicht als Nachfuellcharge angelegt. Stattdessen erscheint eine seitendeckende Meldung mit Soll-Farbe und Scanwert, die bewusst mit `Verstanden` bestaetigt werden muss; der Fehlscan wird als separates WebApp-Auditereignis protokolliert.
- [x] QR-Kamera und QR-Erkennung sind bewusst getrennt: Scanner-Dialog oeffnet zuerst nur das Kamerabild und erlaubt das Einstellen des Zooms; `BarcodeDetector` und Scan-Laser starten erst nach bewusstem Druck auf `Scannen`. Damit werden Fehlscans beim Oeffnen/Ausrichten reduziert.
- [x] Der Kamera-Zoom ist eine dauerhafte lokale UI-/Geraetepraeferenz und wird fuer denselben Browser-Origin ueber Buchungsvorgaenge und App-Neustarts hinweg wiederhergestellt, soweit die Kamera Zoom unterstuetzt. Der gespeicherte Hardware-Zoom wird vor dem Binden des Streams an das sichtbare Video angewendet, um den sichtbaren Sprung vom Standardzoom zu vermeiden. Er ist kein fachlicher Zustand.
- [x] Beim Chargenscan bleiben sowohl SOLL- als auch IST-Farbkaestchen sichtbar. Bei korrektem Artikel verwendet die IST-Seite dieselben aus Oxaion geladenen EFA01/EFA02-Erkennungsfarben; bei einem falschen Artikel werden niemals die Sollfarben faelschlich als Istfarben dargestellt, sondern unbekannte Istfarben neutral gekennzeichnet. Die Freigabe basiert weiterhin auf Artikel/Charge/Oxaion-Bestand, nicht auf Farbe.
- [x] Mitarbeiteransicht und Dev-Ansicht verwenden dieselbe PWA. `Dev-Infos` aendert nur die Sichtbarkeit technischer Informationen und niemals fachliche Freigaben oder Backend-Pruefungen. Der jeweils naechste Bedienerschritt wird optisch hervorgehoben; Details siehe `docs/WORKER_GUIDED_UI.md`.
- [x] Nach dem Tankscan zeigt die Mitarbeiteransicht bekannte positive Lagerorte und ggf. Lagerplaetze mit passendem Pulver als reine Suchhilfe. Die tatsaechliche Buchungsquelle wird weiterhin erst aus dem Chargen-QR und dem aktuellen Oxaion-Bestand bestimmt und vor dem Schreiben erneut validiert.
- [x] Einfuellmengen fuer Nachfuellchargen werden niemals vorbelegt. Jede Menge muss vom Mitarbeiter bewusst eingegeben werden. Eine weitere Nachfuellcharge wird im gefuehrten Ablauf erst freigegeben, wenn die aktuelle Charge inklusive Entnahmeort und Menge vollstaendig ist. Der Button fuer die weitere Charge steht unterhalb der bereits erfassten Charge(n). Solange das Mengenfeld den Tastaturfokus hat, darf die Schrittsteuerung den Scroll-/Eingabefokus nicht zum Buchungsbutton verschieben.
- [x] Die Mitarbeiter-Zusammenfassung vor `Buchung starten` zeigt pro Nachfuellcharge neben Charge und Menge auch den bestaetigten Oxaion-Lagerort und gegebenenfalls den internen Lagerplatz.
- [x] Buchungsergebnisse werden im Mitarbeitermodus seitendeckend und bestaetigungspflichtig dargestellt. `SUCCESS` wird erst nach vollstaendiger Backend-/Oxaion-Verifikation als Erfolg angezeigt; nach `Verstanden` wird nur der abgeschlossene Vorgang geleert, die Mitarbeiter-Session bleibt bestehen und der naechste Ablauf beginnt wieder beim Maschinentank-Scan. `REJECTED`, `UNCERTAIN`, `MANUAL_REVIEW_REQUIRED` und nicht eindeutig einordenbare Verbindungsabbrueche behalten die dokumentierte Recovery-/Kein-Blind-Retry-Logik.
- [ ] Produktionsmaschinen-ID aus dem Fertigungsauftrag, z. B. `EP-M650-1`, verbindlich einem Maschinentank/Oxaion-Lagerort zuordnen. Dabei muss eine kurzfristige Aenderung der tatsaechlichen Produktionsmaschine weiterhin sicher moeglich sein. Eine Zuordnung wird bis zur Bestaetigung nicht erfunden.
- [ ] Pruefen, ob der Soll-Entnahmelagerort der Materialposition im Fertigungsauftrag eine geeignete und bei kurzfristiger Maschinenumplanung korrekt aenderbare Datenquelle fuer die Tankvorgabe ist.
- [ ] Online-Beauftragungsprozess fuer `Tank nachfuellen` und `Tank wechseln` durch Produktionsleitung beziehungsweise Stellvertretung definieren. Bis dahin gilt der bestehende organisatorische Uebergangsprozess mit muendlicher beziehungsweise papierbasierter Beauftragung; der alte vierteilige Entnahmeschein-QR ist fuer die neue Online-PWA keine verbindliche Buchungswahrheit.
- [ ] Kompatibilitaetsregeln fuer vorhandenes Pulver und Mix-Chargen ueber die aktuelle Artikelgleichheit hinaus festlegen
- [ ] Reihenfolge, Atomaritaet und Verhalten bei Teilfehlern des Pulverwechsels festlegen

## Lagerplatz-Umlagerung aus Lageruebersicht 07.10.2026

- [x] Fachliche Entscheidung: positive RP.*-Positionen mit konkretem Lagerplatz koennen direkt aus der Pulverlagerliste fuer `Umlagern` ausgewaehlt werden.
- [x] Menge wird mit der vollstaendigen aktuell angezeigten Positionsmenge vorgeschlagen und darf vor der Buchung reduziert, aber nicht erhoeht werden.
- [x] Artikel, Charge und Quellposition sind aus der angeklickten Lagerposition fest vorgegeben; kein freies Aendern dieser Buchungsschluessel.
- [x] Ziellagerort/-lagerplatz werden aus Backend-/Oxaion-Treffern gewaehlt. Die exakte Quellposition ist als Ziel unzulaessig.
- [x] Maschinentank-Lagerorte (`ULGSTP.LGLGART='02'`) sind als Umlagerungsziel verboten. UI filtert sie aus, Backend prueft die Tankdefinition zusaetzlich fail-closed.
- [x] Technische Umsetzung verwendet den bestehenden bestaetigten `LF -> LE`-Materialtransfer: eine Position, Charge bleibt unveraendert, exakte Bewegungsverifikation nach dem Schreiben.
- [x] Vor dem Schreiben werden Mitarbeiter, exakte Quellposition, seit Anzeige erwarteter Quellbestand, verfuegbare Menge sowie Ziel-Lagerort/-lagerplatz erneut serverseitig validiert.
- [x] Umlagerung besitzt eigene `clientOperationId`, eigenen Transaktionsstatus und read-only Reconcile; bei `UNCERTAIN`/`MANUAL_REVIEW_REQUIRED` kein automatischer erneuter Buchungsversuch.
- [x] Umlagerung ist online-only; keine automatische Offline-/Outbox-Buchung aus einem veralteten Lagerbestand.
- [x] 07.10.2026 Android/STAGING: reale Lagerplatz-Umlagerung aus der Lageruebersicht vom Benutzer erfolgreich bestaetigt; der neue Vorgang funktioniert im Praxistest.
- [x] 07.10.2026 UX-Entscheidung fuer `0.1.9`: `Umlagern` wird nicht mehr direkt in jeder Lagerzeile angezeigt. Ein Tipp auf die Pulverlagerposition oeffnet zuerst `Lagerplatzdetails` mit Artikel, Bezeichnung, Lagerort, Lagerplatz, Charge und Bestand; nur dort wird bei zulaessigen Positionen `Umlagern` angeboten.
- [x] 07.10.2026 Android/STAGING mit `0.1.9` praxisbestaetigt: Pulverlagerzeile antippen, `Lagerplatzdetails` anzeigen und `Umlagern` erst aus dem Popup starten funktioniert wie vorgesehen.
- [ ] Erweiterten STAGING-Detailtest bei Gelegenheit nachholen: volle vorgeschlagene Menge und reduzierte Teilmenge jeweils pruefen und den Oxaion-Beleg auf exakt `LF` Quelle + `LE` Ziel mit gleicher Charge/Menge kontrollieren.
- [ ] STAGING-Sicherheitstest: Tanklager darf in der Zielauswahl nicht erscheinen; manipulierter Request mit Tanklager als Ziel muss serverseitig abgelehnt werden.
- [ ] STAGING-Konflikttest: Lagerbestand nach Laden der Uebersicht extern veraendern; vorbereitete Umlagerung muss wegen veraenderter erwarteter Quellmenge ohne Schreibvorgang stoppen.

## Erkennungsfarben Tankkarten 02.10.2026

- [x] PRODUCTION-API fuer `RP.00024` bestaetigt: EFA01 `FF0000 / Rot`, EFA02 `833C0C / Braun`, Status `COMPLETE`, identisch fuer `M400-02` und `M650`.
- [x] Ursache des leeren Tank-Farbfelds gefunden: CSS-Spezifitaet setzte `.processSwatch` innerhalb `.inventoryTank` auf `display:block`, waehrend dieselbe Farbe in der Pulverlagerkarte korrekt war.
- [x] Tankkarten-Swatch auf explizites Flex-Layout korrigiert; innere Farbhaelften erhalten volle Hoehe.
- [x] PWA-Cache auf neue Generation angehoben und alte `fam-pulver-*` Cache-Generationen werden bei Aktivierung entfernt.
- [x] 05.10.2026 Android/PRODUCTION nach MSI-Update auf `0.1.3`: Die Farbfelder der Maschinentanks werden korrekt dargestellt; der zuvor leere Farbrahmen bei vorhandenen EFA01/EFA02-Farben ist damit live behoben.
- [ ] Android/PRODUCTION separat noch bestaetigen, dass ein Artikel ohne gueltige EFA01/EFA02-Farben (Referenz `RP.00026`) weiterhin **kein** leeres Farbfeld anzeigt.

## Anwendung und Betrieb

- [x] Mitarbeiter-Authentifizierung fuer den aktuellen PWA-Ablauf festgelegt: bevorzugt NFC, alternativ Personalnummer + SYNCOS-Passwort, serverseitige Session und zusaetzliche exakte Oxaion-Personalrevalidierung vor der Materialbuchung. Produktive HTTPS-/Rolloutdetails bleiben separat offen.
- [x] 02.10.2026 Serverhosting festgelegt und umgesetzt: ASP.NET-Core-Backend als Windows-Dienst `FAMPulverentnahme` hinter IIS, Hauptdienst auf Loopback `127.0.0.1:5080`, lokale Administrationsoberflaeche auf `127.0.0.1:5081`.
- [x] 02.10.2026 Laufzeit-Secrets fuer den Dienstbetrieb festgelegt: Oxaion-Benutzer/-Passwort sowie eine gemeinsame native SQL-Anmeldung werden lokal administriert und per Windows-DPAPI LocalMachine in `%ProgramData%\FAM-Pulverentnahme\service-config.json` gespeichert. Keine Secrets im Frontend/Repository.
- [x] 02.10.2026 STAGING/PRODUCTION-Umschaltung umgesetzt: Syncos verwendet `syncos_stg_102` bzw. `syncos_prd_102`; Oxaion-SQL-Katalognamen werden fuer beide Umgebungen explizit konfiguriert; Oxaion HTTP schaltet zwischen den bestaetigten Ports 11118/11108. Produktivumschaltung benoetigt eine bewusste Bestaetigung.
- [x] 02.10.2026 Umgebungswechsel abgesichert: bestehende Mitarbeiter-Sessions werden ungueltig; serverseitige Transaktions-/Auditdateien sind nach STAGING/PRODUCTION getrennt. Historische ungetrennte Dateien werden einmalig dem bisherigen STAGING-Kontext zugeordnet.
- [x] 02.10.2026 MSI-Installationsprojekt angelegt und als zukuenftiger verbindlicher APP-01-Deploymentweg festgelegt: jede auszuliefernde Serverversion als `FAM-Pulverentnahme-Setup-<Version>-x64.msi`; vorhandenen Dienst vor Dateiaustausch stoppen und nach erfolgreichem Upgrade automatisch wieder starten. Legacy-ZIP/PowerShell nur noch fuer Entwicklung/Diagnose.
- [x] 02.10.2026 GitHub-Actions-Artefaktspeicher bereinigt und automatische Retention umgesetzt: Nach erfolgreichem MSI-Build bleiben nur die Artefakte des aktuellen Builds erhalten; aeltere MSI-/STAGING-Artefakte werden repositoryweit geloescht. Beim ersten Lauf wurden 5 Artefakte gefunden, 3 alte geloescht und 2 aktuelle behalten.
- [ ] APP-01-Livetest des MSI-Upgrades: vorhandenen Prozess/Dienst ersetzen, Stop/Start-Verhalten pruefen und bestehenden IIS-Reverse-Proxy auf `127.0.0.1:5080` bestaetigen.
- [ ] Lokale Admin-Konfiguration auf APP-01 befuellen und testen: gemeinsamer SQL-Server/-Benutzer, Oxaion-STAGING-/PRODUCTION-SQL-Katalognamen, Oxaion-Benutzer/-Passwort; danach Verbindungstest fuer Syncos SQL, Oxaion SQL und Oxaion HTTP.
- [ ] STAGING/PRODUCTION-Umschaltung live pruefen: sichtbares STG/PROD-Kennzeichen, erzwungene Neuanmeldung und korrekte Datenbank-/Oxaion-Zielwahl. Produktivbuchungen erst nach separater bewusster Freigabe testen.
- [ ] Nach korrekter STAGING-Konfiguration Lageruebersicht erneut pruefen: `RP.00010` und `RP.00012` muessen den direkt getesteten STAGING-SQL-Daten entsprechen; EFA01/EFA02 von `RP.00010` erneut bestaetigen.
- [ ] Persistenztechnik fuer produktives Transaktionslog, Idempotenz, Status und Audit Trail finalisieren; aktuell bleiben JSON-Dateien bestehen, sind aber nach STAGING/PRODUCTION getrennt.
- [ ] Aufbewahrungsdauer, produktiver Speicherort, Zugriffsrechte und Auswertung fuer das Fehlscan-Audit festlegen.
- [ ] Eindeutigkeitsbedingungen und Aufbewahrungszeit fuer Idempotenzdaten festlegen
- [ ] Timeoutwerte und Retry-Policy nach weiterer Analyse der Oxaion-Schnittstelle festlegen
- [ ] Datenschutz-, Berechtigungs- und Aufbewahrungskonzept fuer Protokolldaten festlegen
- [ ] Concurrency-/Sperrstrategie fuer den Zeitraum zwischen erfolgreicher Maschinen-/Quellbestands-Revalidierung und erster schreibender Oxaion-Materialbuchung festlegen; die aktuelle STAGING-Pruefung blockiert erkannte Aenderungen, ist aber noch keine atomare Reservierung

## PWA, Offline und Synchronisation

Die grundsaetzliche Entscheidung fuer PWA, Service Worker, IndexedDB, lokale Outbox, `clientOperationId`, serverseitige Revalidierung und kontrollierte Updates ist in `docs/OFFLINE_PWA.md` dokumentiert. Der aktuelle STAGING-Prototyp verwendet bereits `IndexedDB` fuer einen offenen `clientOperationId`-Vorgang und das Backend behandelt diese ID idempotent. Offen sind weiterhin die konkreten Betriebsparameter und Prozessgrenzen:

- [x] STAGING-PWA mit installierbarem Web App Manifest, lokalen 192x192-/512x512-App-Icons, maskierbarem Android-Icon, Favicon und Service-Worker-App-Shell konfiguriert; fuer Installation auf realen Android-Geraeten bleibt HTTPS am IIS/Reverse Proxy Voraussetzung.
- [x] 30.09.2026 PWA-Cache fuer Etikettendruck/Nachdruck korrigiert: `process-mode.js` hatte nach der Erweiterung noch dieselbe Asset-URL und der Service Worker dieselbe Cache-Generation wie vor dem Etikettendruck. Dadurch konnte ein installiertes Android-PWA trotz neuer Serverversion die alte Prozessoberflaeche ohne `Etiketten drucken?` und ohne `Etiketten nachdrucken` weiterverwenden. Asset-Version und Cache-Generation sind jetzt gemeinsam angehoben und durch Regressionstest abgesichert.
- [x] Vorhandener FAM-Personalchip auf dem aktuell eingesetzten Android-/Browser-Testaufbau ueber Web NFC lesbar und Ende-zu-Ende fuer die Personalauswahl bestaetigt. Ohne sicheren HTTPS-Kontext oder ohne lesbare UID bleibt der manuelle Login mit Personalnummer und Passwort verfuegbar.
- [x] Kamera-QR-Scanner fuer den aktuellen STAGING-Ablauf ohne externe Bibliotheken umgesetzt: `getUserMedia`, native `BarcodeDetector`-QR-Erkennung, sichtbarer Kameraausschnitt, Kamera-Zoom soweit vom Geraet unterstuetzt sowie browsergeneriertes Tonsignal/Vibration bei Erkennung. Die QR-Erkennung startet erst nach bewusstem Druck auf `Scannen`; reines Oeffnen/Ausrichten/Zoomen erkennt noch keinen Code.
- [x] Kamera-Zoom wird als reine nicht-fachliche UI-Praeferenz lokal gespeichert und beim naechsten Kameraoeffnen wieder angewendet; fachliche PWA-Daten bleiben davon getrennt in IndexedDB.
- [ ] Maximale Gueligkeitsdauer eines lokal gecachten Maschinenzustands fachlich festlegen
- [ ] Pro Buchungsszenario festlegen, welche Schritte offline bis `PENDING_SYNC` vorbereitet werden duerfen
- [ ] Entscheiden, ob Pulverwechsel offline nur erfasst oder teilweise vorbereitet werden darf
- [ ] Produktives IndexedDB-Schema, Store-Namen, Indizes und Versionierung festlegen
- [ ] Migrationsstrategie fuer IndexedDB so definieren, dass `PENDING_SYNC`-Vorgaenge bei App-Updates erhalten bleiben
- [ ] Reihenfolge und Abhaengigkeiten bei der Synchronisation mehrerer lokaler Vorgaenge definieren
- [ ] Verhalten bei vollem, vom Benutzer geloeschtem oder vom Browser bereinigtem lokalem Speicher festlegen
- [ ] Pruefen, ob `navigator.storage.persist()` auf den eingesetzten Android-Geraeten sinnvoll und ausreichend unterstuetzt wird
- [ ] Authentifizierungsverhalten bei abgelaufener Session waehrend Offline-Betrieb definieren
- [x] Health-/Connectivity-Endpunkte fuer den STAGING-Prototyp angelegt (`/api/health` und `/api/health/oxaion`); produktive Health-Policy noch festlegen
- [ ] Frontend-/API-Versionierung und Kompatibilitaetsregeln definieren
- [ ] Technische Update-Erkennung fuer die PWA festlegen, zum Beispiel Versionsressource plus Service-Worker-Lebenszyklus
- [ ] Verhalten bei neuer App-Version und offenen `PENDING_SYNC`-Vorgaengen im Detail festlegen
- [ ] Browser Background Sync nur als optionale Optimierung pruefen; Zuverlaessigkeit darf nicht davon abhaengen
- [ ] Installations-/Rollout-Konzept fuer verwaltete Android-Geraete festlegen
