# STAGING-Prototyp: Realer Pulver-Mix mit Frontend und Backend

## Zweck

Dieser Stand bildet den bereits praktisch getesteten Oxaion-Vorgang als testbare Webanwendung ab:

1. aktuellen Maschinenbestand des Lagerorts lesen und alte Mix-Charge plus Gesamtmenge automatisch uebernehmen;
2. pruefen, ob genau der erwartete Pulverartikel eindeutig auf der Maschine liegt;
3. alte Mix-Charge auf neue Mix-Charge umbuchen;
4. zusaetzliche Pulver-/Liefercharge auf dieselbe neue Mix-Charge buchen;
5. beide Quellen in einem Oxaion-Lagerbeleg dokumentieren;
6. Personalnummer/Name in den vorhandenen Oxaion-Freitextfeldern dokumentieren;
7. nach Abschluss die erwarteten LM/LN-Bewegungen erneut aus Oxaion lesen und verifizieren;
8. den fuer diese Abschlussverifikation erneut geoeffneten Oxaion-Beleg explizit wieder mit `LB20100J *END` schliessen, bevor `SUCCESS` gesetzt wird.

Es handelt sich bewusst um einen **STAGING-Prototyp** fuer Firma `103` und Port `11118`.

## Automatische Bestandsabfrage vor dem Nachfuellen

Der am 01.09.2026 aufgezeichnete Oxaion-Datenstrom fuer **Chargen pro Lagerort** ist in `docs/OXAION_MACHINE_STOCK_LOOKUP.md` detailliert dokumentiert.

### Nachgewiesene Bestandsbedingung

Die spaeter separat aufgezeichnete Oxaion-Selektionsmaske zeigt die Bedingung des bisherigen Filters eindeutig:

```text
Feld:     LLAWEP.LALABE
Operator: <>
Wert:     0
```

Im JET-Datenstrom wird dafuer `LB30230 *SAVLST` auf `LLAWEP.LALABE` mit `OPER=<>` ausgefuehrt. Der Backend-Prototyp ist deshalb **nicht mehr von einem gespeicherten Filter `mit Bestand` abhaengig**. `LB30230 *GETFILTER` und `*LOADSET` sind nicht Bestandteil der Backend-Bestandslogik.

### Backend-Leseablauf

- `US30600J` mit Startkontext fuer den Maschinen-Lagerort
- `LB30230R *GETHDR`
- `LB30230R *FIRSTLIST` mit `mode=reset`
- falls noch kein `<STOP/>`: `LB30230R *NEXTLIST` mit derselben `SSID`, bis das Listenende bestaetigt ist
- alle Zeilen des Lagerorts auswerten
- direkt `LLAWEP.LALABE != 0` anwenden

Der ungefilterte Referenzfall fuer `EOS1` lieferte 25 Zeilen verschiedener Artikel und Chargen inklusive Nullbestaenden und endete mit `<STOP/>`. Nach Anwendung von `LLAWEP.LALABE != 0` blieb im Referenzzustand genau:

```text
Lagerort: EOS1
Artikel:  RP.00010
Charge:   RP10WEB_20260901_085443
Bestand:  164,330 KGM
```

Damit kann das Backend nicht nur die erwartete aktive Mix-Charge erkennen, sondern auch einen positiven Bestand eines **anderen Artikels** auf dem Maschinen-Lagerort.

### Fachliche Ergebnisse

`GET /api/machine-stock` liefert fuer den Nachfuellprozess mindestens folgende Zustaende:

- `UNIQUE`: genau ein positiver `KGM`-Bestand und Artikel entspricht dem erwarteten Pulver
- `EMPTY`: kein Bestand ungleich 0 auf dem vollstaendig gelesenen Maschinen-Lagerort
- `WRONG_ARTICLE`: genau ein positiver Bestand eines anderen Artikels; Pulverwechsel erforderlich
- `INVALID_STOCK`: negativer Bestand oder unerwartete Mengeneinheit
- `AMBIGUOUS`: mehrere Bestaende ungleich 0

