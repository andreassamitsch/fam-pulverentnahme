# FAM Pulverentnahme - Systemdokumentation

Stand: 08.10.2026

## Zweck und Zielgruppe

Diese Datei ist der zentrale Einstieg in die technische und fachliche Systemdokumentation der FAM-Pulverentnahme.

Sie beschreibt, wie das Gesamtsystem aktuell aufgebaut ist, welche Systeme fuehrend sind, wie STAGING und PRODUCTION getrennt werden und welche Sicherheitsgrenzen fuer Buchungen gelten.

Ergaenzende praxisorientierte Dokumente:

- Administratoren: `docs/ADMIN_GUIDE.md`
- Bediener / Produktion: `docs/OPERATOR_GUIDE.md`
- Serverdienst / MSI / IIS: `docs/SERVICE_DEPLOYMENT.md`
- Maschinentanks: `docs/MACHINE_TANK_DEFINITION.md`
- Buchungsszenarien: `docs/BOOKING_SCENARIOS.md`
- Fehlerbehandlung / Retry / Idempotenz: `docs/ERROR_HANDLING.md`
- PWA / Offline: `docs/OFFLINE_PWA.md`
- Entwicklungs-/Test-/Release-Workflow: `docs/DEVELOPMENT_WORKFLOW.md`
- aktuell offene Punkte: `docs/OPEN_POINTS.md`

## Systemzweck

Die FAM-Pulverentnahme ist eine mobile WebApp fuer die FAM-Produktion. Sie unterstuetzt die sichere und nachvollziehbare Pulverentnahme, Pulvernachfuellung, Tankbefuellung, Fertigungsauftragsbuchung, Jobabbruch-Korrektur, Lagerbestandsanzeige und den Etikettendruck.

Oxaion ist das fuehrende ERP-System. Die WebApp ersetzt Oxaion nicht und fuehrt keine direkten ERP-Buchungen per SQL durch.

## Gesamtarchitektur

```text
Android Smartphone / installierte PWA
        |
        | HTTPS
        v
IIS auf APP-01
        | Reverse Proxy
        v
Windows-Dienst FAMPulverentnahme
ASP.NET Core / Kestrel 127.0.0.1:5080
        |
        +--> Oxaion HTTP / App-Tunnel
        |      ERP-Fachlogik und Materialbuchungen
        |
        +--> Oxaion SQL (nur lesend)
        |      Lagerbestand, Zielorte, Tankdefinition
        |
        +--> Syncos SQL
               Mitarbeiter-/RFID-/Passwortpruefung

Lokale Administration auf APP-01:
Browser -> http://127.0.0.1:5081/admin
```

Das Frontend kommuniziert niemals direkt mit Oxaion oder SQL. Alle fachlichen Pruefungen, Secrets und schreibenden Integrationen liegen im Backend.

## Serverbetrieb

Der verbindliche Serverbetrieb erfolgt als Windows-Dienst:

- Dienstname: `FAMPulverentnahme`
- Starttyp: automatisch
- PWA/API intern: `127.0.0.1:5080`
- lokale Admin-Oberflaeche: `127.0.0.1:5081/admin`
- externer HTTPS-Zugang: IIS-Reverse-Proxy auf Port 5080

Regulaere APP-01-Updates werden als MSI ausgeliefert:

`FAM-Pulverentnahme-Setup-<Version>-x64.msi`

Der Installer stoppt einen vorhandenen Dienst vor dem Austausch der Programmdateien und startet ihn nach erfolgreichem Upgrade wieder. IIS-Site, HTTPS-Binding und Zertifikat werden von der MSI nicht ungefragt veraendert.

Der CI-Workflow haelt den GitHub-Actions-Artefaktspeicher bewusst klein: Nach einem erfolgreichen MSI-Build werden repositoryweit alle aelteren Build-Artefakte geloescht. Erhalten bleiben nur die Artefakte des aktuellsten erfolgreichen Builds.

