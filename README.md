# FAM Pulverentnahme / Pulverwechsel

Mobile WebApp fuer die sichere, nachvollziehbare Pulverentnahme, Pulvernachfuellung und den Pulverwechsel in der FAM-Produktion mit Oxaion-Integration.

## Status

Neben der fachlichen Grundlage enthaelt das Repository jetzt einen **testbaren STAGING-Prototyp** unter [`src/Fam.Pulverentnahme.Web`](src/Fam.Pulverentnahme.Web).

Der bereits manuell und per HTTP getestete Vorgang **alte Mix-Charge + neue Pulvercharge -> neue Mix-Charge** ist als ASP.NET-Core-Backend mit mobilem Webfrontend umgesetzt. Das Backend verwendet die bestaetigte Oxaion-Sequenz mit `LB20100J`, `LB20090J`, `LB20110R` und `LB20115J`, dokumentiert Personalnummer/Name im Lagerbeleg, verhindert blinde Doppelbuchungen ueber `clientOperationId` und kann einen unklaren Teilzustand gegen den bestehenden Oxaion-Lagerbeleg abgleichen.

Details und Startanleitung: [`docs/STAGING_REAL_MIX_PROTOTYPE.md`](docs/STAGING_REAL_MIX_PROTOTYPE.md).

## Architektur

```text
Android Webbrowser / PWA
  -> HTML/CSS/JavaScript Frontend
  -> ASP.NET Core Backend
  -> Oxaion HTTP-Schnittstelle
  -> Oxaion Fachlogik
```

Das Frontend enthaelt keine Oxaion-Zugangsdaten. Das Oxaion-Passwort wird im Test ueber die Backend-Laufzeitvariable `Oxaion__Password` gesetzt und niemals in Git gespeichert.

Fuer die rein lesende RP.*-Lagerbestandsansicht verwendet das Backend zusaetzlich eine **eigene Oxaion-SQL-Verbindung** aus `OxaionSql__ConnectionString`. Diese Verbindung ist bewusst von `Syncos__ConnectionString` getrennt, damit die richtige Oxaion-Datenbank gewaehlt wird. SQL wird nicht fuer ERP-Buchungen oder Bestandsaenderungen verwendet.

Fuer kurze Netzwerkausfaelle werden lokale Vorgangsdaten in `IndexedDB` gehalten. Das Backend fuehrt eine serverseitige Transaktion je `clientOperationId`. Ein unklarer Oxaion-Ausgang wird nicht blind wiederholt, sondern ueber den bekannten Lagerbeleg revalidiert.

## Technologie

- Frontend: HTML, CSS, JavaScript, PWA, Service Worker, `IndexedDB`, spaeter Smartphone-Kamera fuer QR-/Barcodes
- Backend: ASP.NET Core / .NET 8, REST API, IIS-faehig
- ERP: Oxaion HTTP-Schnittstelle
- Oxaion SQL: ausschliesslich rein lesende RP.*-Lagerbestandsansicht
- Prototyp-Persistenz: JSON-Transaktionsdateien unter `App_Data/transactions`; spaeter eigene Transaktionsdatenbank vorgesehen

## Schnellstart STAGING

### Ohne Git und ohne lokale .NET-Installation

Der GitHub-Actions-Workflow `Build` erzeugt auf den freigegebenen STAGING-Branches das Artefakt `FAM-Pulverentnahme-STAGING-win-x64` als **self-contained Windows-Paket**. Die .NET-8-Laufzeit ist darin enthalten.

1. Artefakt ZIP herunterladen und komplett entpacken.
2. `START_STAGING.bat` starten.
3. Oxaion-STAGING-Benutzer und -Passwort fuer die HTTP-Fachlogik eingeben.
4. Den **SYNCOS STAGING SQL Connection String** fuer NFC/Passwortpruefung eingeben.
5. Den **OXAION STAGING SQL Connection String** fuer die rein lesende RP.*-Lagerbestandsansicht eingeben.
6. Am PC `http://localhost:5080` oder am Android-Geraet `http://<SERVER-IP>:5080` aufrufen.

Beide SQL-Connection-Strings werden verdeckt abgefragt, nur fuer den laufenden Prozess als Umgebungsvariable gesetzt und nicht im Repository gespeichert. `Syncos__ConnectionString` und `OxaionSql__ConnectionString` sind absichtlich getrennt.

Es ist weder Git noch ein lokal installiertes .NET SDK/Runtime erforderlich.

### Entwicklung mit lokalem .NET 8 SDK

```powershell
.\scripts\start-staging.ps1
```

Das Skript fragt die Oxaion-HTTP-Zugangsdaten sowie die getrennten Syncos- und Oxaion-SQL-Verbindungen verdeckt ab und setzt sie nur fuer den laufenden Backend-Prozess.

Alternativ koennen die Laufzeitwerte vor dem Start als Umgebungsvariablen gesetzt werden:

```powershell
$env:Oxaion__Password = "<STAGING-Passwort>"
$env:Syncos__ConnectionString = "<SYNCOS-STAGING>"
$env:PersonnelAuthentication__ConnectionString = $env:Syncos__ConnectionString
$env:OxaionSql__ConnectionString = "<OXAION-STAGING-READONLY>"
dotnet run --project .\src\Fam.Pulverentnahme.Web\Fam.Pulverentnahme.Web.csproj --urls http://0.0.0.0:5080
```

Der Prototyp blockiert bei `StagingOnly=true` Oxaion-Port `11108` und erwartet Port `11118` sowie Firma `103`.

## Projektwissen fuer ChatGPT / Codex

- Die fachliche Referenz ist [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md).
- Verbindliche Regeln fuer Coding Agents stehen in [`AGENTS.md`](AGENTS.md).
- PWA-, Offline-, Outbox-, Sync- und Update-Regeln stehen in [`docs/OFFLINE_PWA.md`](docs/OFFLINE_PWA.md).
- Fehler-, Retry- und Idempotenzregeln stehen in [`docs/ERROR_HANDLING.md`](docs/ERROR_HANDLING.md).
- Der bestaetigte STAGING-Mix-Ablauf steht in [`docs/STAGING_REAL_MIX_PROTOTYPE.md`](docs/STAGING_REAL_MIX_PROTOTYPE.md).
- Die rein lesende RP.*-Bestandsansicht steht in [`docs/INVENTORY_VIEW.md`](docs/INVENTORY_VIEW.md).
- Ein kopierbarer Repository-first-Projektprompt steht in [`PROJECT_PROMPT.md`](PROJECT_PROMPT.md).

Der Grundsatz lautet: Vor Antworten und Aenderungen zuerst den aktuellen Stand im Repository lesen und gezielt nach bereits vorhandenen Entscheidungen und Implementierungen suchen.
