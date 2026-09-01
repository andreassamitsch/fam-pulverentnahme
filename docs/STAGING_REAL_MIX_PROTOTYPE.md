# STAGING-Prototyp: Realer Pulver-Mix mit Frontend und Backend

## Zweck

Dieser Stand bildet den bereits praktisch getesteten Oxaion-Vorgang als testbare Webanwendung ab:

1. aktuellen positiven Maschinenbestand fuer den erwarteten Artikel lesen und alte Mix-Charge plus Gesamtmenge automatisch uebernehmen;
2. alte Mix-Charge auf neue Mix-Charge umbuchen;
3. zusaetzliche Pulver-/Liefercharge auf dieselbe neue Mix-Charge buchen;
4. beide Quellen in einem Oxaion-Lagerbeleg dokumentieren;
5. Personalnummer/Name in den vorhandenen Oxaion-Freitextfeldern dokumentieren;
6. nach Abschluss die vier erwarteten LM/LN-Bewegungen erneut aus Oxaion lesen und verifizieren.

Es handelt sich bewusst um einen **STAGING-Prototyp** fuer Firma `103` und Port `11118`.

## Automatische Bestandsabfrage vor dem Nachfuellen

Der am 01.09.2026 aufgezeichnete Oxaion-Datenstrom fuer **Chargen pro Lagerort** ist in `docs/OXAION_MACHINE_STOCK_LOOKUP.md` dokumentiert und im Backend als rein lesende Abfrage umgesetzt.

Der beobachtete Ablauf ist:

- `US30600J` mit Startkontext fuer Artikel/Lagerort
- `LB30230R *GETHDR`
- `LB30230R *FIRSTLIST` mit `mode=reset`
- `LB30230 *GETFILTER`
- Filter `mit Bestand` anhand der von Oxaion gelieferten Filterdaten ermitteln
- `LB30230 *LOADSET`
- `LB30230R *GETU01`
- `LB30230R *FIRSTLIST` mit `mode=replace`

Der Referenzfall `Firma 103 / EOS1 / RP.00010` lieferte nach dem Filter genau eine positive Charge:

```text
Charge:  RP10WEB_20260901_085443
Bestand: 164,330 KGM
```

Der STAGING-Prototyp stellt dafuer `GET /api/machine-stock` bereit. Im Frontend werden **Aktuelle Mix-Charge** und **Gesamter Tankbestand kg** automatisch befuellt und sind nicht manuell editierbar.

Nur ein Ergebnis `UNIQUE` mit genau einer positiven Charge fuer den erwarteten Artikel gibt den aktuellen Nachfuellablauf frei. Mehrere positive Chargen werden als `AMBIGUOUS` gestoppt. Kein positiver Bestand fuer den erwarteten Artikel wird als `NO_STOCK_FOR_ARTICLE` gestoppt und **nicht** als sicher leere Maschine interpretiert, weil der bisher bestaetigte Datenstrom artikelbezogen ist.

Unmittelbar vor dem Senden liest das Frontend den Bestand erneut. Das Backend fuehrt vor dem Start der schreibenden Materialbuchung nochmals eine eigene Bestandsabfrage durch und vergleicht Lagerort, Artikel, Charge und Menge mit dem Request. Bei einer Abweichung antwortet es mit `CONFLICT` / `MACHINE_STOCK_VALIDATION`; es wird kein Lagerbelegkopf angelegt und keine Materialbuchung gestartet.

Noch live zu bestaetigen ist der serverseitige Einstieg in die Lesetransaktion: Der aufgezeichnete interaktive `US30600J`-Aufruf enthielt eine Elternbildschirm-`SSID`, waehrend das Backend bei diesem rein lesenden Start `SSID` leer sendet und eine neue `SSID` in der Antwort verlangt. Bis dieser Punkt in STAGING bestaetigt ist, ist die Programmlogik aus dem Datenstrom technisch nachgewiesen, der Backend-Start aber noch nicht produktiv freigegeben.

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

### Position 2: neue Pulvercharge -> dieselbe neue Mix-Charge

Nach Position 1 wird der persistierte LN-Zielsatz ueber `LB20110R *FIRSTLIST` neu gelesen und dessen `PSBGZT` als Fortsetzungszustand verwendet.

