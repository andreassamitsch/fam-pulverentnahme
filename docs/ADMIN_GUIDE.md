# FAM Pulverentnahme - Administratorhandbuch

Stand: 05.10.2026

## Zielgruppe

Dieses Handbuch richtet sich an Administratoren und technische Betreuer von APP-01, IIS, Windows-Dienst, SQL-Zugriff und Oxaion-Anbindung.

Fachliche Bedienablaeufe fuer Produktionsmitarbeiter stehen in `docs/OPERATOR_GUIDE.md`.

## 1. Betriebsmodell

Der regulaere Serverbetrieb erfolgt als Windows-Dienst hinter IIS.

- Windows-Dienst: `FAMPulverentnahme`
- Kestrel PWA/API: `127.0.0.1:5080`
- lokale Admin-Oberflaeche: `127.0.0.1:5081/admin`
- externer Zugriff: IIS/HTTPS -> Reverse Proxy auf Port 5080
- Dienststart: automatisch

Port 5081 ist ausschliesslich fuer die lokale Administration auf APP-01 vorgesehen und darf nicht ueber IIS extern veroeffentlicht werden.

## 2. Installation und Update

Regulaere Releases werden als MSI bereitgestellt:

`FAM-Pulverentnahme-Setup-<Version>-x64.msi`

Der stabile auf Android/STAGING praxisgetestete und freigegebene Stand auf `main` ist `0.1.7`. Aktueller Release Candidate ist `0.1.8` mit der neuen Lagerplatz-Umlagerung aus der Lageruebersicht; dieser schreibende Vorgang muss vor Freigabe noch real in STAGING getestet werden.

### Erstinstallation

1. MSI als Administrator starten.
2. Die Anwendung wird unter `%ProgramFiles%\FAM Pulverentnahme` installiert.
3. Der Dienst `FAMPulverentnahme` wird angelegt.
4. Starttyp wird auf automatisch gesetzt.
5. Der Dienst wird nach der Installation gestartet.
6. Danach lokal auf APP-01 `http://127.0.0.1:5081/admin` oeffnen und die Laufzeitkonfiguration eintragen.

### Upgrade

Bei einem normalen MSI-Upgrade wird der vorhandene Dienst kontrolliert gestoppt, die Programmdateien werden ersetzt und der Dienst wird danach automatisch wieder gestartet.

Die maschinenweite Konfiguration unter `%ProgramData%\FAM-Pulverentnahme` liegt ausserhalb des Installationsverzeichnisses und soll bei normalen Upgrades erhalten bleiben.

Die MSI veraendert keine bestehende IIS-Site, HTTPS-Bindung oder Zertifikatskonfiguration.

### GitHub Actions / Build-Artefakte

Nach einem erfolgreichen Build auf `main` beziehungsweise dem aktuellen Entwicklungsbranch mit MSI-Erzeugung loescht der Workflow automatisch alle aelteren GitHub-Actions-Artefakte dieses Repositorys. Erhalten bleiben nur die Artefakte des aktuellen erfolgreichen Builds (aktuelle MSI und aktuelles STAGING-Diagnosepaket).

Die Workflow-Historie bleibt erhalten; bereinigt werden die gespeicherten Build-Artefakte.

Eine bereits bereitgestellte MSI-Version ist unveraenderlich. Wird nach einem installierbaren Teststand Code, Frontend, Service Worker oder Installer geaendert, muss die naechste Versionsnummer verwendet werden. Der stabile, freigegebene Stand wird nach erfolgreichem Praxistest ueber einen gruenen Pull Request nach `main` uebernommen. Details stehen in `docs/DEVELOPMENT_WORKFLOW.md`.

### Nach jedem Update pruefen

1. `FAMPulverentnahme` laeuft.
2. Port 5080 lauscht lokal.
3. Port 5081 ist lokal erreichbar.
4. `http://127.0.0.1:5081/admin` zeigt die gespeicherte Konfiguration.
5. `Aktive Verbindungen testen` ist erfolgreich.
6. IIS-Aufruf der PWA funktioniert ueber HTTPS.
7. PWA zeigt die richtige Umgebung `STG` oder `PROD`.
8. `/api/machines` liefert die Tanklagerorte der aktuell gewaehlten Oxaion-Umgebung.

## 3. Windows-Dienst pruefen

PowerShell als Administrator:

```powershell
Get-Service FAMPulverentnahme
```

Dienst neu starten:

```powershell
Restart-Service FAMPulverentnahme
```

Lokale Listener pruefen:

