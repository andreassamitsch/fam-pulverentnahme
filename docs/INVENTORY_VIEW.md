# RP.* Lagerbestandsansicht

Stand: 05.10.2026

## Fachliches Ziel

Die PWA zeigt alle aktuellen Chargenbestaende der Pulverartikel `RP.*` fuer Firma `103` beziehungsweise die konfigurierte Oxaion-Firma an.

Angezeigt werden mindestens:

- Artikel
- Artikelbezeichnung
- Lagerort
- interner Lagerplatzschluessel, wenn der Lagerort lagerplatzgefuehrt ist
- Charge
- Bestand
- Einheit

Nullbestaende werden nicht angezeigt. Negative Bestaende bleiben sichtbar und werden in der PWA als Klaerungsfall markiert.

## Verbindliche technische Entscheidung

Fuer diese **rein lesende Lagerbestandsansicht** verwendet das Backend den Oxaion-Datenbankkatalog der aktuell gewaehlten Serverumgebung.

Ab 02.10.2026 gilt fuer den Dienstbetrieb:

- SQL Server, nativer SQL-Benutzer und SQL-Passwort werden nur einmal zentral konfiguriert und fuer Syncos sowie die freigegebenen Oxaion-SQL-Lesewege wiederverwendet.
- STAGING und PRODUCTION besitzen getrennte Datenbankziele. Die exakten Oxaion-SQL-Katalognamen werden in der lokalen Konfigurationsoberflaeche gepflegt.
- Der bisherige Fehlerfall, bei dem die App wegen eines auf PRODUCTION zeigenden SQL-Connection-Strings andere Lagerdaten als eine manuelle STAGING-Abfrage zeigte, wird dadurch vermieden: Umgebung und Datenbankauswahl sind eine gemeinsame serverseitige Konfiguration.
- SQL-Passwort und Oxaion-Zugangsdaten werden DPAPI-verschluesselt unter `%ProgramData%\FAM-Pulverentnahme\service-config.json` gespeichert.

Wichtig:

- SQL wird hier **nur lesend** verwendet.
- Es gibt keine ERP-Buchung, Bestandskorrektur oder sonstige Datenmanipulation per SQL.
- Alle produktiven Materialbuchungen laufen weiterhin ausschliesslich ueber die bestaetigte Oxaion-Fachlogik/HTTP-Schnittstelle.
- Firma wird serverseitig als SQL-Parameter `@firm` aus der aktiven Oxaion-Konfiguration uebergeben und nicht frei aus dem Browser gesetzt.
- Connection String, Benutzer und Passwort verbleiben ausschliesslich serverseitig und werden weder im Frontend noch in Health-Antworten oder Logs ausgegeben.

## Aktuelle SQL-Sicht

### Lagerplatzbestaende

- `OXAION.LLPWEP` ist die fuehrende Zeilenquelle fuer vorhandene Lagerplatzbestaende und liefert Firma, Lagerort, internen Lagerplatz, Artikel, Charge und `LPLABE`.
- Ein zusaetzlicher INNER JOIN auf `LLPLAP` oder `ULGSTP` darf eine reale, nicht-null Bestandszeile nicht aus der Informationsansicht herausfiltern.
- `OXAION.UTLSTP` liefert die Mengeneinheit.
- `OXAION.UPOSTP` ergaenzt Chargen-/Artikelbezeichnung beziehungsweise Chargendatum.
- nur `RP.%`
- Charge darf nicht leer sein
- Bestand `<> 0`

Die Korrektur vom 01.10.2026 reagiert auf den Android-/Oxaion-Befund, dass `RP.00010` in Oxaion auf Lagerplaetzen sichtbar war, in der bisherigen WebApp-Sicht jedoch fehlte. Die bisherige Abfrage konnte reale `LLPWEP`-Bestandszeilen ueber zusaetzliche Stammdaten-INNER-JOINs verlieren. Die Lagerplatzsicht wird deshalb direkt aus den bestaetigten `LLPWEP`-Bestandszeilen aufgebaut. Am 02.10.2026 wurde die aktuelle SQL-Abfrage direkt gegen Oxaion geprueft; `RP.00010` ist im Ergebnis enthalten. Der Android-STAGING-Test der Anzeige bleibt separat offen.

### Lagerortbestaende ohne eigene Lagerplatzzeile

