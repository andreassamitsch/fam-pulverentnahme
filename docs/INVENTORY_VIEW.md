# RP.* Lagerbestandsansicht

Stand: 08.09.2026

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

Fuer diese **rein lesende Lagerbestandsansicht** wird der bereits vorhandene serverseitige SQL-Zugang wiederverwendet. Die Konfiguration bleibt aus Kompatibilitaetsgruenden `Syncos__ConnectionString`; derselbe SQL-Login kann neben der Syncos-Personalzuordnung auch die bestaetigten Oxaion-Lesetabellen erreichen.

Wichtig:

- SQL wird hier **nur lesend** verwendet.
- Es gibt keine ERP-Buchung, Bestandskorrektur oder sonstige Datenmanipulation per SQL.
- Alle produktiven Materialbuchungen laufen weiterhin ausschliesslich ueber die bestaetigte Oxaion-Fachlogik/HTTP-Schnittstelle.
- Firma wird als SQL-Parameter `@firm` aus `Oxaion__Firm` uebergeben und nicht fuer andere Firmen frei aus dem Browser gesetzt.
- Connection String, Benutzer und Passwort verbleiben ausschliesslich im Backend/Runtime-Environment.

## Bestaetigte SQL-Sicht

Die Lagerplatzorganisation wird aus `OXAION.ULGSTP.LGKLPL` unterschieden.

### Lagerorte mit Lagerplatzorganisation (`LGKLPL='J'`)

- `OXAION.LLPLAP` liefert Lagerort, internen Lagerplatz, Artikel und Charge.
- `OXAION.LLPWEP` liefert den Lagerplatzbestand `LPLABE`.
- `OXAION.UTLSTP` liefert die Mengeneinheit.
- `OXAION.UPOSTP` liefert Chargen-/Artikelbezeichnung beziehungsweise Chargendatum.
- nur `RP.%`
- Charge darf nicht leer sein
- Bestand `<> 0`

### Lagerorte ohne Lagerplatzorganisation (`LGKLPL='N'`)

- `OXAION.LLAWEP` liefert Lagerort, Artikel, Charge und Bestand `LALABE`.
- Lagerplatz bleibt leer.
- `OXAION.UTLSTP` und `OXAION.UPOSTP` ergaenzen Einheit/Bezeichnung/Chargendatum.
- nur `RP.%`
- Charge darf nicht leer sein
- Bestand `<> 0`
- `LAGRKZ <> 'J'`, damit lagerplatzgefuehrte Bestaende nicht doppelt als Lagerortsumme erscheinen.

Damit wird insbesondere der frueher beobachtete Doppelbestand vermieden, bei dem fuer einen lagerplatzgefuehrten Lagerort sowohl die Lagerplatzzeilen als auch eine Lagerortsumme angezeigt wurden.

## Nicht mehr verwendeter JET-Indexweg

Die vorherige Implementierung ueber `LB30210R - Chargen je Firma` und anschliessenden Abstieg ueber `LB30340R`/`LB30430R` bleibt als technischer Analyseweg dokumentiert, wird fuer die allgemeine Bestandsansicht aber nicht mehr verwendet.

Gruende:

- die SQL-Abfrage liefert die benoetigte Lagerort-/Lagerplatzstruktur direkt in einem rein lesenden Aufruf;
- `RP.*` ist in den Oxaion-Unterprogrammen kein gueltiger Artikelkey und fuehrte bei falscher Verwendung zu `IDN1823`;
- `_CALC.W_LAGO` aus `Chargen je Firma` ist eine aggregierte Anzeige und war fuer eine exakte Lagerplatzansicht ungeeignet;
- der SQL-Weg vermeidet viele serielle Oxaion-HTTP-Listenaufrufe und ist fuer diese reine Informationsfunktion einfacher und belastbarer.

## Sicherheitsgrenze

Diese Entscheidung ist **keine Freigabe fuer direkte Oxaion-Buchungen per SQL**. Schreibende Statements gegen Oxaion-Tabellen bleiben fuer die PWA unzulaessig.
