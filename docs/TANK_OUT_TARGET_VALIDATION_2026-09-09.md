# Tankauslagerung - Zielauswahl und F4-Validierung 09.09.2026

## Anlass

Beim Android-STAGING-Test der Tankauslagerung wurde als Ziel `H04KDX / LL3 18` ausgewaehlt. Der WebApp-Vorgang legte den Oxaion-Materialbeleg `FA26MB00077` an und erreichte die Validierung der ersten LF-Position, brach aber vor `LB20110R *UPD` ab, weil die erste `LB13210R *FIRSTLIST`-Seite kein `<STOP/>` enthielt.

Der damalige Code verlangte bei `US16601R` und `LB13210R` faelschlich, dass bereits die erste F4-Listenseite vollstaendig ist. Das funktioniert nur fuer kleine Listen und blockiert grosse lagerplatzgefuehrte Lagerorte.

Ein weiterer Android-Test zeigte danach, dass die sichtbare AJAX-Lagerplatzliste fuer `H04KDX` bei `LL324` endete, obwohl reale PCL-Lagerplaetze bis mindestens `LL350` existieren. Das Entfernen der frueheren SQL-Begrenzung `TOP (100)` war nicht ausreichend.

## Verbindliches Verhalten

### AJAX-Suche

Die rein lesende SQL-Suche nach Ziel-Lagerort und Ziel-Lagerplatz ist explizit case-insensitive. Die Suchbedingungen vergleichen Lagerort/Lagerplatz und Suchpraefix ueber `UPPER(...)`.

Fuer den Ziel-Lagerplatz verwendet die Bedienhilfe aktuell die bekannte PCL-Lagerplatzdatei `OXAION.LPCLAP`:

- Firma: `PCFIRM`
- Lagerort: `PCLAGO`
- interner Lagerplatz: `PCLAPL`

Die Rueckgabewerte werden nicht veraendert: als kanonischer Buchungsschluessel wird exakt der aus der Oxaion-Datenbank gelesene interne Lagerplatz uebernommen. Es gibt keine `TOP`-Begrenzung, keinen Artikel-/`RP.*`-Filter und keinen Bestandsfilter. SQL bleibt nur Bedienhilfe und fuehrt keine ERP-Buchung aus.

### Follow-up: `LPCLAP` loest H04KDX noch nicht nachweislich

Der erneute Android-Test nach der Umstellung auf `LPCLAP` zeigt weiterhin ein sichtbares Listenende bei `LL324`. Damit ist die Annahme, `LPCLAP` sei allein bereits die vollstaendige Datenquelle fuer den konkreten `H04KDX`-Matchcode, **nicht bestaetigt**.

Es wird deshalb keine weitere Oxaion-Tabelle oder Selektionslogik geraten. Der naechste Live-Test zeigt unter dem Lagerplatzfeld zusaetzlich:

- Anzahl der vom Backend gelieferten eindeutigen Lagerplaetze;
- letzter vom Backend gelieferter Lagerplatzschluessel.

Zusaetzlich kann gezielt nach `LL35` beziehungsweise `LL350` gesucht werden. Damit wird unterschieden zwischen:

1. Backend-/SQL-Antwort endet bereits bei `LL324`;
2. Backend liefert spaetere Schluessel, aber die Android-Darstellung/Scroll-Liste zeigt sie nicht korrekt.

Wenn Fall 1 bestaetigt wird, muss der reale `LB13210R`-Datenstrom beziehungsweise dessen STAGING-Stammdatenquelle mit `LPCLAP` verglichen werden, bevor eine weitere produktive Quelle eingebaut wird.

### Oxaion-F4-Pruefung unmittelbar vor der Buchung

Die bereits bestaetigten F4-Wege bleiben unveraendert:

- Ziel-Lagerort: `LB20115J *F4` -> `US16601R`
- Ziel-Lagerplatz: `LB20115J *F4` -> `LB13210R`

Die Listenpruefung ist seitenfaehig:

1. `*FIRSTLIST` mit dem bestaetigten F4-Kontext lesen.
2. Wenn kein `<STOP/>` geliefert wird, mit dem im Projekt bereits fuer Oxaion-R-Listen verwendeten Pagingmechanismus `*NEXTLIST` + derselben `SSID` weiterlesen.
3. Erst nach einem eindeutig erreichten `<STOP/>` darf die Liste als vollstaendig gelten.
4. Der Zielschluessel muss in der gesamten vollstaendigen Liste exakt einmal vorkommen.
5. Spaetestens nach 100 Seiten wird fail-closed abgebrochen.
6. Eine leere nichtterminale Seite oder eine exakt wiederholte nichtterminale Seite wird als nicht fortschreitende Pagination abgebrochen.

Damit wird die Sicherheitsregel nicht aufgeweicht: ein Treffer auf einer unvollstaendigen ersten Seite reicht weiterhin **nicht** als Buchungsfreigabe. Auch ein Treffer der rein lesenden SQL-Bedienhilfe ist keine Buchungsfreigabe.

## Status des Oxaion-Pagings

`*NEXTLIST` + `SSID` ist im Projekt bereits fuer paginierte Oxaion-R-Listen bestaetigt und produktiv im lesenden Maschinenbestandsweg verwendet. Fuer den konkreten `LB13210R`-F4-Fall mit vielen Lagerplaetzen ist die Anwendung dieses gleichen Listenprotokolls noch einmal live in STAGING zu bestaetigen. Bis zum `<STOP/>` bleibt die Validierung fail-closed.

## Fehler-/Retry-Regel fuer den beobachteten Vorgang

Beim fehlgeschlagenen Vorgang wurde bereits der Belegkopf `FA26MB00077` erzeugt, aber laut WebApp-Protokoll kein `POSITION_1_UPD_SENT` erreicht und keine Bewegung bestaetigt. Der bestehende Vorgang bleibt trotzdem `MANUAL_REVIEW_REQUIRED` und wird nicht mit derselben `clientOperationId` blind erneut ausgefuehrt.

Ein neuer Test wird als neuer Bedienvorgang mit neuer `clientOperationId` gestartet.

## Live-STAGING-Pruefung

Noch zu bestaetigen:

- `H04KDX` oeffnen und Anzahl sowie letzten geladenen Lagerplatz aus der neuen Statuszeile festhalten.
- `LL35` beziehungsweise `LL350` gezielt suchen und pruefen, ob der Schluessel aus der aktuellen Backendquelle geliefert wird.
- Einen spaeten, serverseitig gelieferten Lagerplatz auswaehlen und die vollstaendige `LB13210R`-F4-Pruefung bis `<STOP/>` bestaetigen.
- Erst danach die reale `LF/LE`-Tankauslagerung wie bisher fortsetzen und vollstaendig verifizieren.