```powershell
Get-NetTCPConnection -LocalPort 5080,5081 -State Listen
```

Backend direkt pruefen:

```powershell
curl.exe --noproxy "*" http://127.0.0.1:5080/api/health
```

Admin-Oberflaeche lokal pruefen:

```powershell
curl.exe --noproxy "*" -I http://127.0.0.1:5081/admin
```

## 4. IIS

IIS bleibt der externe HTTPS-Endpunkt.

Der Reverse Proxy muss auf folgendes Ziel zeigen:

`http://127.0.0.1:5080`

Die HTTPS-Adresse der produktionsnahen PWA wird im IIS gepflegt. Zertifikat und externe Hostnamen sind keine Aufgabe des Backend-Dienstes oder der MSI.

Port 5081 darf nicht als IIS-Route oder extern erreichbare Site veroeffentlicht werden.

## 5. Lokale Serverkonfiguration

Direkt auf APP-01 im Browser:

`http://127.0.0.1:5081/admin`

Die Konfiguration ist maschinenweit und wird unter folgendem Pfad gespeichert:

`%ProgramData%\FAM-Pulverentnahme\service-config.json`

### Umgebung

Auswahl:

- `STAGING`
- `PRODUCTION`

PRODUCTION erfordert eine zusaetzliche bewusste Bestaetigung.

Beim Umgebungswechsel werden vorhandene Mitarbeiter-Sessions ungueltig. Bediener muessen sich erneut anmelden.

### User Timeout bei Inaktivitaet

In der lokalen Admin-Oberflaeche kann `User Timeout bei Inaktivitaet (Minuten)` eingestellt werden.

- zulaessiger Bereich: **5 bis 1440 Minuten**
- Default fuer bestehende Installationen: **480 Minuten**
- die Einstellung wird in `service-config.json` gespeichert
- eine Aenderung gilt serverseitig ab dem naechsten Request; ein Neustart des Dienstes ist nicht erforderlich
- nur echte Bedieneraktivitaet verlaengert die Frist; Healthchecks und automatische Hintergrundabfragen nicht
- nach Ablauf wird die Personal-Session serverseitig verworfen und die PWA verlangt eine neue Anmeldung

Fuer den produktiven Betrieb sollte der Wert so gewaehlt werden, dass gemeinsam genutzte Produktionsgeraete nicht dauerhaft unter dem vorherigen Mitarbeiter angemeldet bleiben.

### Gemeinsame SQL-Anmeldung

Einmal zentral konfigurieren:

- SQL Server / Instanz
- SQL Benutzer
- SQL Passwort
- `Encrypt`
- `TrustServerCertificate`

Diese native SQL-Anmeldung wird fuer Syncos und fuer die dokumentierten rein lesenden Oxaion-SQL-Funktionen verwendet.

### Syncos-Ziele

Voreinstellung:

- STAGING: `syncos_stg_102`
- PRODUCTION: `syncos_prd_102`
- Schema: aktuell `ITSDEV`

Syncos wird fuer die Mitarbeiter-/RFID-/Passwortwege verwendet.

### Oxaion SQL

Die exakten Oxaion-SQL-Datenbanknamen fuer STAGING und PRODUCTION werden in der Admin-Oberflaeche eingetragen.

Diese Namen duerfen nicht aus Annahmen im Code erzeugt werden.

Oxaion SQL ist fuer die WebApp rein lesend. Benötigt werden aktuell unter anderem Leserechte fuer:

- `OXAION.ULGSTP` - dynamische Tanklagerorte
- `OXAION.LLPWEP` - Lagerplatzbestand
- `OXAION.LLAWEP` - Lagerortbestand
- weitere im Repository dokumentierte rein lesende Zielort-/Bestandsabfragen

Keine INSERT-/UPDATE-/DELETE-Rechte auf Oxaion-ERP-Tabellen sind fuer die WebApp erforderlich.

### Oxaion HTTP

Voreinstellung:

- STAGING: `http://oxapp.cnc-domain.fuchshofer:11118`
- PRODUCTION: `http://oxapp.cnc-domain.fuchshofer:11108`
- Firma: `103`

Zusaetzlich werden Oxaion-Benutzer und Oxaion-Passwort hinterlegt.

Der Backend-Sicherheitscheck blockiert den Produktionsport 11108 im STAGING-Modus und den STAGING-Port 11118 im PRODUCTION-Modus.

## 6. Secrets

SQL-Passwort und Oxaion-Passwort werden mit Windows DPAPI `LocalMachine` verschluesselt gespeichert.

