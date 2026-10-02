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

Das Frontend enthaelt keine Oxaion- oder SQL-Zugangsdaten. Im Windows-Dienstbetrieb werden eine gemeinsame native SQL-Anmeldung sowie der Oxaion-HTTP-Benutzer ausschliesslich ueber die lokale Serverkonfiguration gepflegt. Passwoerter werden per Windows-DPAPI verschluesselt gespeichert und niemals in Git oder an das Frontend ausgeliefert.

Die aktive Serverumgebung schaltet gemeinsam Syncos, den rein lesenden Oxaion-SQL-Datenbankkatalog und Oxaion HTTP zwischen STAGING und PRODUCTION um. SQL bleibt fuer Oxaion auf dokumentierte rein lesende Informationsabfragen beschraenkt; ERP-Buchungen und Bestandsaenderungen laufen ausschliesslich ueber Oxaion HTTP/Fachlogik.

Fuer kurze Netzwerkausfaelle werden lokale Vorgangsdaten in `IndexedDB` gehalten. Das Backend fuehrt eine serverseitige Transaktion je `clientOperationId`. Ein unklarer Oxaion-Ausgang wird nicht blind wiederholt, sondern ueber den bekannten Lagerbeleg revalidiert.

## Technologie

- Frontend: HTML, CSS, JavaScript, PWA, Service Worker, `IndexedDB`, spaeter Smartphone-Kamera fuer QR-/Barcodes
- Backend: ASP.NET Core / .NET 8, REST API als Windows-Dienst hinter IIS
- ERP: Oxaion HTTP-Schnittstelle
- SQL: gemeinsame native Anmeldung; Syncos-Personalwege und freigegebene rein lesende Oxaion-Informationsabfragen mit umgebungsabhaengigen Datenbankzielen
- Prototyp-Persistenz: JSON-Transaktionsdateien unter `App_Data/transactions`; spaeter eigene Transaktionsdatenbank vorgesehen

## Serverinstallation per MSI

Der bevorzugte APP-01-/IIS-Betrieb wird als Windows-Dienst installiert. GitHub Actions erzeugt:

`FAM-Pulverentnahme-Setup-0.1.0-x64.msi`

Die MSI installiert den Dienst `FAMPulverentnahme`, stoppt ihn bei einem Upgrade kontrolliert und startet ihn nach der Installation wieder. Die Anwendung lauscht im Dienstbetrieb nur lokal:

- `127.0.0.1:5080` – PWA/API fuer den IIS-Reverse-Proxy
- `127.0.0.1:5081/admin` – lokale Serverkonfiguration

Nach der Installation auf APP-01 wird die Konfiguration direkt am Server ueber `http://127.0.0.1:5081/admin` gepflegt. Dort werden eine gemeinsame native SQL-Anmeldung, die STAGING-/PRODUCTION-Datenbankziele sowie Oxaion-Benutzer/-Passwort hinterlegt.

Syncos ist vorbelegt mit:

- STAGING: `syncos_stg_102`
- PRODUCTION: `syncos_prd_102`

Die exakten Oxaion-SQL-Katalognamen fuer STAGING und PRODUCTION muessen passend zur vorhandenen Installation eingetragen werden und werden nicht im Repository angenommen.

Die bestaetigten Oxaion-HTTP-Ziele sind:

- STAGING: Port `11118`
- PRODUCTION: Port `11108`
- Firma: `103`

PRODUCTION muss in der Admin-Oberflaeche bewusst bestaetigt werden. Beim Umgebungswechsel werden vorhandene Bedienersessions ungueltig und serverseitige Transaktionsdaten bleiben zwischen STAGING und PRODUCTION getrennt.

Der bestehende IIS-/HTTPS-Auftritt soll auf `http://127.0.0.1:5080` weiterleiten. Die lokale Admin-Oberflaeche auf Port 5081 wird nicht ueber IIS veroeffentlicht.

Details: [`docs/SERVICE_DEPLOYMENT.md`](docs/SERVICE_DEPLOYMENT.md).

## Legacy-/Entwicklungsstart STAGING

Die bisherigen self-contained ZIP-/PowerShell-Starter bleiben fuer Entwicklung und gezielte STAGING-Diagnose im Repository. Sie sind nicht mehr das Ziel fuer den dauerhaften APP-01-/IIS-Betrieb.

## Projektwissen fuer ChatGPT / Codex

- Die fachliche Referenz ist [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md).
- Verbindliche Regeln fuer Coding Agents stehen in [`AGENTS.md`](AGENTS.md).
- PWA-, Offline-, Outbox-, Sync- und Update-Regeln stehen in [`docs/OFFLINE_PWA.md`](docs/OFFLINE_PWA.md).
- Fehler-, Retry- und Idempotenzregeln stehen in [`docs/ERROR_HANDLING.md`](docs/ERROR_HANDLING.md).
- Der bestaetigte STAGING-Mix-Ablauf steht in [`docs/STAGING_REAL_MIX_PROTOTYPE.md`](docs/STAGING_REAL_MIX_PROTOTYPE.md).
- Die rein lesende RP.*-Bestandsansicht steht in [`docs/INVENTORY_VIEW.md`](docs/INVENTORY_VIEW.md).
- Ein kopierbarer Repository-first-Projektprompt steht in [`PROJECT_PROMPT.md`](PROJECT_PROMPT.md).

Der Grundsatz lautet: Vor Antworten und Aenderungen zuerst den aktuellen Stand im Repository lesen und gezielt nach bereits vorhandenen Entscheidungen und Implementierungen suchen.