- `OXAION.LLAWEP` liefert Lagerort, Artikel, Charge und Bestand `LALABE`.
- Lagerplatz bleibt leer.
- `OXAION.UTLSTP` und `OXAION.UPOSTP` ergaenzen Einheit/Bezeichnung/Chargendatum.
- nur `RP.%`
- Charge darf nicht leer sein
- Bestand `<> 0`
- `LAGRKZ <> 'J'` bleibt als vorhandenes Kennzeichen bestehen.
- Zusaetzlich wird eine `LLAWEP`-Zeile unterdrueckt, sobald fuer **denselben Artikel, Lagerort und dieselbe Charge** ein realer `LLPWEP`-Lagerplatzbestand ungleich 0 existiert. Damit kann ein unzuverlaessiges beziehungsweise nicht passend gepflegtes `LAGRKZ` keine Lagerort-Summe neben der konkreten Lagerplatzposition duplizieren.
- Der aeussere SELECT ist `DISTINCT`, damit identische Informationszeilen nicht mehrfach angezeigt werden.
- Wegen `SELECT DISTINCT` verwendet die Sortierung dieselben projizierten Ausdruecke fuer Artikel, Artikelbezeichnung und Charge: `CAST(X.Artikel as nvarchar(12))`, `CAST(X.Artikelbezeichnung as nvarchar(20))` und `TRIM(X.Charge)`. Damit ist die Abfrage SQL-Server-kompatibel und bleibt nach Lagerort/Lagerplatz/Artikel/Bezeichnung/Charge deterministisch sortiert.

Damit zeigt die Informationsansicht fuer lagerplatzgefuehrte Bestaende die konkreten Lagerplatzpositionen und fuer echte nicht lagerplatzgefuehrte Bestaende weiterhin die Lagerortposition.

## Darstellung in der PWA ab 01.10.2026

Die Informationsseite ist zweigeteilt:

1. **Maschinentanks** ganz oben: alle dynamisch aus der aktiven Oxaion-SQL-Datenbank gelesenen Tanklagerorte der Firma mit `ULGSTP.LGLGART = '02'`. Bei eindeutigem Bestand werden Artikel, Bezeichnung, Mix-Charge, Menge und EFA01/EFA02-Erkennungsfarben angezeigt. Leere Tanks werden explizit als `Tank leer` dargestellt. Uneindeutige oder nicht lesbare Tankzustaende werden als Klaerungsfall sichtbar gemacht. Eine statische `MachineTanks:Warehouses`-Liste wird nicht mehr verwendet.
2. **Pulverlager** darunter: pro Artikel eine flache Liste der tatsaechlichen Bestandspositionen. Jede Zeile zeigt `Lagerort / Lagerplatz` (beziehungsweise nur den Lagerort, wenn kein Lagerplatz existiert), darunter die Charge und rechts die Menge. Eine zusaetzliche Lagerort-Kopfzeile mit nochmals separaten Chargen-/Mengenzeilen wird bewusst nicht dargestellt.

### Lagerplatz-Umlagerung aus der Pulverlagerliste

Ab dem neuen Umlagerungs-Release kann eine positive RP.*-Bestandsposition mit **konkretem Lagerplatz** direkt in der Pulverlagerliste mit `Umlagern` ausgewaehlt werden. Die reine SQL-Lageransicht bleibt dabei unveraendert read-only; der Klick startet einen separaten, schreibenden Backend-Vorgang.

Verbindlicher Ablauf:

- Quelle, Artikel und Charge stammen fest aus der angeklickten Bestandsposition und sind nicht frei editierbar.
- Die aktuell angezeigte volle Positionsmenge wird als Umlagerungsmenge vorgeschlagen. Der Bediener darf sie vor der Bestaetigung reduzieren, aber nicht erhoehen.
- Als Ziel sind nur lagerplatzgefuehrte, aktive Oxaion-Lagerorte/Lagerplaetze zulaessig. Standardmaessig wird der aktuelle Lagerort vorgeschlagen, damit eine typische Lagerplatz-zu-Lagerplatz-Umlagerung mit wenig Eingaben moeglich ist.
- Die exakt gleiche Quell-/Zielposition ist unzulaessig.
- **Maschinentank-Lagerorte sind als Ziel ausgeschlossen.** Die UI filtert sie aus; das Backend prueft die dynamische Tankdefinition unmittelbar vor der Buchung nochmals und lehnt manipulierte Requests ab.
- Vor der ersten schreibenden Oxaion-Aktion liest das Backend die exakte Quellposition ueber den bestaetigten Oxaion-HTTP-Bestandsweg erneut. Artikel, Lagerort, Lagerplatz, Charge und die seit der Anzeige erwartete Gesamtmenge muessen noch uebereinstimmen; ausserdem muss die gewuenschte Teilmenge weiterhin verfuegbar sein.
- Ziel-Lagerort und Ziel-Lagerplatz werden vor dem Schreiben erneut ueber die bestaetigten Oxaion-F4-Listen validiert.
- Die Buchung verwendet genau eine `LF -> LE`-Position. Die Charge bleibt unveraendert. Nach dem Schreiben muss das erwartete LF-/LE-Bewegungspaar mit Quelle, Ziel, Charge und Menge exakt im Oxaion-Beleg vorhanden sein.