Die Admin-API liefert gespeicherte Passwoerter niemals zurueck. Die Oberflaeche zeigt nur an, ob ein Passwort vorhanden ist.

Ein leeres Passwortfeld beim Speichern bedeutet: bestehendes gespeichertes Passwort beibehalten.

Secrets duerfen nicht in:

- Git
- `appsettings.json`
- JavaScript
- MSI-Projektdateien
- Build-Logs
- Support-Screenshots

geschrieben werden.

## 7. Verbindungstest

In der lokalen Admin-Oberflaeche steht `Aktive Verbindungen testen` zur Verfuegung.

Geprueft werden:

1. Syncos SQL und Leseberechtigung auf `ITSUSER`.
2. Oxaion SQL und Leseberechtigung auf Lagerbestand.
3. Oxaion SQL und Leseberechtigung auf `ULGSTP` fuer Tanklagerorte.
4. Oxaion HTTP-Anmeldung / App-Tunnel.

Ein erfolgreicher Verbindungstest bedeutet nur, dass die technischen Verbindungen funktionieren. Er ist keine Freigabe fuer eine reale Produktivbuchung.

## 8. Dynamische Maschinentanks

Tanklagerorte werden nicht in einer App-Konfigurationsliste gepflegt.

Fuehrende Abfrage:

```sql
SELECT
    LG.LGLAGO,
    LG.LGBEZC
FROM OXAION.ULGSTP AS LG
WHERE LG.LGFIRM = @firm
  AND LG.LGLGART = N'02'
ORDER BY LG.LGLAGO;
```

Wenn ein Tank in der App fehlt:

1. aktive Umgebung `STG`/`PROD` pruefen;
2. kontrollieren, ob die richtige Oxaion-SQL-Datenbank ausgewaehlt ist;
3. SQL-Benutzer auf `OXAION.ULGSTP` pruefen;
4. direkt in der aktiven Oxaion-Datenbank kontrollieren, ob der Lagerort fuer Firma 103 `LGLGART = '02'` besitzt;
5. `/api/machines` pruefen;
6. erst danach Frontend/PWA-Cache untersuchen.

Der am 02.10.2026 bestaetigte PRD-Snapshot enthaelt `EOS1`, `EOS2`, `M400-01`, `M400-02` und `M650`. Dieser Snapshot ist keine Whitelist.

## 9. Lagerbestand

Die Lageruebersicht verwendet:

- Maschinentanks aus `ULGSTP / LGLGART = '02'`;
- RP.*-Pulverlagerbestand aus den dokumentierten Oxaion-SQL-Lesewegen;
- Artikel-Erkennungsfarben `EFA01`/`EFA02` ueber Oxaion HTTP-Sachmerkmale.

Wenn die App andere Lagerdaten als eine direkte SQL-Abfrage zeigt, zuerst kontrollieren:

1. aktive Umgebung;
2. Oxaion-SQL-Datenbankname der aktiven Umgebung;
3. Firma;
4. SQL-Rechte;
5. `/api/inventory/overview` beziehungsweise `/api/inventory/rp-stock`;
6. erst danach Browserdarstellung.

## 10. Erkennungsfarben EFA01 / EFA02

Die Farbfelder sind eine visuelle Bedienhilfe.

Referenz `RP.00010`:

- `EFA01 = 0D0D0D = Schwarz`
- `EFA02 = 7030A0 = Violett`

Fehlt eine Farbe, darf dies keine Materialbuchung freigeben oder blockieren.

Bei Problemen zuerst den Backend-Aufruf fuer die Artikel-Erkennungsfarben und danach den dokumentierten Oxaion-Sachmerkmalsweg in `docs/OXAION_ARTICLE_RECOGNITION_COLORS.md` pruefen.

## 11. Health und Diagnose

Wichtige Endpunkte:

- `GET /api/health`
- `GET /api/health/oxaion`
- `GET /api/machines`
- `GET /api/inventory/overview`

Die normale Produktionsoberflaeche zeigt einen Verbindungsstatus in der Kopfzeile.

`Backend erreichbar` oder `Oxaion erreichbar` ist nur ein Connectivity-Signal. Buchungen werden trotzdem unmittelbar vor dem Schreiben serverseitig revalidiert.

Diagnose-/Dev-Infos sind im normalen Produktionsbetrieb ausgeblendet und koennen nur serverseitig freigegeben werden.

Ab `0.1.7` zeigt `Diagnose kopieren` zusaetzlich:

