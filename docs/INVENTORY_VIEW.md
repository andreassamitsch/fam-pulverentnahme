# RP.* Lagerbestandsansicht

Stand: 01.10.2026

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

## Verbindliche technische Entscheidung ab 08.09.2026

Fuer diese **rein lesende Lagerbestandsansicht** wird ein eigener serverseitiger SQL-Zugang zur Oxaion-Datenbank verwendet. Die Laufzeitkonfiguration lautet `OxaionSql__ConnectionString`.

`OxaionSql__ConnectionString` ist bewusst von `Syncos__ConnectionString` getrennt:

- `Syncos__ConnectionString` bleibt fuer Syncos-Personalwege wie RFID und Passwort-Fallback bestimmt.
- `OxaionSql__ConnectionString` muss auf die richtige Oxaion-Datenbank zeigen und wird ausschliesslich fuer die hier dokumentierte lesende RP.*-Bestandsansicht verwendet.
- Im STAGING-Startskript werden beide Connection Strings getrennt behandelt. Falls keine Umgebungsvariable gesetzt ist, wird zuerst der per Windows-DPAPI lokal gespeicherte Wert verwendet; nur beim ersten Start beziehungsweise nach bewusstem Reset wird verdeckt abgefragt.

Wichtig:

- SQL wird hier **nur lesend** verwendet.
- Es gibt keine ERP-Buchung, Bestandskorrektur oder sonstige Datenmanipulation per SQL.
- Alle produktiven Materialbuchungen laufen weiterhin ausschliesslich ueber die bestaetigte Oxaion-Fachlogik/HTTP-Schnittstelle.
- Firma wird als SQL-Parameter `@firm` aus `Oxaion__Firm` uebergeben und nicht fuer andere Firmen frei aus dem Browser gesetzt.
- Connection String, Benutzer und Passwort verbleiben ausschliesslich serverseitig. Die STAGING-SQL-Connection-Strings duerfen verschluesselt per Windows-DPAPI unter `%LOCALAPPDATA%\FAM-Pulverentnahme\staging-sql-secrets.clixml` persistiert werden; Klartext wird nicht in Repository, Frontend oder Logs geschrieben.
- Der Connection String wird weder im Frontend noch in Health-Antworten oder Logs ausgegeben.

## Aktuelle SQL-Sicht

### Lagerplatzbestaende

- `OXAION.LLPWEP` ist die fuehrende Zeilenquelle fuer vorhandene Lagerplatzbestaende und liefert Firma, Lagerort, internen Lagerplatz, Artikel, Charge und `LPLABE`.
- Ein zusaetzlicher INNER JOIN auf `LLPLAP` oder `ULGSTP` darf eine reale, nicht-null Bestandszeile nicht aus der Informationsansicht herausfiltern.
- `OXAION.UTLSTP` liefert die Mengeneinheit.
- `OXAION.UPOSTP` ergaenzt Chargen-/Artikelbezeichnung beziehungsweise Chargendatum.
- nur `RP.%`
- Charge darf nicht leer sein
- Bestand `<> 0`

Die Korrektur vom 01.10.2026 reagiert auf den Android-/Oxaion-Befund, dass `RP.00010` in Oxaion auf Lagerplaetzen sichtbar war, in der bisherigen WebApp-Sicht jedoch fehlte. Die bisherige Abfrage konnte reale `LLPWEP`-Bestandszeilen ueber zusaetzliche Stammdaten-INNER-JOINs verlieren. Die Lagerplatzsicht wird deshalb direkt aus den bestaetigten `LLPWEP`-Bestandszeilen aufgebaut. Der reale `RP.00010`-Fall ist nach Bereitstellung des neuen STAGING-Builds noch live zu bestaetigen.

### Lagerortbestaende ohne eigene Lagerplatzzeile

- `OXAION.LLAWEP` liefert Lagerort, Artikel, Charge und Bestand `LALABE`.
- Lagerplatz bleibt leer.
- `OXAION.UTLSTP` und `OXAION.UPOSTP` ergaenzen Einheit/Bezeichnung/Chargendatum.
- nur `RP.%`
- Charge darf nicht leer sein
- Bestand `<> 0`
- `LAGRKZ <> 'J'`, damit lagerplatzgefuehrte Bestaende nicht noch einmal als Lagerortsumme erscheinen.

Damit bleibt die bestehende Doppelbestands-Sperre erhalten, ohne einen vorhandenen Lagerplatzbestand ueber einen separaten Stammdatenjoin zu verlieren.

## Darstellung in der PWA ab 01.10.2026

Die Informationsseite ist zweigeteilt:

1. **Maschinentanks** ganz oben: alle in `MachineTanks:Warehouses` konfigurierten Tanks mit aktuellem Oxaion-Zustand. Bei eindeutigem Bestand werden Artikel, Bezeichnung, Mix-Charge, Menge und EFA01/EFA02-Erkennungsfarben angezeigt. Leere Tanks werden explizit als `Tank leer` dargestellt. Uneindeutige oder nicht lesbare Tankzustaende werden als Klaerungsfall sichtbar gemacht.
2. **Pulverlager** darunter: die RP.*-Bestandspositionen nach Artikel, Lagerort, Lagerplatz und Charge.

Die bereits bestaetigte Erkennungsfarbenlogik aus `docs/OXAION_ARTICLE_RECOGNITION_COLORS.md` wird artikelweise wiederverwendet. Farben sind nur visuelle Erkennungshilfe und keine Buchungsfreigabe.

## Nicht mehr verwendeter JET-Indexweg

Die vorherige Implementierung ueber `LB30210R - Chargen je Firma` und anschliessenden Abstieg ueber `LB30340R`/`LB30430R` bleibt als technischer Analyseweg dokumentiert, wird fuer die allgemeine Bestandsansicht aber nicht mehr verwendet.

Gruende:

- die SQL-Abfrage liefert die benoetigte Lagerort-/Lagerplatzstruktur direkt in einem rein lesenden Aufruf;
- `RP.*` ist in den Oxaion-Unterprogrammen kein gueltiger Artikelkey und fuehrte bei falscher Verwendung zu `IDN1823`;
- `_CALC.W_LAGO` aus `Chargen je Firma` ist eine aggregierte Anzeige und war fuer eine exakte Lagerplatzansicht ungeeignet;
- der SQL-Weg vermeidet viele serielle Oxaion-HTTP-Listenaufrufe und ist fuer diese reine Informationsfunktion einfacher und belastbarer.

## Sicherheitsgrenze

Diese Entscheidung ist **keine Freigabe fuer direkte Oxaion-Buchungen per SQL**. Schreibende Statements gegen Oxaion-Tabellen bleiben fuer die PWA unzulaessig.