- `LB20115J *NEW`
- `LB20115J *PUTNEW` mit `LM`
- wenn `TCODE=WIN3`: `*LOADWIN3` und finales `*PUTNEW`
- wenn der erste `*PUTNEW` bereits den final validierten HTTP-Zustand liefert: **kein** zweites `*PUTNEW`
- `LB20110R *UPD`

Diese Unterscheidung verhindert `AKT1504: Datensatz bereits vorhanden`.

### Abschluss

- `LB20100J *END`
- Beleg erneut oeffnen
- `LB20110R *FIRSTLIST`
- exakt vier erwartete Bewegungen pruefen:
  - Pos. 1 `LM` alte Mix-Charge
  - Pos. 1 `LN` neue Mix-Charge
  - Pos. 2 `LM` neue Pulvercharge
  - Pos. 2 `LN` neue Mix-Charge

`<STOP/>` innerhalb einer Tabellenantwort ist ein normaler Tabellenabschluss und kein Buchungsfehler, wenn die erwarteten Zeilen vorhanden sind.

Die Bewegungsmenge aus der `FIRSTLIST`-Tabellenantwort kann als formatierter Anzeigewert inklusive Mengeneinheit geliefert werden, zum Beispiel `0,001 KGM` oder `0,005 KGM`. Fuer die Abschluss- und Recovery-Verifikation wird deshalb der fuehrende numerische Anteil kulturunabhaengig ausgewertet. Ein nichtleerer, nicht sicher parsbarer Mengenwert wird nicht als `0` interpretiert, sondern fuehrt weiterhin kontrolliert in die Fehlerbehandlung. Dieser Fall wurde nach dem erfolgreichen Beleg `FA26MB00026` korrigiert und ist durch Regressionstests abgedeckt.

## Fehler-/Recovery-Verhalten

Das Frontend vergibt vor Versand eine `clientOperationId` und speichert den offenen Vorgang in `IndexedDB`.

Auf Android kann `crypto.randomUUID()` bei Zugriff ueber eine unverschluesselte HTTP-Adresse wie `http://<SERVER-IP>:5080` fehlen, weil die API an einen Secure Context gebunden sein kann. Das Frontend verwendet deshalb `crypto.randomUUID()` nur, wenn es verfuegbar ist, und faellt sonst auf eine UUID-v4-Erzeugung mit `crypto.getRandomValues()` zurueck. Eine schwache Zufalls-ID auf Basis von `Math.random()` wird fuer die Idempotenz-ID nicht verwendet.

Das Backend persistiert je `clientOperationId` eine Transaktion unter `App_Data/transactions` und fuehrt dieselbe ID niemals blind ein zweites Mal als neue Oxaion-Buchung aus.

Die Maschinenbestands-Revalidierung behandelt eine bereits bekannte `clientOperationId` bewusst idempotent: existiert der Vorgang bereits serverseitig, wird zuerst dessen gespeicherter Transaktionsstatus zurueckgegeben. Ein spaeter veraenderter Maschinenbestand darf einen bereits erfolgreich oder unklar verarbeiteten Vorgang nicht in einen neuen Vorgang umdeuten.

Bei einem unklaren Transportfehler Backend -> Oxaion:

- Status `UNCERTAIN`;
- kein automatischer Blind-Retry;
- `POST /api/mix/{clientOperationId}/reconcile` oeffnet den bekannten Lagerbeleg erneut;
- kein Bestand gebucht -> Position 1 kontrolliert neu aufbauen;
- nur Position 1 vorhanden -> nur Position 2 fortsetzen;
- alle vier Bewegungen vorhanden -> nichts erneut buchen, nur Abschluss/Verifikation;
- anderer Zustand -> `MANUAL_REVIEW_REQUIRED`.

