# Tankauslagerung - Zielauswahl und F4-Validierung 09.09.2026

## Anlass

Beim Android-STAGING-Test der Tankauslagerung wurde als Ziel `H04KDX / LL3 18` ausgewaehlt. Der WebApp-Vorgang legte den Oxaion-Materialbeleg `FA26MB00077` an und erreichte die Validierung der ersten LF-Position, brach aber vor `LB20110R *UPD` ab, weil die erste `LB13210R *FIRSTLIST`-Seite kein `<STOP/>` enthielt.

Der bisherige Code verlangte bei `US16601R` und `LB13210R` faelschlich, dass bereits die erste F4-Listenseite vollstaendig ist. Das funktioniert nur fuer kleine Listen und blockiert grosse lagerplatzgefuehrte Lagerorte.

## Verbindliches Verhalten

### AJAX-Suche

Die rein lesende SQL-Suche nach Ziel-Lagerort und Ziel-Lagerplatz ist explizit case-insensitive. Die Suchbedingungen vergleichen Lagerort/Lagerplatz und Suchpraefix ueber `UPPER(...)`.

Die Rueckgabewerte werden nicht veraendert: als kanonischer Buchungsschluessel wird weiterhin exakt der aus Oxaion SQL gelesene Lagerort beziehungsweise interne Lagerplatz uebernommen. SQL bleibt nur Bedienhilfe und fuehrt keine ERP-Buchung aus.

### Oxaion-F4-Pruefung unmittelbar vor der Buchung

Die bereits bestaetigten F4-Wege bleiben unveraendert:

- Ziel-Lagerort: `LB20115J *F4` -> `US16601R`
- Ziel-Lagerplatz: `LB20115J *F4` -> `LB13210R`

Die Listenpruefung ist jetzt seitenfaehig:

1. `*FIRSTLIST` mit dem bestaetigten F4-Kontext lesen.
2. Wenn kein `<STOP/>` geliefert wird, mit dem im Projekt bereits fuer Oxaion-R-Listen verwendeten Pagingmechanismus `*NEXTLIST` + derselben `SSID` weiterlesen.
3. Erst nach einem eindeutig erreichten `<STOP/>` darf die Liste als vollstaendig gelten.
4. Der Zielschluessel muss in der gesamten vollstaendigen Liste exakt einmal vorkommen.
5. Spaetestens nach 100 Seiten wird fail-closed abgebrochen.
6. Eine leere nichtterminale Seite oder eine exakt wiederholte nichtterminale Seite wird als nicht fortschreitende Pagination abgebrochen.

Damit wird die Sicherheitsregel nicht aufgeweicht: ein Treffer auf einer unvollstaendigen ersten Seite reicht weiterhin **nicht** als Buchungsfreigabe.

## Status des Oxaion-Pagings

`*NEXTLIST` + `SSID` ist im Projekt bereits fuer paginierte Oxaion-R-Listen bestaetigt und produktiv im lesenden Maschinenbestandsweg verwendet. Fuer den konkreten `LB13210R`-F4-Fall mit vielen Lagerplaetzen ist die neue Anwendung dieses gleichen Listenprotokolls noch einmal live in STAGING zu bestaetigen. Bis zum `<STOP/>` bleibt die Validierung fail-closed.

## Fehler-/Retry-Regel fuer den beobachteten Vorgang

Beim fehlgeschlagenen Vorgang wurde bereits der Belegkopf `FA26MB00077` erzeugt, aber laut WebApp-Protokoll kein `POSITION_1_UPD_SENT` erreicht und keine Bewegung bestaetigt. Der bestehende Vorgang bleibt trotzdem `MANUAL_REVIEW_REQUIRED` und wird nicht mit derselben `clientOperationId` blind erneut ausgefuehrt.

Ein neuer Test nach Klaerung des leeren Belegkopfs wird als neuer Bedienvorgang mit neuer `clientOperationId` gestartet.