- geladene Versionen von `app.js`, `submit.js`, `worker-enhancements.js` und `ui-diagnostics.js`;
- die vom **aktiven** Service Worker gemeldete Cachegeneration sowie vorhandene `fam-pulver-*`-Caches;
- den aktuell sichtbaren Text und die CSS-Klasse von `stockStatus`;
- die letzte bereinigte Antwort von `/api/machines/{warehouse}/stock` mit HTTP-Status, fachlichem Status (`EMPTY`, `UNIQUE`, `AMBIGUOUS`, ...), Meldung, Zeilenanzahl und den fachlich relevanten Bestandsfeldern.

Damit laesst sich unterscheiden, ob ein Fehler von einem alten PWA-Stand oder von einem tatsaechlich anderen Backend-/Oxaion-Ergebnis stammt. Passwoerter, Tokens, Connection Strings, Personalnummern und Mitarbeiternamen werden weiterhin nicht in das UI-Diagnoseprotokoll aufgenommen.

## 12. Transaktionen und aktuelle Persistenz

Der aktuelle Stand speichert Transaktions- und Auditdaten serverseitig als JSON unter `App_Data`.

STAGING und PRODUCTION werden in getrennten Unterverzeichnissen gehalten.

Die finale produktive Persistenztechnik und Aufbewahrungsdauer sind noch nicht abgeschlossen. Details stehen in `docs/OPEN_POINTS.md`.

Bei `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED` niemals einfach den gleichen fachlichen Vorgang neu starten. Zuerst Oxaion und die gespeicherte Operation klaeren.

## 13. Typische Stoerungsbilder

### Dienst nicht erreichbar

- `Get-Service FAMPulverentnahme`
- Ports 5080/5081 pruefen
- Dienst neu starten
- danach `/api/health` testen

### IIS liefert 502

Zuerst pruefen, ob der Backend-Dienst auf `127.0.0.1:5080` lauscht und ob die IIS-Rewrite-/Reverse-Proxy-Regel auf genau diesen Port zeigt.

### App zeigt falsche Umgebung

Lokale Admin-Oberflaeche auf APP-01 pruefen. Nach einem Umgebungswechsel muss der Bediener neu angemeldet werden.

### Tanks fehlen

`ULGSTP / LGLGART = '02'`, aktive Oxaion-SQL-Datenbank und `/api/machines` pruefen.

### Lagerartikel fehlen

Direkte SQL-Abfrage und aktuell konfigurierte Oxaion-SQL-Datenbank vergleichen. Keine statische Artikelliste wird verwendet.

### Oxaion-Buchung unklar

Nicht erneut buchen. Status `UNCERTAIN` beziehungsweise `MANUAL_REVIEW_REQUIRED` gemaess `docs/ERROR_HANDLING.md` behandeln.

## 14. Backup / Wiederherstellung

Bei einer Systemsicherung muessen mindestens beruecksichtigt werden:

- `%ProgramData%\FAM-Pulverentnahme\service-config.json`
- aktuelle `App_Data`-Transaktions-/Auditverzeichnisse, solange die JSON-Persistenz verwendet wird
- IIS-Konfiguration und Zertifikat nach den allgemeinen APP-01-Betriebsregeln

DPAPI-geschuetzte Passwoerter sind maschinengebunden. Bei einer Migration auf einen anderen Server ist deshalb mit einer erneuten Eingabe der Secrets in der lokalen Admin-Oberflaeche zu rechnen.

## 15. Release- und Dokumentationspflicht

Bei jeder neuen MSI-Version sind vor der Bereitstellung zu pruefen:

- Build erfolgreich
- Tests erfolgreich
- MSI-Erzeugung erfolgreich
- Service-Stop/Start-Verhalten unveraendert oder bewusst migriert
- Konfigurationsmigration dokumentiert
- `SYSTEM_DOCUMENTATION.md` aktuell
- `ADMIN_GUIDE.md` aktuell
- `OPERATOR_GUIDE.md` aktuell, falls sich Bedienung oder Meldungen geaendert haben
- `OPEN_POINTS.md` aktualisiert
- Versionsnummer gegen `installer/Fam.Pulverentnahme.Setup.wixproj` geprueft
- Frontend-/PWA-Aenderungen besitzen eine wirksame Asset-/Service-Worker-Cache-Aktualisierung
- erforderlicher APP-01/Oxaion/Android-Praxistest erfolgreich, bevor der Stand als freigegeben nach `main` uebernommen wird

Eine Aenderung gilt fuer den Projektstand nicht als vollstaendig dokumentiert, wenn die betroffene System-/Admin-/Bedienerdokumentation veraltet bleibt.
