# STAGING-Prototyp: Realer Pulver-Mix mit Frontend und Backend

## Zweck

Dieser Stand bildet den bereits praktisch getesteten Oxaion-Vorgang als testbare Webanwendung ab:

1. alte Mix-Charge auf neue Mix-Charge umbuchen;
2. zusaetzliche Pulver-/Liefercharge auf dieselbe neue Mix-Charge buchen;
3. beide Quellen in einem Oxaion-Lagerbeleg dokumentieren;
4. Personalnummer/Name in den vorhandenen Oxaion-Freitextfeldern dokumentieren;
5. nach Abschluss die vier erwarteten LM/LN-Bewegungen erneut aus Oxaion lesen und verifizieren.

Es handelt sich bewusst um einen **STAGING-Prototyp** fuer Firma `103` und Port `11118`.

## Bestaetigte Oxaion-Sequenz fuer diesen Prototyp

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

## Fehler-/Recovery-Verhalten

Das Frontend vergibt vor Versand eine `clientOperationId` und speichert den offenen Vorgang in `IndexedDB`.

Das Backend persistiert je `clientOperationId` eine Transaktion unter `App_Data/transactions` und fuehrt dieselbe ID niemals blind ein zweites Mal als neue Oxaion-Buchung aus.

Bei einem unklaren Transportfehler Backend -> Oxaion:

- Status `UNCERTAIN`;
- kein automatischer Blind-Retry;
- `POST /api/mix/{clientOperationId}/reconcile` oeffnet den bekannten Lagerbeleg erneut;
- kein Bestand gebucht -> Position 1 kontrolliert neu aufbauen;
- nur Position 1 vorhanden -> nur Position 2 fortsetzen;
- alle vier Bewegungen vorhanden -> nichts erneut buchen, nur Abschluss/Verifikation;
- anderer Zustand -> `MANUAL_REVIEW_REQUIRED`.

## Lokaler Start

Voraussetzung: .NET 8 SDK.

```powershell
$env:Oxaion__Password = "<STAGING-Passwort fuer KHCSYN>"
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

Fuer IIS spaeter normal mit `dotnet publish` veroeffentlichen und das ASP.NET Core Hosting Bundle verwenden.

## Secrets

Das Oxaion-Passwort steht **nicht** in Frontend, Repository oder `appsettings.json`.

Fuer den Test wird es als Laufzeitkonfiguration gesetzt:

```text
Oxaion__Password
```

Im IIS-Betrieb soll das Secret ueber eine geschuetzte Server-/Prozesskonfiguration bereitgestellt werden.

## Noch nicht Teil dieses Prototyps

- automatische Abfrage der aktuell aktiven Mix-Charge der Maschine;
- unabhaengige Bestandsabfrage vor/nach dem Mix;
- FA-QR-Scan und FA-Materialrueckmeldung;
- finale Authentifizierung der WebApp-Benutzer;
- produktive Transaktionsdatenbank statt JSON-Dateistore;
- finaler Pulverwechsel-/Ruecklagerungsprozess;
- finaler Scanner-/PWA-Endausbau.