Seit dem Diagnose-Stand vom 01.09.2026 loest das Frontend beim blossen Oeffnen oder Neuladen der PWA **kein schreibendes Reconcile mehr automatisch aus**. Bei einem vorhandenen lokalen Vorgang wird beim Start nur der bereits gespeicherte Backend-Status ueber `GET /api/mix/{clientOperationId}` gelesen. Ein Oxaion-Reconcile erfolgt erst nach bewusster Bedieneraktion und nur, wenn eine bestaetigte Oxaion-Belegnummer vorhanden ist. Dadurch bleibt insbesondere die urspruengliche Backend-/Oxaion-Fehlermeldung sichtbar und wird nicht durch eine nachfolgende generische Recovery-Meldung ueberschrieben.

Die Recovery-Karte zeigt `Status`, `Stage`, `DocumentNo` und die letzte Backend-Meldung. Wenn noch keine bestaetigte Belegnummer existiert, fuehrt der Recovery-Button nur eine lesende Backend-Statusaktualisierung aus und keine weitere Oxaion-Buchung.

Der Service Worker verwendet fuer diesen Stand einen neuen App-Shell-Cache und behandelt Navigationen network-first mit Cache-Fallback. Damit soll ein Android-Geraet nach einem Serverupdate nicht dauerhaft die vorherige STAGING-Oberflaeche aus dem Cache ausfuehren. Der IndexedDB-Vorgangsspeicher wird durch diesen Cachewechsel nicht geloescht.

### Bestaetigte fachliche Ablehnung `U180500`

Am 01.09.2026 wurde im STAGING bei `LB20100J *PUTNEW` fuer den Lagerbelegkopf folgende eindeutige Oxaion-Ablehnung beobachtet:

```text
U180500
Periode 3/2026 fuer Anwendung "Lagerbuchhaltung" noch nicht eroeffnet.
Field: KOBGDT
```

Der betroffene Vorgang hatte danach `documentNo=null`, `headerDta=null` und keine Bewegungen. Dieser Fall ist deshalb `REJECTED`, nicht `UNCERTAIN`: Es wurde keine Lagerbuchung durchgefuehrt und es ist kein Belegkopf bestaetigt worden.

Verbindliches Verhalten des STAGING-Prototyps:

- `REJECTED` ist fuer dieselbe `clientOperationId` ein terminaler Zustand;
- ein Reconcile darf `REJECTED` nicht in `MANUAL_REVIEW_REQUIRED` umwandeln;
- bei `U180500` wird dem Bediener angezeigt, dass die benoetigte Lagerbuchhaltungsperiode nicht geoeffnet ist;
- Massnahme: Periode in Oxaion oeffnen lassen beziehungsweise das zulaessige Buchungsdatum klaeren;
- es gibt keinen automatischen Oxaion-Retry fuer den abgelehnten Vorgang.

### Bewusster neuer Versuch nach behobenem `REJECTED`

Ist die Ursache einer eindeutigen fachlichen Ablehnung behoben, kann der Bediener **denselben Buchungsauftrag mit denselben Buchungsdaten bewusst erneut versuchen**. Dieser neue Versuch ist technisch kein Retry derselben Transaktion, sondern ein neuer Vorgang:

- der alte Vorgang bleibt unveraendert als `REJECTED` erhalten;
- das Frontend erzeugt eine **neue** `clientOperationId`;
- der neue Request traegt `retryOfClientOperationId` mit der `clientOperationId` des vorherigen abgelehnten Vorgangs;
- Artikel, Chargen, Lagerorte, Lagerplaetze, Mengen, Personalnummer, Buchungs-/Produktionsdatum, Buchungstext und Ziel-Mix-Charge werden unveraendert uebernommen;
- eine eventuell gesetzte STAGING-Fehlersimulation wird fuer den neuen Versuch nicht uebernommen;
- das Backend akzeptiert einen solchen verknuepften neuen Versuch nur, wenn der referenzierte Vorgang eindeutig `REJECTED` ist und die fachlichen Buchungsdaten exakt mit dem abgelehnten Vorgang uebereinstimmen;
- vor dem neuen Versuch muss der aktuelle Maschinenbestand erneut exakt zur alten Mix-Charge und Menge des abgelehnten Requests passen; andernfalls wird der Versuch vor jeder schreibenden Materialbuchung als Bestandskonflikt gestoppt;
- sollen Buchungsdaten geaendert werden, ist stattdessen ein normaler neuer Vorgang erforderlich.