Nur `UNIQUE` gibt den aktuellen Nachfuellablauf frei. Alte Mix-Charge und gesamter Tankbestand werden im Frontend automatisch befuellt und sind nicht manuell editierbar.

Unmittelbar vor dem Senden liest das Frontend den Bestand erneut. Das Backend fuehrt vor dem Start der schreibenden Materialbuchung nochmals eine eigene Bestandsabfrage durch und vergleicht Lagerort, Artikel, Charge und Menge mit dem Request. Bei einer Abweichung antwortet es mit `CONFLICT` / `MACHINE_STOCK_VALIDATION`; es wird kein Lagerbelegkopf angelegt und keine Materialbuchung gestartet.

Der serverseitige Einstieg in die Lesetransaktion mit leerer Eltern-`SSID` wurde inzwischen in STAGING live bestaetigt.

## Bestaetigte Oxaion-Sequenz fuer die Mix-Buchung

### Lagerbelegkopf

- `LB20100J *LOADNEW`
- `LB20100J *NEW`
- `LB20100J *PUTNEW`
- `LB20100J *OPEN`
- `LB20090J *SHORT`
- `LB20110R *GETHDR`
- `LB20110R *FIRSTLIST`

### Position 1: alte Mix-Charge -> neue Mix-Charge

- `LB20115J *NEW`
- JET-Validierungsfolge `*PUTNEW` mit leerem Bewegungskennzeichen, `LN`, `LM`
- `LB20115J *LOADWIN3`
- finales `LB20115J *PUTNEW`
- `LB20110R *UPD`

Dabei sind die Zwischenmeldungen `BWK2601` und `KDI1901` Teil der reproduzierten JET-Validierungsfolge und werden nicht als finaler Buchungsfehler behandelt.

### Position 2 und weitere Nachfuellpositionen

Nach einer bestaetigten Position wird der persistierte LN-Zielsatz ueber `LB20110R *FIRSTLIST` neu gelesen und dessen `PSBGZT` als Fortsetzungszustand verwendet.

- `LB20115J *NEW`
- `LB20115J *PUTNEW` mit `LM`
- wenn `TCODE=WIN3`: `*LOADWIN3` und finales `*PUTNEW`
- wenn der erste `*PUTNEW` bereits den final validierten HTTP-Zustand liefert: **kein** zweites `*PUTNEW`
- `LB20110R *UPD`

Diese Unterscheidung verhindert `AKT1504: Datensatz bereits vorhanden`. Mehrere Nachfuellchargen und die dynamische Positions-/Recovery-Logik sind in `docs/MULTI_BATCH_REPLENISHMENT.md` beschrieben.

### Abschluss und Sperrfreigabe

- `LB20100J *END` nach den schreibenden Positionen
- Beleg fuer die Abschlussverifikation erneut mit `LB20100J *OPEN` oeffnen
- `LB20110R *FIRSTLIST`
- alle fuer den Vorgang erwarteten LM/LN-Bewegungen exakt pruefen
- **anschliessend den fuer die Verifikation erneut geoeffneten Beleg nochmals explizit mit `LB20100J *END` schliessen**
- erst nach erfolgreichem zweiten `*END` Status `SUCCESS` setzen
- danach wird die app-tunnel Session wie bisher mit `/disconnect` beendet

Am 02.09.2026 wurde im STAGING beobachtet, dass eine erfolgreiche App-Buchung den Lagerbeleg nach der Abschlussverifikation gesperrt liess. Ursache im Backend war, dass der Beleg fuer die rein lesende Abschlussverifikation erneut geoeffnet, danach aber kein zweites `LB20100J *END` mehr gesendet wurde. Der allgemeine app-tunnel `/disconnect` wird deshalb nicht als Ersatz fuer das fachlich bestaetigte `*END` verwendet.

