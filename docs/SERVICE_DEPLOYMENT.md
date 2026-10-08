# Windows-Dienst, IIS und Serverkonfiguration

Stand: 05.10.2026

## Zugehoerige Betriebsdokumentation

Die vollstaendige Administrationssicht steht in `docs/ADMIN_GUIDE.md`. Der Gesamtaufbau steht in `docs/SYSTEM_DOCUMENTATION.md`.

Bei Aenderungen an Windows-Dienst, IIS, MSI, Ports, Konfigurationsoberflaeche, Secrets oder STAGING/PRODUCTION-Umschaltung muessen `SERVICE_DEPLOYMENT.md`, `ADMIN_GUIDE.md` und gegebenenfalls `SYSTEM_DOCUMENTATION.md` gemeinsam aktualisiert werden.

## Zielbild

Der produktionsnahe Serverbetrieb erfolgt nicht mehr ueber einen interaktiven PowerShell-Start, sondern ueber den Windows-Dienst:

`FAMPulverentnahme`

Der Dienst startet automatisch mit Windows und hostet zwei ausschliesslich lokale Kestrel-Endpunkte:

- `127.0.0.1:5080`: PWA und REST-API. Dieser Port ist das Ziel des bestehenden IIS-Reverse-Proxys.
- `127.0.0.1:5081`: lokale Administrationsoberflaeche. Dieser Port wird nicht ueber IIS veroeffentlicht.

Die externe HTTPS-Adresse bleibt Aufgabe von IIS. Die MSI installiert den Backend-Dienst, sie veraendert keine bestehende IIS-Site oder Zertifikatsbindung automatisch.

## Verbindlichkeit fuer zukuenftige Releases

Dieses Dokument beschreibt den verbindlichen Deploymentweg fuer APP-01 und den IIS-Betrieb.

Fuer jede zukuenftig auszuliefernde Serverversion gilt:

- regulaere Bereitstellung als MSI, nicht als manuell gestartete ZIP;
- Dateinamensschema `FAM-Pulverentnahme-Setup-<Version>-x64.msi`;
- vorhandenen Dienst `FAMPulverentnahme` vor dem Ersetzen laufender Dateien stoppen;
- Dienst nach erfolgreichem Upgrade automatisch wieder starten;
- Service-Name `FAMPulverentnahme`, Installationsidentitaet/UpgradeCode und Datenpfad nicht ohne dokumentierte Migration aendern;
- maschinenweite Konfiguration unter `%ProgramData%\FAM-Pulverentnahme` bei Upgrades erhalten;
- IIS-Konfiguration, HTTPS-Binding und Zertifikat nicht ungefragt durch die MSI veraendern;
- vor Bereitstellung Build, Tests und MSI-Erzeugung in CI erfolgreich abschliessen;
- eine bereits zum Test oder Einsatz bereitgestellte MSI-Version nicht mit geaendertem Inhalt erneut erzeugen; jede weitere installierbare Aenderung erhaelt die naechste Versionsnummer;
- die Versionsnummer zentral im WiX-Projekt als `ProductVersion` pflegen; MSI-Dateiname und CI-Artefaktname werden daraus abgeleitet;
- den getesteten und freigegebenen Stand nach erfolgreicher Praxisfreigabe ueber einen gruenen Pull Request nach `main` uebernehmen. Details: `docs/DEVELOPMENT_WORKFLOW.md`.

Die self-contained STAGING-ZIP kann weiterhin als Diagnose-/Entwicklungsartefakt erzeugt werden, ist aber nicht der regulaere APP-01-Updateweg.

### GitHub-Actions-Artefaktspeicher

Der Build-Workflow bereinigt den GitHub-Actions-Artefaktspeicher nach einem erfolgreichen MSI-Build automatisch repositoryweit:

- erhalten bleiben nur die Artefakte des aktuell erfolgreich abgeschlossenen Builds;
- damit bleiben die aktuelle MSI und das aktuelle self-contained STAGING-Diagnosepaket verfuegbar;
- alle Artefakte aelterer Builds werden geloescht;
- Workflow-Runs und deren Historie werden dadurch nicht geloescht.

Diese Regel verhindert, dass alte MSI-/ZIP-Artefakte den Actions-Speicher dauerhaft belegen.

## Installation und Upgrade

Das CI-Artefakt lautet:

`FAM-Pulverentnahme-Setup-0.1.9-x64.msi`

Die MSI ist eine per-machine Installation und benoetigt Administratorrechte.

Beim Erstinstallieren:

1. Anwendungsdateien werden unter `%ProgramFiles%\FAM Pulverentnahme` installiert.
2. `%ProgramData%\FAM-Pulverentnahme` wird fuer die maschinenweite Konfiguration angelegt.
3. Der Dienst `FAMPulverentnahme` wird mit Starttyp automatisch registriert.
4. Der Dienst wird nach der Installation gestartet.

Beim Major Upgrade:

1. Ein vorhandener Dienst wird kontrolliert gestoppt.
2. Die Anwendungsdateien werden aktualisiert.
3. Die maschinenweite Konfigurationsdatei unter `%ProgramData%` bleibt ausserhalb des Installationsverzeichnisses erhalten.
4. Der Dienst wird nach dem Upgrade wieder gestartet.

Der Dienst laeuft im aktuellen Installer als `LocalSystem`. Eine spaetere weitere Haertung auf ein dediziertes Servicekonto kann separat entschieden werden, sofern fuer Netzwerk-/SQL-Zugriffe erforderlich. Die SQL-Verbindung selbst verwendet eine native SQL-Anmeldung und ist damit nicht von der Windows-Identitaet des Dienstes abhaengig.