`main` ist der stabile, getestete und freigegebene Integrationsstand. Installierbare Aenderungen werden auf Feature-/Fix-/Release-Branches vorbereitet, erhalten eine eindeutige neue MSI-Version und werden nach gruenem CI sowie erforderlichem Praxistest per Pull Request nach `main` uebernommen. Eine bereits bereitgestellte MSI-Version wird nicht mit anderem Inhalt wiederverwendet.

Der am 07.10.2026 praxisgetestete und freigegebene Stand ist `0.1.8`. Die Lagerplatz-Umlagerung aus der Pulverlagerliste wurde in Android/STAGING erfolgreich bestaetigt.

Die Mitarbeiteransicht verwendet in `0.1.8` zusaetzlich die vereinfachten sichtbaren Vorgangsbezeichnungen `Tank nachfuellen`, `Pulververbrauch erfassen`, `Tank entleeren`, `Leeren Tank befuellen`, `Jobabbruch. Verbrauch korrigieren`, `Bestaende anzeigen` und `Etiketten nachdrucken`. Dies ist ausschliesslich eine UX-/Textaenderung; interne Prozess-IDs, Endpunkte und Buchungslogik bleiben unveraendert.

## STAGING und PRODUCTION

Die aktive Umgebung wird ausschliesslich serverseitig in der lokalen Administrationsoberflaeche gewaehlt.

### STAGING

- Syncos-Datenbank: `syncos_stg_102`
- Oxaion HTTP: Port `11118`
- Oxaion-SQL-Datenbank: lokal konfigurierter STAGING-Katalog
- Anzeige in der PWA: `STG`

### PRODUCTION

- Syncos-Datenbank: `syncos_prd_102`
- Oxaion HTTP: Port `11108`
- Oxaion-SQL-Datenbank: lokal konfigurierter PRODUCTION-Katalog
- Anzeige in der PWA: `PROD`

Firma `103` ist fuer die FAM-WebApp aktuell verbindlich bestaetigt.

Eine Produktivumschaltung muss in der lokalen Admin-Oberflaeche bewusst bestaetigt werden. Beim Umgebungswechsel werden vorhandene Mitarbeiter-Sessions ungueltig. Serverseitige Transaktions- und Auditdaten werden zwischen STAGING und PRODUCTION getrennt.

## Laufzeitkonfiguration und Secrets

Die maschinenweite Konfiguration liegt unter:

`%ProgramData%\FAM-Pulverentnahme\service-config.json`

Konfiguriert werden:

- aktive Umgebung STAGING / PRODUCTION
- SQL Server / Instanz
- gemeinsamer nativer SQL-Benutzer und SQL-Passwort
- Syncos STAGING-/PRODUCTION-Datenbank
- Syncos-Schema
- Oxaion SQL STAGING-/PRODUCTION-Datenbank
- Oxaion HTTP STAGING-/PRODUCTION-URL
- Oxaion Firma
- Oxaion Benutzer und Passwort
- optionale Entwickler-/Diagnosefreigabe

SQL-Passwort und Oxaion-Passwort werden mit Windows DPAPI `LocalMachine` verschluesselt gespeichert. Klartext-Secrets duerfen weder im Frontend noch im Repository, Installer oder Build-Log stehen.

## Datenquellen und Verantwortlichkeiten

### Oxaion

Oxaion ist fachlich fuehrend fuer:

- Artikel und Chargen
- Lagerbestaende
- Tankbestaende
- Lagerorte und Lagerplaetze
- Fertigungsauftraege und Materialpositionen
- Materialbuchungen und Stornos
- Etikettendruck-Fachlogik

Schreibende ERP-Aktionen erfolgen ausschliesslich ueber bestaetigte Oxaion-HTTP-/App-Tunnel-Fachlogik.

### Oxaion SQL - nur lesend