Der zweite `*END` wird als notwendiger Cleanup nach bereits vollstaendig gebuchter und verifizierter Transaktion behandelt. Schlaegt dieser Close fehl, darf die App nicht `SUCCESS` melden; die Buchung ist dann zwar bereits verifiziert, der Sperrzustand muss aber manuell geprueft werden. Der Close wird nicht durch einen zwischenzeitlichen Browser-Abbruchtoken abgebrochen.

`<STOP/>` innerhalb einer Tabellenantwort ist ein normaler Tabellenabschluss und kein Buchungsfehler, wenn die erwarteten Zeilen vorhanden sind.

Die Bewegungsmenge aus der `FIRSTLIST`-Tabellenantwort kann als formatierter Anzeigewert inklusive Mengeneinheit geliefert werden, zum Beispiel `0,001 KGM` oder `0,005 KGM`. Fuer die Abschluss- und Recovery-Verifikation wird deshalb der fuehrende numerische Anteil kulturunabhaengig ausgewertet. Ein nichtleerer, nicht sicher parsbarer Mengenwert wird nicht als `0` interpretiert, sondern fuehrt kontrolliert in die Fehlerbehandlung.

## Fehler-/Recovery-Verhalten

Das Frontend vergibt vor Versand eine `clientOperationId` und speichert den offenen Vorgang in `IndexedDB`.

Auf Android kann `crypto.randomUUID()` bei Zugriff ueber eine unverschluesselte HTTP-Adresse fehlen. Das Frontend verwendet deshalb `crypto.randomUUID()` nur, wenn es verfuegbar ist, und faellt sonst auf eine UUID-v4-Erzeugung mit `crypto.getRandomValues()` zurueck. Eine schwache Zufalls-ID auf Basis von `Math.random()` wird nicht verwendet.

Das Backend persistiert je `clientOperationId` eine Transaktion unter `App_Data/transactions` und fuehrt dieselbe ID niemals blind ein zweites Mal als neue Oxaion-Buchung aus.

Die Maschinenbestands-Revalidierung behandelt eine bereits bekannte `clientOperationId` idempotent: existiert der Vorgang bereits serverseitig, wird zuerst dessen gespeicherter Transaktionsstatus zurueckgegeben. Ein spaeter veraenderter Maschinenbestand darf einen bereits erfolgreich oder unklar verarbeiteten Vorgang nicht in einen neuen Vorgang umdeuten.

Bei einem unklaren Transportfehler Backend -> Oxaion:

- Status `UNCERTAIN`;
- kein automatischer Blind-Retry;
- `POST /api/mix/{clientOperationId}/reconcile` oeffnet den bekannten Lagerbeleg erneut;
- kein Bestand gebucht -> Position 1 kontrolliert neu aufbauen;
- nur ein vollstaendiger Positions-Prefix vorhanden -> ab der ersten fehlenden Position fortsetzen;
- alle erwarteten Bewegungen vorhanden -> nichts erneut buchen, nur Abschluss/Verifikation und explizites Schliessen;
- anderer Zustand -> `MANUAL_REVIEW_REQUIRED`.

Beim blossen Oeffnen oder Neuladen der PWA wird kein schreibendes Reconcile automatisch ausgeloest. Bei einem vorhandenen lokalen Vorgang wird nur der gespeicherte Backend-Status gelesen. Ein Oxaion-Reconcile erfolgt erst nach bewusster Bedieneraktion und nur, wenn eine bestaetigte Oxaion-Belegnummer vorhanden ist.

### Bestaetigte fachliche Ablehnung `U180500`

Am 01.09.2026 wurde im STAGING bei `LB20100J *PUTNEW` fuer den Lagerbelegkopf eindeutig beobachtet:

```text
U180500
Periode 3/2026 fuer Anwendung "Lagerbuchhaltung" noch nicht eroeffnet.
Field: KOBGDT
```