Der vorhandene `LF -> LE`-Baustein ist technisch bereits fuer die FAM-Materialtransfers bestaetigt. Der konkrete neue Anwendungsfall **Lagerplatz -> anderer Lagerplatz** bleibt vor Freigabe fuer `main` noch mit einer realen STAGING-Umlagerung zu bestaetigen.

Die bereits bestaetigte Erkennungsfarbenlogik aus `docs/OXAION_ARTICLE_RECOGNITION_COLORS.md` wird artikelweise wiederverwendet. Der Sachmerkmalsabruf erfolgt in einem frischen, vom Tank-/Lagerlisten-Kontext getrennten Oxaion-App-Tunnel. Fuer denselben Artikel wird das aufgeloeste Farbergebnis sowohl bei Maschinentanks als auch im Pulverlager verwendet. Kann kein gueltiger HEX-Wert gelesen werden, zeigt die PWA kein leeres Farbfeld. Farben sind nur visuelle Erkennungshilfe und keine Buchungsfreigabe.

### Korrektur 05.10.2026: Farbfeld in Maschinentank-Karten

Beim realen Android-/PRODUCTION-Test wurde fuer `RP.00024` beobachtet, dass EFA01/EFA02 im Pulverlager korrekt als Rot/Braun dargestellt wurden, die Karten `M400-02` und `M650` jedoch nur einen leeren Farbrahmen zeigten. Damit war die Oxaion-Sachmerkmalsauflösung nachweislich vorhanden; der Fehler lag in der Tank-Darstellung des Frontends.

Die Tank- und Pulverlagerdarstellung verwenden deshalb denselben serverseitig artikelweise aufgeloesten `recognitionColors`-Datensatz. Das Frontend rendert die zwei Farben robust als ein einziges `linear-gradient`-Hintergrundfeld statt als zwei innere Teil-Elemente. Ein Regressionstest prueft zusaetzlich, dass ein Artikel wie `RP.00024` im Tank denselben Farbdaten-Datensatz erhaelt wie im Pulverlager.

Da die PWA alte Frontend-Dateien cachen kann, wurde fuer diesen Stand die Service-Worker-Cachekennung auf `fam-pulver-v43-inventory-speed-colors-20261005` und die `process-mode.js`-Assetversion auf `20261005-inventory-speed-colors-1` angehoben. Der installierbare Korrekturstand ist Version `0.1.3`.

**Praxisfreigabe 05.10.2026:** Version `0.1.3` wurde auf APP-01 mit der Android-PWA getestet. Die Farbfelder der Maschinentanks werden nun korrekt dargestellt. Der zuvor sichtbare Fehler mit leerem Farbrahmen bei einem Artikel mit vorhandenen EFA01/EFA02-Farben ist damit fuer den getesteten Fall behoben und praxisbestaetigt.


## Nicht mehr verwendeter JET-Indexweg

Die vorherige Implementierung ueber `LB30210R - Chargen je Firma` und anschliessenden Abstieg ueber `LB30340R`/`LB30430R` bleibt als technischer Analyseweg dokumentiert, wird fuer die allgemeine Bestandsansicht aber nicht mehr verwendet.

Gruende:

- die SQL-Abfrage liefert die benoetigte Lagerort-/Lagerplatzstruktur direkt in einem rein lesenden Aufruf;
- `RP.*` ist in den Oxaion-Unterprogrammen kein gueltiger Artikelkey und fuehrte bei falscher Verwendung zu `IDN1823`;
- `_CALC.W_LAGO` aus `Chargen je Firma` ist eine aggregierte Anzeige und war fuer eine exakte Lagerplatzansicht ungeeignet;
- der SQL-Weg vermeidet viele serielle Oxaion-HTTP-Listenaufrufe und ist fuer diese reine Informationsfunktion einfacher und belastbarer.

## Sicherheitsgrenze

Diese Entscheidung ist **keine Freigabe fuer direkte Oxaion-Buchungen per SQL**. Schreibende Statements gegen Oxaion-Tabellen bleiben fuer die PWA unzulaessig.

Auch die aus der Lageruebersicht gestartete Umlagerung ist technisch ein separater Oxaion-HTTP-/Materialbelegvorgang; SQL liefert nur die read-only Ausgangsanzeige.