Direktes SQL ist ausschliesslich fuer dokumentierte Informationsfunktionen freigegeben, zum Beispiel:

- RP.*-Lagerbestandsansicht
- Lagerort-/Lagerplatzauswahl als Bedienhilfe
- dynamische Maschinentankdefinition

Es gibt keine direkten SQL-INSERT/UPDATE/DELETE-Buchungen in Oxaion.

### Syncos SQL

Syncos wird fuer Mitarbeiterfunktionen verwendet, insbesondere fuer die aktuelle RFID-/NFC-Zuordnung und den Passwort-Fallback. Die aktive Syncos-Datenbank folgt dem STAGING-/PRODUCTION-Schalter.

## Dynamische Maschinentanks

Maschinentanks werden nicht statisch in der App gepflegt.

Fuehrende Definition:

```sql
SELECT
    LG.LGLAGO,
    LG.LGBEZC
FROM OXAION.ULGSTP AS LG
WHERE LG.LGFIRM = @firm
  AND LG.LGLGART = N'02'
ORDER BY LG.LGLAGO;
```

`LGLGART = '02'` ist die verbindliche Tanklagerortart. Die Liste wird aus der aktuell aktiven Oxaion-SQL-Datenbank gelesen. Dadurch koennen STAGING und PRODUCTION unterschiedliche Tanks besitzen.

Der am 02.10.2026 direkt bestaetigte PRD-Snapshot fuer Firma 103 enthielt:

- EOS1
- EOS2
- M400-01
- M400-02
- M650

Dieser Snapshot ist keine Whitelist. Neue oder entfernte Tanks werden ueber Oxaion `ULGSTP` wirksam.

## Mitarbeiteranmeldung

Die Bedieneroberflaeche unterstuetzt aktuell:

- Anmeldung per NFC/Personalchip
- alternativ Personalnummer plus Passwort

Nach erfolgreicher Anmeldung bleibt der Mitarbeiter kompakt in der Kopfzeile sichtbar. Eine abgelaufene Session oder ein Umgebungswechsel erzwingt eine neue Anmeldung. Der Inaktivitaets-Timeout wird lokal am Server konfiguriert (5 bis 1440 Minuten, Default 480); nur echte Bedienereingaben verlaengern die Frist.

Vor schreibenden Materialvorgaengen werden Mitarbeiter und fachlicher Zustand serverseitig erneut validiert.

Die serverseitig freischaltbare In-App-Diagnose protokolliert keine Zugangsdaten oder Mitarbeiterdaten. Ab `0.1.7` enthaelt ein Diagnoseexport zusaetzlich die geladenen versionierten Frontend-Skripte, die aktive Service-Worker-Cachegeneration, vorhandene `fam-pulver-*`-Caches, den sichtbaren Text/Klassenstatus des Tankstatusfelds sowie die zuletzt vom Tank-Bestandsendpoint gelieferte bereinigte Antwort (HTTP-Status, fachlicher Status, Meldung, Zeilenanzahl sowie Artikel/Charge/Menge/Einheit der ersten Positionen).

## Bedienprozesse

Die aktuelle Vorgangsuebersicht enthaelt:

1. Pulver nachfuellen
2. Pulver auf Fertigungsauftrag buchen
3. Pulver aus Tank auslagern
4. Neues Pulver in Tank fuellen
5. Korrekturbuchung Fertigungsauftrag / Jobabbruch
6. Lagerbestand
7. Etiketten nachdrucken

Die Detailablaeufe sind im Bedienerhandbuch `docs/OPERATOR_GUIDE.md` beschrieben. Technische Buchungsfolgen stehen in den spezialisierten Dokumenten unter `docs/`.

## QR-Code-Grundformen

- Maschinentank: nur Lagerort, zum Beispiel `EOS1`
- Pulvercharge: `Artikel+++Charge`
- Fertigungsauftrag: `Rohmaterial+++Fertigungsauftrag+++Maschinen-ID`