Der betroffene Vorgang hatte `documentNo=null`, `headerDta=null` und keine Bewegungen. Dieser Fall ist `REJECTED`, nicht `UNCERTAIN`; es wurde keine Lagerbuchung durchgefuehrt.

### Bewusster neuer Versuch nach behobenem `REJECTED`

Nach Behebung einer eindeutigen fachlichen Ablehnung kann derselbe Buchungsauftrag bewusst erneut versucht werden:

- der alte Vorgang bleibt als `REJECTED` erhalten;
- neue `clientOperationId`;
- Verknuepfung ueber `retryOfClientOperationId`;
- fachliche Buchungsdaten bleiben identisch;
- Fehlersimulation wird nicht uebernommen;
- Backend akzeptiert den neuen Versuch nur bei bestaetigtem `REJECTED` und identischen Daten;
- aktueller Maschinenbestand muss vor dem neuen Versuch erneut exakt passen.

## Oxaion-Laufzeitbenutzer

Der Oxaion-Benutzer ist **nicht hart codiert**. `appsettings.json` enthaelt keinen Benutzer-Vorgabewert. Sowohl der Quellcode-Starter als auch das self-contained Windows-Paket fragen beim Start einen frei waehlbaren Oxaion-STAGING-Benutzer und danach dessen Passwort ab.

Der gewaehlte Benutzer wird nur als Laufzeitkonfiguration gesetzt:

```text
Oxaion__User
Oxaion__Password
```

Das Passwort wird verdeckt abgefragt; Benutzer und Passwort werden nach Ende des Starter-Prozesses aus dessen Umgebungsvariablen entfernt.

Wichtig: Der Health-Check `CONNECT + LB20100J *LOADNEW/*NEW` bestaetigt nur Login und diese nicht persistierenden Programmschritte. Er beweist nicht automatisch die Berechtigung fuer alle spaeteren Oxaion-Fachprogramme.

## Lokaler Start

Quellcode-Start mit .NET 8 SDK:

```powershell
.\scripts\start-staging.ps1
```

Windows-Test ohne lokale .NET-Installation: self-contained Artifact `FAM-Pulverentnahme-STAGING-win-x64` entpacken und `START_STAGING.bat` starten. Der Starter fragt zuerst den frei waehlbaren Oxaion-STAGING-Benutzer und danach dessen Passwort ab.

WebApp am PC:

```text
http://localhost:5080
```

Android im selben Netz:

```text
http://<IP-DES-WEBSERVERS>:5080
```

## Noch nicht Teil dieses Prototyps

- atomare Reservierung/Sperrung zwischen Bestands-Revalidierung und erster Materialbuchung;
- FA-QR-Scan und FA-Materialrueckmeldung;
- finale Authentifizierung der WebApp-Benutzer;
- produktive Transaktionsdatenbank statt JSON-Dateistore;
- finaler Pulverwechsel-/Ruecklagerungsprozess;
- finaler Scanner-/PWA-Endausbau.
## Persistierte STAGING-SQL-Verbindungen

Die beiden SQL-Verbindungen fuer Syncos-Personalwege und die rein lesenden Oxaion-SQL-Abfragen muessen nicht mehr bei jedem Start neu eingegeben werden. `start-staging.ps1` und `start-staging-published.ps1` verwenden Umgebungsvariablen mit Vorrang; ansonsten lesen sie die benutzer-/rechnergebunden per Windows-DPAPI verschluesselte Datei `%LOCALAPPDATA%\FAM-Pulverentnahme\staging-sql-secrets.clixml`. Fehlt ein Wert, wird er einmalig verdeckt abgefragt und dort verschluesselt gespeichert. `-ResetStoredSqlConnections` loescht die lokale Speicherung fuer eine bewusste Neuerfassung. Oxaion-HTTP-Benutzer und -Passwort werden weiterhin nicht dauerhaft gespeichert.