Auch historische STAGING-Vorgaenge, bei denen eine fruehere Frontend-/Recovery-Version einen bereits protokollierten `REJECTED`-Status spaeter irrtuemlich in `MANUAL_REVIEW_REQUIRED` ueberschrieben hat, werden fuer diesen Zweck als bestaetigt abgelehnt erkannt, wenn keine Oxaion-Belegnummer vorhanden ist und die Ereignishistorie eindeutig ein `REJECTED` enthaelt.

## Oxaion-Laufzeitbenutzer

Der technische Oxaion-Benutzer wird fuer den STAGING-Test nicht in `appsettings.json` fest vorgegeben. Benutzer und Passwort werden beim Serverstart gesetzt. Mit dem Startskript werden beide Werte interaktiv abgefragt.

Wichtig: Der Health-Check `CONNECT + LB20100J *LOADNEW/*NEW` bestaetigt nur Login und diese nicht persistierenden Programmschritte fuer den gewaehlten Benutzer. Er beweist **nicht**, dass derselbe Benutzer auch `LB20100J *PUTNEW`, `LB20115J` und `LB20110R *UPD` mit denselben Berechtigungen beziehungsweise benutzerspezifischen Oxaion-Vorgaben wie der bisher getestete technische Benutzer ausfuehren kann. Unterschiede muessen anhand der konkreten Oxaion-Meldung bewertet werden; fehlende Berechtigungen oder Benutzerparameter duerfen nicht als Ursache erfunden werden.

## Lokaler Start

Voraussetzung fuer den Quellcode-Start: .NET 8 SDK.

Manueller Quellcode-Start ohne Startskript:

```powershell
$env:Oxaion__User = "<STAGING-Oxaion-Benutzer>"
$env:Oxaion__Password = "<STAGING-Passwort>"
dotnet run --project .\src\Fam.Pulverentnahme.Web\Fam.Pulverentnahme.Web.csproj --urls http://0.0.0.0:5080
```

Danach am PC:

```text
http://localhost:5080
```

oder am Android-Geraet im selben Netz:

```text
http://<IP-DES-WEBSERVERS>:5080
```

Fuer Windows-Tests ohne lokal installiertes .NET SDK erzeugt GitHub Actions auf `main` zusaetzlich das self-contained Artifact `FAM-Pulverentnahme-STAGING-win-x64`. Dieses Paket enthaelt die benoetigte .NET-Laufzeit und wird mit `START_STAGING.bat` gestartet. Beim Start werden zuerst der Oxaion-STAGING-Benutzer und danach dessen Passwort abgefragt.

Fuer IIS spaeter normal mit `dotnet publish` veroeffentlichen und das ASP.NET Core Hosting Bundle verwenden.

## Zugangsdaten und Secrets

Weder Oxaion-Benutzer noch Oxaion-Passwort werden fuer den STAGING-Prototyp im Frontend oder fest in `appsettings.json` hinterlegt.

Fuer den Test werden beide als Laufzeitkonfiguration gesetzt:

```text
Oxaion__User
Oxaion__Password
```

Das Passwort wird vom Startskript verdeckt abgefragt. Die Laufzeitvariablen werden nur fuer den gestarteten Backend-Prozess gesetzt und beim Ende des Startskripts wieder entfernt.

Im IIS-Betrieb sollen technische Zugangsdaten ueber eine geschuetzte Server-/Prozesskonfiguration bereitgestellt werden.

## Noch nicht Teil dieses Prototyps

- artikelunabhaengige Ermittlung des gesamten Maschinenbestands zur sicheren Unterscheidung `leer` / `anderes Pulver`;
- live bestaetigter Backend-Start der `US30600J`-Lesetransaktion mit leerer Eltern-`SSID`;
- atomare Reservierung/Sperrung zwischen Bestands-Revalidierung und erster Materialbuchung;
- FA-QR-Scan und FA-Materialrueckmeldung;
- finale Authentifizierung der WebApp-Benutzer;
- produktive Transaktionsdatenbank statt JSON-Dateistore;
- finaler Pulverwechsel-/Ruecklagerungsprozess;
- finaler Scanner-/PWA-Endausbau.