Der Tank-QR wird dynamisch gegen Oxaion `ULGSTP / LGLGART = '02'` validiert. Lagerorte oder Chargen werden nicht aus einem QR blind als Buchungswahrheit uebernommen; das Backend prueft den aktuellen Oxaion-Zustand.

## Lagerbestand

Die Lageruebersicht besteht aus zwei Bereichen:

1. Maschinentanks: dynamisch aus Oxaion `ULGSTP`, jeweils mit aktuellem Tankzustand.
2. Pulverlager: RP.*-Bestandspositionen mit Lagerort/Lagerplatz, Charge und Menge.

Die Lagerliste selbst bleibt rein lesend. Bei einer positiven RP.*-Position mit konkretem Lagerplatz kann der Bediener jedoch bewusst `Umlagern` starten. Diese Aktion ist ein separater Oxaion-HTTP-Materialvorgang mit eigener `clientOperationId`; sie schreibt nicht per SQL. Quelle/Charge bleiben fix, die volle Positionsmenge wird vorgeschlagen und kann reduziert werden. Ziel-Lagerort/-lagerplatz werden serverseitig validiert; dynamische Maschinentanks sind als Ziel verboten. Vor dem Schreiben wird die exakte Quelle erneut aus Oxaion gelesen und die seit Anzeige erwartete Gesamtmenge verglichen. Die Buchung verwendet eine `LF -> LE`-Position und gilt erst nach exakter Bewegungspaar-Verifikation als erfolgreich.

Bei vorhandenen Oxaion-Sachmerkmalen koennen `EFA01` und `EFA02` als Erkennungsfarben angezeigt werden. Dieselbe Farbdarstellung wird in Maschinentank- und Pulverlagerkarten verwendet. Ein am 02.10.2026 in PRODUCTION nachgewiesener Tankkarten-CSS-Fehler wurde behoben; `RP.00024` liefert `FF0000 / 833C0C` (Rot/Braun) und muss in Tank- und Lagerkarte identisch erscheinen. Artikel ohne gepflegte gueltige EFA-Werte, z. B. der bestaetigte Gegenfall `RP.00026`, zeigen bewusst kein Farbfeld. Die Farben sind nur eine visuelle Bedienhilfe und keine Buchungsfreigabe.

## Buchungssicherheit

Jeder produktive Vorgang besitzt eine eindeutige `clientOperationId` beziehungsweise serverseitige Vorgangs-ID.

Grundregeln:

- vor dem ersten schreibenden Oxaion-Aufruf wird der aktuelle fachliche Zustand erneut gelesen;
- gleiche `clientOperationId` darf keine Doppelbuchung erzeugen;
- eine bereits erfolgreiche Operation wird nicht erneut gebucht;
- bei unklarem Oxaion-Ausgang niemals blind erneut buchen;
- Quellen-, Tank-, FA- und Mitarbeiterdaten werden vor dem Schreiben revalidiert;
- bei Lagerplatz-Umlagerungen werden Quelle, unveraenderter Ausgangsbestand, Menge, Nicht-Tank-Ziel und Ziel-Lagerplatz vor dem Schreiben erneut validiert;
- bei Konflikten wird sicher gestoppt statt ein Wert angenommen.

Wichtige serverseitige Status sind unter anderem:

- `SUCCESS` - eindeutig erfolgreich
- `CONFLICT` - Zustand hat sich geaendert oder passt nicht mehr
- `REJECTED` - Oxaion hat fachlich eindeutig abgelehnt
- `LOCKED` - Oxaion-Datensatz ist gesperrt
- `UNCERTAIN` - Oxaion koennte verarbeitet haben, Ergebnis ist nicht sicher
- `MANUAL_REVIEW_REQUIRED` - automatische Klaerung ist nicht sicher moeglich

Bei `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED` darf derselbe fachliche Vorgang nicht einfach nochmals gestartet werden.