## Erstkonfiguration

Direkt auf APP-01 im Browser oeffnen:

`http://127.0.0.1:5081/admin`

Diese Oberflaeche ist absichtlich nur ueber den lokalen Admin-Port erreichbar.

### Umgebung

Auswahl:

- `STAGING`
- `PRODUCTION`

PRODUCTION benoetigt eine zusaetzliche bewusste Bestaetigung in der Admin-Oberflaeche.

Die Bediener-PWA zeigt die aktive Umgebung dauerhaft als `STG` oder `PROD` in der Kopfzeile.

Ein Umgebungswechsel verwirft vorhandene Mitarbeiter-Sessions. Der Bediener muss sich danach erneut anmelden.

Die lokale Admin-Oberflaeche verwaltet ab `0.1.5` ausserdem den Bediener-Inaktivitaets-Timeout in Minuten (5 bis 1440, Default 480). Die Einstellung wird maschinenweit in `service-config.json` gespeichert und ohne Dienstneustart fuer nachfolgende Requests wirksam.

### Gemeinsame native SQL-Anmeldung

Einmalig konfigurieren:

- SQL Server / Instanz
- SQL Benutzer
- SQL Passwort
- `Encrypt`
- `TrustServerCertificate`

Dieselben Zugangsdaten werden fuer Syncos und die freigegebenen Oxaion-SQL-Lesewege verwendet. Die Ziel-Datenbank wird separat gewaehlt.

Voreingestellte Syncos-Datenbanken:

- STAGING: `syncos_stg_102`
- PRODUCTION: `syncos_prd_102`
- Schema: `ITSDEV`

Die exakten Oxaion-SQL-Datenbanknamen sind im Repository nicht verbindlich dokumentiert und werden deshalb fuer STAGING und PRODUCTION explizit in der Admin-Oberflaeche eingetragen.

Der native SQL-Benutzer benoetigt nur die fuer die WebApp erforderlichen Leserechte:

- auf der jeweils aktiven Syncos-Datenbank fuer `ITSUSER` (RFID und Passwortpruefung);
- auf der jeweils aktiven Oxaion-Datenbank fuer die dokumentierten rein lesenden Lagerbestands-/Zielortabfragen sowie `OXAION.ULGSTP` zur dynamischen Tankdefinition (`LGLGART = '02'`).

Keine schreibenden Rechte auf Oxaion-ERP-Tabellen sind fuer die WebApp vorgesehen. Materialbuchungen laufen weiterhin ausschliesslich ueber die Oxaion-HTTP-Fachlogik.

### Oxaion HTTP

Voreingestellte Ziele:

- STAGING: `http://oxapp.cnc-domain.fuchshofer:11118`
- PRODUCTION: `http://oxapp.cnc-domain.fuchshofer:11108`
- Firma: `103`

Zusaetzlich werden Oxaion-Benutzer und Oxaion-Passwort in der lokalen Konfiguration hinterlegt.

Der Backend-Sicherheitscheck blockiert Port 11108 im STAGING-Modus und Port 11118 im PRODUCTION-Modus.

## Secret-Speicherung

Die maschinenweite Konfiguration liegt unter:

`%ProgramData%\FAM-Pulverentnahme\service-config.json`

SQL-Passwort und Oxaion-Passwort werden nicht im Klartext gespeichert. Die Anwendung schuetzt sie mit Windows DPAPI `LocalMachine`.

Die Admin-API gibt Passwoerter niemals zurueck. Die Oberflaeche sieht nur, ob bereits ein Passwort gespeichert ist. Ein leeres Passwortfeld beim Speichern bedeutet: vorhandenes Passwort beibehalten.

## Verbindungstest

Die Admin-Oberflaeche bietet `Aktive Verbindungen testen`.

Geprueft werden:

1. Syncos SQL in der aktuell gewaehlten Datenbank durch einen rein lesenden Zugriff auf `ITSUSER`.
2. Oxaion SQL in der aktuell gewaehlten Datenbank durch rein lesende Zugriffe auf Lagerbestand und `OXAION.ULGSTP` fuer Tanklagerorte (`LGLGART = '02'`).
3. Oxaion HTTP durch einen App-Tunnel-Connect.

Ein erfolgreicher SQL-Test ist keine Freigabe fuer produktive Materialbuchungen.

## IIS

Der bestehende IIS-Reverse-Proxy fuer `fam-pulver.fuchshofer.at` soll auf den Dienst unter:

`http://127.0.0.1:5080`

zeigen.

HTTPS, Zertifikat und der externe Hostname bleiben im IIS konfiguriert.

Der lokale Admin-Port `5081` darf nicht als oeffentliche IIS-Route freigegeben werden.

## Umgebungsgetrennte Vorgangsdaten

Serverseitige JSON-Transaktionen und Fehlscan-Audits werden getrennt nach:

- `STAGING`
- `PRODUCTION`

gespeichert.

Vor der Einfuehrung der Umgebungsumschaltung vorhandene ungetrennte JSON-Dateien stammen aus dem bisherigen STAGING-Betrieb und werden einmalig dem STAGING-Unterverzeichnis zugeordnet.

Damit kann eine identische `clientOperationId` oder eine alte STAGING-Transaktion nicht durch einen Umgebungswechsel als PRODUCTION-Transaktion wiederverwendet werden.

Die grundsaetzlich noch offene Entscheidung zur finalen produktiven Persistenztechnik und Aufbewahrungsdauer bleibt in `docs/OPEN_POINTS.md` bestehen.