## PWA und Offline-Verhalten

Die Anwendung ist als PWA aufgebaut. Der Service Worker darf die App-Shell lokal cachen.

Offline bedeutet jedoch niemals, dass eine Oxaion-Buchung erfolgreich war. Oxaion und Backend bleiben fuer produktive Buchungen fuehrend. Ein lokal vorbereiteter Vorgang muss nach Wiederherstellung der Verbindung erneut serverseitig validiert werden.

Die vollstaendige fachliche Offline-/Outbox-Ausgestaltung ist noch nicht fuer alle Prozesse abgeschlossen. Verbindliche Details und offene Punkte stehen in `docs/OFFLINE_PWA.md` und `docs/OPEN_POINTS.md`.

## Aktuelle Persistenz

Der aktuelle Backend-Stand verwendet JSON-Dateien fuer Transaktions- und Auditdaten. Diese liegen unter `App_Data` und werden serverseitig nach STAGING und PRODUCTION getrennt.

Die finale produktive Persistenztechnik und Aufbewahrungsstrategie ist weiterhin ein offener Architekturpunkt. Siehe `docs/OPEN_POINTS.md`.

## Monitoring und Health

Wichtige Endpunkte:

- `/api/health` - Backend-/Konfigurationsstatus
- `/api/health/oxaion` - Oxaion-Erreichbarkeit
- `/api/machines` - aktuell dynamisch ermittelte Tanklagerorte
- `/api/inventory/overview` - Lageruebersicht
- `/api/charge-origin` - read-only Oxaion-Chargenherkunft und gefilterte Grundchargen

Die PWA zeigt den Verbindungszustand in der Kopfzeile. Ein gruener Zustand ist nur ein Erreichbarkeitshinweis; die eigentliche Buchungsfreigabe erfolgt immer durch die serverseitige Revalidierung.

## Dokumentationspflege

Diese Systemdokumentation ist Teil des verbindlichen Projektstands.

Bei jeder Aenderung an Architektur, Hosting, Schnittstellen, Datenquellen, Sicherheitsregeln, Umgebungskonfiguration oder Benutzerprozessen muss im selben Arbeitsschritt geprueft werden, ob diese Datei sowie `ADMIN_GUIDE.md` und `OPERATOR_GUIDE.md` angepasst werden muessen.

Die Dokumentation darf nicht erst nachtraeglich oder nur im Chat aktualisiert werden. Dauerhaft relevante Aenderungen gehoeren ins Repository.

## Chargenherkunft

Ab 08.10.2026 ist ein eigener read-only Backend-Leseweg fuer die Oxaion-Chargenherkunft umgesetzt.

- Endpoint: `GET /api/charge-origin?article=<Artikel>&batch=<Charge>&objectId=<optional>`
- Oxaion-Fachlogik: `US17490J` mit Usage `CH`, danach `US17476R`.
- Unterbaeume werden ausschliesslich ueber die von Oxaion gelieferten `PESSID`-/`PEMPOS`-Schluessel und `SUBTREES=TRUE` rekursiv gelesen.
- Die WebApp bildet keine eigene Chargenherkunft aus Oxaion-SQL-Tabellen nach.
- Ausgegeben werden eindeutige Grundchargen; Zwischen-/Mixchargen mit vorhandenem Unterbaum werden ausgefiltert.
- Ein fehlender Oxaion-`STOP`-Marker oder ueberschrittene Sicherheitsgrenzen fuehren zu einem Fehler statt zu einer vermeintlich vollstaendigen Teilantwort.
- `POOBID/FIOBID` wird nur verwendet, wenn eine bestaetigte interne Objekt-ID vorhanden ist. Die Ermittlung dieser ID allein aus Artikel+Charge ist noch per STAGING-Livetest beziehungsweise weiterem bestaetigten Oxaion-Leseweg zu klaeren.

Details siehe `docs/CHARGE_ORIGIN.md`.

