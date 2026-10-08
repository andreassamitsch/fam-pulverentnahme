# Technische Architektur

## Zielbild

```text
[Android Smartphone]
        |
        | PWA: HTML / JavaScript / Service Worker
        | IndexedDB: lokaler Zustand + Outbox
        | Backend-Personal-Session
        |
        | HTTPS / REST / JSON
        v
[ASP.NET Core Backend]
        |                 |                    |
        | Oxaion HTTP     | Syncos SQL         | Oxaion SQL
        | Buchung/ERP     | Personal read-only | RP.* Bestand read-only
        v                 v                    v
[Oxaion App Server]   [Syncos DB]          [Oxaion DB]
        |
        v
    [Oxaion DB]

Optional, ausschliesslich fuer die WebApp:

[WebApp Transaction DB]
```

Die WebApp verwendet ab 02.10.2026 eine gemeinsame native SQL-Anmeldung fuer Syncos und die freigegebenen rein lesenden Oxaion-SQL-Funktionen. Server/Benutzer/Passwort sind gemeinsam; die jeweilige Datenbank wird aus der aktiven Umgebung gewaehlt. Syncos verwendet STAGING `syncos_stg_102` beziehungsweise PRODUCTION `syncos_prd_102`; die exakten Oxaion-SQL-Katalognamen werden in der lokalen Serverkonfiguration gepflegt. Oxaion bleibt fuer ERP-Stammdaten und Materialbuchungen fachlich fuehrend. Es gibt keine direkten Oxaion-Buchungen per SQL.

## Komponenten und Verantwortlichkeiten

### Android Smartphone und WebApp

- mobile Bedienoberflaeche fuer Produktionsmitarbeiter
- schrittgefuehrter Mitarbeitermodus mit deutlicher Hervorhebung der aktuell erwarteten Aktion
- Diagnose- und `Dev-Infos`-Werkzeuge sind im normalen Betrieb ausgeblendet; sie werden ausschliesslich durch die serverseitige Konfiguration `Prototype:DeveloperToolsEnabled` freigegeben und veraendern keine fachliche Berechtigung
- bevorzugte Mitarbeiter-Anmeldung ueber NFC; Fallback Personalnummer + SYNCOS-Passwort
- Scan von Fertigungsauftrags-, Maschinentank- und Rohmaterial-/Chargencodes ueber die Kamera
- Kamera wird zuerst nur geoeffnet; Barcode-Erkennung startet erst nach bewusstem Druck auf `Scannen`
- Kamera-Zoom wird als reine lokale Geraete-/UI-Praeferenz gespeichert und beim naechsten Kameraoeffnen wieder angewendet, soweit vom Track unterstuetzt; dieser Wert ist kein fachlicher Prozesszustand
- Anzeige von Maschinentank, Pulverartikel, EFA01/EFA02-Erkennungsfarben, Prozessstatus und konkreten Fehlermassnahmen
- beim Chargenscan bleibt die EFA01/EFA02-Sollfarbe sichtbar; ein erkannter Artikel/Charge wird unmittelbar als Ist-Scan gegenuebergestellt
- Anzeige bekannter positiver Lagerorte/Lagerplaetze mit passendem Pulver als Suchhilfe; diese Anzeige ist keine Buchungsfreigabe
- Mengenfelder fuer Nachfuellchargen werden nicht vorbelegt
- PWA mit Web App Manifest und Service Worker
- Offline-faehige App-Shell fuer kurze Netzunterbrechungen
- `IndexedDB` fuer lokale Vorgangsdaten, Maschinenzustands-Cache und Outbox
- `localStorage` nur fuer nicht-fachliche Geraete-/UI-Praeferenzen wie den Kamera-Zoom; keine Buchungs-, Outbox-, Authentifizierungs- oder ERP-Wahrheit daraus ableiten
- Vergabe einer eindeutigen `clientOperationId` bereits beim lokalen Anlegen eines Vorgangs
- klare Trennung zwischen lokalem Sync-Status und serverseitigem Buchungsstatus
- kontrollierte Synchronisation nach Wiederherstellung der Backend-Verbindung
- kontrollierte PWA-Aktualisierung ohne Datenverlust und ohne erzwungenen Reload waehrend kritischer Vorgaenge
- keine Oxaion-Zugangsdaten, Syncos-/Oxaion-SQL-Connection-Strings, Passworttransformationen, Buchungsschluessel oder vertrauenswuerdige Buchungslogik im Frontend
- Nachfuellquellen werden nicht als freie Lagerort-/Lagerplatz-/Chargenschluessel eingegeben, sondern aus den vom Backend gelieferten aktuellen Oxaion-Bestandspositionen bestimmt
- dieselbe Chargennummer darf auf mehreren unterschiedlichen positiven Bestandspositionen verwendet werden; Duplicate Prevention bezieht sich auf die exakte Kombination aus Lagerort, internem Lagerplatz und Charge
- in der Lageruebersicht kann eine positive RP.*-Position mit konkretem Lagerplatz bewusst fuer `Umlagern` ausgewaehlt werden; Artikel, Charge und Quelle sind dabei fix, die aktuelle volle Positionsmenge wird vorgeschlagen und kann reduziert werden
- Ziellagerort/-lagerplatz werden nur aus Backend-Treffern gewaehlt; dynamische Maschinentank-Lagerorte werden nicht angeboten und serverseitig zusaetzlich blockiert

Der Browser-/Geraetespeicher ist nur ein Zwischenpuffer. Er ist nicht die fachlich fuehrende Datenhaltung.

### Mitarbeiter-Session

Beide Anmeldewege setzen dieselbe ASP.NET-Core-Session:

- NFC: Web NFC -> Backend -> Syncos RFID/ObjectKey -> exakte Oxaion-Personalpruefung -> Session
- Fallback: Oxaion-Personalauswahl -> Backend-Passwortpruefung gegen Syncos -> Session

Die Session enthaelt nur die benoetigte Mitarbeiteridentitaet. Das Klartextpasswort wird nicht in IndexedDB, Transaktionsdaten oder Logs gespeichert.

Die Personal-Session besitzt einen serverseitig konfigurierbaren Inaktivitaets-Timeout (5 bis 1440 Minuten, Default 480). Nur explizite Bedieneraktivitaet verlaengert die Frist; Hintergrund-/Health-Requests tun dies nicht. Das Backend erzwingt den Ablauf unabhaengig vom Client.

Vor `/api/mix` prueft ein Endpoint-Filter, dass Session-Personalnummer und -Name exakt zum Request passen. Innerhalb des Buchungsablaufs wird die Person direkt vor den ersten schreibenden Oxaion-Aufrufen nochmals ueber den bestaetigten Oxaion-Personalweg gelesen. Die Session ersetzt diese fachliche Revalidierung nicht.

Details siehe `docs/PERSONNEL_AUTHENTICATION.md`.

### Serverseitige UI-Freigabe

Der Endpoint `/api/ui-config` liefert ausschliesslich nicht-sensitive UI-Konfiguration. Aktuell werden damit `developerToolsEnabled`, die aktive Umgebung und der konfigurierte Personal-Inaktivitaets-Timeout an die PWA uebergeben. `developerToolsEnabled` ist standardmaessig `false`. Der Schalter dient nur zur Sichtbarkeit von Diagnose-/Entwicklerwerkzeugen; Authentifizierung, Buchungsfreigaben und Oxaion-Revalidierung werden davon nicht beeinflusst.

### Serverdienst und lokale Administrationsoberflaeche

Die Anwendung hat im Windows-Dienstbetrieb zwei strikt lokale Kestrel-Endpunkte:

- `127.0.0.1:5080`: PWA/API fuer den IIS-Reverse-Proxy.
- `127.0.0.1:5081`: lokale Serverkonfiguration unter `/admin`.

`/admin` und `/api/admin/*` werden auf Port 5080 mit 404 abgewiesen. Auf Port 5081 werden Nicht-Admin-Pfade auf `/admin` umgeleitet. Damit ist die Secret-Konfiguration nicht Bestandteil der extern erreichbaren PWA.

Der STAGING/PRODUCTION-Schalter ist eine serverseitige Betriebsentscheidung. PRODUCTION muss in der Admin-Oberflaeche zusaetzlich bewusst bestaetigt werden. Im PWA-Header wird die aktive Umgebung als `STG` beziehungsweise `PROD` sichtbar angezeigt.

### Service Worker

Der Service Worker verwaltet ausschliesslich die fuer die PWA geeignete Offline-Infrastruktur:

- Cache der statischen App-Shell einschliesslich der Worker-/Dev-UI-Schicht
- Erkennung und kontrollierte Bereitstellung neuer Frontend-Versionen
- optional technische Unterstuetzung fuer spaetere Sync-Mechanismen

Schreibende API-Antworten, Authentifizierungsantworten und Oxaion-Buchungsergebnisse duerfen nicht aus einem Service-Worker-Cache als fachliche Wahrheit verwendet werden.

### IndexedDB und lokale Outbox

`IndexedDB` speichert mindestens:

- lokale Scans und noch nicht synchronisierte Bedienvorgaenge
- `clientOperationId`
- lokalen Sync-Status
- letzte eindeutig bestaetigte Maschinenzustaende mit Zeitstempel und, sofern vorhanden, Revision/ETag
- Synchronisationsversuche und serverseitige Referenzen, soweit sicher und erforderlich

Passwoerter und Personal-Session-Cookies werden nicht in der fachlichen IndexedDB-Outbox gespeichert.

Die Outbox muss einen Browser-Neustart und eine kurze Offline-Phase ueberstehen. Ein lokaler Eintrag darf erst dann als fachlich abgeschlossen gelten, wenn das Backend beziehungsweise Oxaion den dafuer erforderlichen Endstatus eindeutig bestaetigt hat.

### ASP.NET Core Backend

- zentrale Vermittlungs- und Kontrollschicht
- Eingabevalidierung und fachliche Ablaufsteuerung
- serverseitige Personal-Session und Buchungsautorisierung
- `POST /api/personnel/nfc`: RFID ueber Syncos aufloesen, Person exakt in Oxaion bestaetigen und bei Erfolg Session setzen
- `POST /api/personnel/login`: manueller Passwort-Fallback; Person in Oxaion bestaetigen und vorhandenes SYNCOS-Passwort serverseitig pruefen
- `GET /api/personnel/session` und `POST /api/personnel/logout` fuer Sessionstatus und Abmeldung
- Login-Rate-Limit fuer den Passwortweg
- `POST /api/scan-events/rejected-charge` fuer strukturierte, authentifizierte Auditereignisse bei fachlich abgelehnten Chargenscans; diese Ereignisse sind keine Oxaion-Buchung und kein Transaktionsstatus `REJECTED`
- STAGING-Persistenz der Fehlscan-Audits als WebApp-eigene JSON-Dateien unter `App_Data/scan-events`; Details und Sicherheitsgrenzen siehe `docs/REJECTED_SCAN_AUDIT.md`
- Vergabe und Persistierung eindeutiger serverseitiger Transaktions- beziehungsweise Vorgangs-IDs
- eindeutige Zuordnung der `clientOperationId` zu einer serverseitigen Transaktion
- serverseitige Idempotenz, Duplicate Prevention und Statusverwaltung
- erneute fachliche Validierung nach Reconnect, bevor eine produktive Oxaion-Buchung erfolgt
- Aufruf ausschliesslich freigegebener Oxaion HTTP-Schnittstellen fuer schreibende ERP-Vorgaenge
- sichere technische Protokollierung ohne Secrets
- Uebersetzung technischer und fachlicher Oxaion-Ergebnisse in klare Bedienermeldungen
- `GET /api/inventory/rp-stock`: rein lesende RP.*-Lagerbestandsansicht ueber die separate Laufzeitverbindung `OxaionSql__ConnectionString`; Details in `docs/INVENTORY_VIEW.md`
- `POST /api/stock-relocation`: eigener idempotenter Buchungsvorgang fuer eine Lagerplatz-Umlagerung aus der Lageruebersicht. Vor dem Schreiben werden Personal, exakte Quellposition/Charge/Bestand, Nicht-Tank-Ziel und Ziel-Lagerplatz erneut bestaetigt; die Buchung verwendet genau eine bestaetigte `LF -> LE`-Transferposition und wird danach auf das exakte Bewegungspaar verifiziert
- dynamische Maschinentankdefinition aus Oxaion SQL `ULGSTP`: aktive Firma plus `LGLGART = '02'`; Lagerortcode aus `LGLAGO`, Bezeichnung aus `LGBEZC`. Keine statische EOS1/EOS2-Whitelist. Details in `docs/MACHINE_TANK_DEFINITION.md`
- lesender Maschinenbestand aus der bestaetigten `LB30230R`-Auflistung `Chargen pro Lagerort`; Details in `docs/OXAION_MACHINE_STOCK_LOOKUP.md`
- der Maschinenbestand ist nicht von einem gespeicherten Oxaion-Filter abhaengig: das Backend liest die vollstaendige `LB30230R`-Liste des Lagerorts und wertet direkt die bestaetigte Bedingung `LLAWEP.LALABE != 0` aus
- EFA01/EFA02 werden nach Artikelableitung ueber den bestaetigten Sachmerkmals-Leseweg geladen und als reine Erkennungshilfe an das Frontend geliefert
- lesende Nachfuellquellen-Endpunkte `GET /api/source-stock/warehouses` und `GET /api/source-stock/positions`; sie kapseln die bestaetigten Oxaion-Auskuenfte `LB30340R` und `LB30430R`
- fuer Lagerorte ohne Lagerplatzorganisation wird ausschliesslich bei eindeutigem Oxaion-Code `LAG1626` auf den bestaetigten `LB30230R`-Lagerortbestand zurueckgegriffen; der Lagerplatz bleibt leer
- vor einem neuen Mix-Buchungsversuch: Session zum Mitarbeiter pruefen und Mitarbeiter erneut exakt in Oxaion lesen
- vor einem neuen Mix-Buchungsversuch: erneute Bestandsabfrage und Vergleich von Lagerort, Artikel, Charge und kompletter Maschinenmenge mit dem vom Frontend vorbereiteten Request; bei Abweichung keine schreibende Materialbuchung starten
- zusaetzlich vor einem neuen Mix-Buchungsversuch: jede Nachfuellquelle anhand von Artikel, Lagerort, internem Lagerplatzschluessel, Charge und verfuegbarer Menge erneut aus Oxaion lesen; bei Abweichung oder unzureichendem Bestand keine schreibende Materialbuchung starten
- dieselbe exakte Quellenposition Lagerort/Lagerplatz/Charge wird in `SourceStockService.ValidateSourcesAsync` weiterhin serverseitig als Duplicate blockiert; dieselbe Chargennummer auf unterschiedlichen Positionen bleibt zulaessig

Dasselbe `clientOperationId` darf nicht zu mehreren wirksamen Oxaion-Buchungen fuehren. Wiederholtes Senden derselben Outbox-Nachricht muss serverseitig idempotent behandelt werden.

### Syncos SQL

Der aktuelle bestaetigte Einsatz ist rein lesend:

- RFID -> aktiver/sichtbarer `ITSUSER` -> `ObjectKey` als Personalnummer
- Passwort-Fallback -> vorhandenes `ITSUSER.PASSWORD`

Die aktive Syncos-Verbindung wird serverseitig aus der gemeinsamen SQL-Anmeldung und der umgebungsabhaengigen Datenbank erzeugt. Der Browser erhaelt weder Connection String noch gespeicherten Passwortwert. RFID- und Passwortabfrage verwenden dadurch garantiert dieselbe aktive Syncos-Datenbank.

### Oxaion SQL

Der direkte SQL-Zugriff auf Oxaion ist auf genau die dokumentierte rein lesende Informationsfunktion begrenzt:

- RP.*-Chargenbestaende aller Lagerorte und Lagerplaetze fuer die konfigurierte Firma
- Maschinentankdefinition aus `OXAION.ULGSTP` fuer die aktive Firma mit `LGLGART = '02'`
- Die Oxaion-SQL-Verbindung verwendet dieselbe native SQL-Anmeldung wie Syncos, aber einen eigenen, umgebungsabhaengigen Oxaion-Datenbankkatalog.
- Die Katalognamen fuer Oxaion STAGING und PRODUCTION werden lokal konfiguriert und nicht im Code angenommen.
- keine `INSERT`, `UPDATE`, `DELETE`, `MERGE` oder andere schreibende ERP-Manipulationen
- die neue Lagerplatz-Umlagerung nutzt die SQL-Bestandsansicht nur als Bediener-Einstieg. Der schreibende Vorgang laeuft ausschliesslich ueber Oxaion HTTP/Fachlogik und den bestaetigten Materialbelegweg.

Die SQL-Verbindung ist kein Ersatz fuer Oxaion HTTP/Fachlogik und darf nie fuer Materialbuchungen verwendet werden.

### Oxaion Application Server

- Ausfuehrung der freigegebenen Oxaion-Fachlogik
- bevorzugt Nutzung vorhandener BDE-/PPS-Prozesse
- fuehrende Personal-/Artikel-/Lagerfachdaten fuer den Prozess
- Pruefung und Durchfuehrung der ERP-Buchungen
- Bereitstellung von fachlichen Fehlern, Sperrstatus und Buchungsergebnissen, soweit die zu bestaetigenden Schnittstellen dies unterstuetzen

### Oxaion-Datenbank

- bleibt unter Kontrolle der Oxaion-Applikation
- die RP.*-Bestandsansicht darf die bestaetigten Tabellen rein lesend abfragen
- keine direkten ERP-Buchungen, Bestandskorrekturen oder sonstigen Tabellenmanipulationen durch die WebApp

### Optionale WebApp Transaction DB

Eine separate Datenbank darf ausschliesslich WebApp-eigene Informationen verwalten:

- Transaktionslog
- Idempotency Keys beziehungsweise Vorgangs-IDs
- `clientOperationId` und eindeutige Zuordnung zur Backend-Transaktion
- technische und fachliche Status
- Fehlerprotokoll
- Audit Trail, einschliesslich spaeter produktiv zu persistierender Fehlscan-Ereignisse
- Zuordnung von Planmaschine und tatsaechlich verwendeter Maschine

Sie ist kein Ersatz fuer Oxaion als fachlich fuehrendes ERP-System.

## Online-/Offline-Grenze

Online wird der aktuelle Maschinenzustand vor einer produktiven Freigabe ueber das Backend aus der bestaetigten Oxaion-Logik ermittelt.

Im aktuellen STAGING-Nachfuellprototyp wird die ungefilterte `LB30230R`-Lagerortliste bis zum bestaetigten `<STOP/>` gelesen. Fuer `EOS1` enthielt der Referenzdatenstrom 25 Zeilen verschiedener Artikel und Chargen. Das Backend wendet darauf die in der Oxaion-Selektionsmaske nachgewiesene Bedingung `LLAWEP.LALABE <> 0` direkt an. Damit ist die Laufzeitlogik unabhaengig von Namen, Freigabe oder Existenz eines gespeicherten Oxaion-Filters.

Bei genau einem positiven `KGM`-Bestand werden Artikel, alte Mix-Charge und gesamte Restmenge automatisch abgeleitet. Kein Bestand ungleich 0 wird als leerer Maschinentank erkannt. Mehrere Bestaende ungleich 0, negative Bestaende oder unerwartete Mengeneinheiten sperren den Nachfuellvorgang.

Nach erfolgreicher Artikelableitung werden die Erkennungsfarben sowie die positiven Nachfuellquellen online gelesen. Die Mitarbeiteransicht darf daraus bekannte Lagerorte/Lagerplaetze als Suchhilfe anzeigen. Diese Voranzeige ist nicht die Buchungsauswahl: die tatsaechlich verwendete Charge muss gescannt und die daraus ermittelte konkrete Bestandsposition bestaetigt werden.

`LB30340R` liefert Lagerorte/Chargen zum Artikel, `LB30430R` liefert den exakten internen Lagerplatzschluessel, Charge und Lagerplatzbestand. Die WebApp verwendet diese Werte als Auswahl und nicht als editierbare Buchungsschluessel. Details stehen in `docs/OXAION_SOURCE_STOCK_LOOKUP.md`.

Das Backend liest sowohl Maschinenbestand als auch alle ausgewaehlten Nachfuellbestandspositionen unmittelbar vor dem Start einer neuen schreibenden Materialbuchung nochmals und blockiert erkannte Abweichungen. Eine atomare Sperr-/Reservierungsstrategie zwischen letzter Bestandspruefung und erster schreibender Buchung ist weiterhin offen.

Die aktuelle Personal-Anmeldung ist ebenfalls online: NFC benoetigt Syncos und Oxaion; der Passwort-Fallback benoetigt Oxaion und Syncos. Verhalten einer abgelaufenen Personal-Session waehrend spaeterer Offline-Prozesse ist weiterhin gesondert festzulegen.

Die allgemeine RP.*-Bestandsansicht benoetigt online die separate Oxaion-SQL-Verbindung. Ein SQL-Ausfall ist kein Nachweis fuer Leerbestand und darf keine Buchungsentscheidung freigeben.

Offline darf ein zuvor serverseitig bestaetigter Maschinenzustand nur nach den Regeln aus `docs/OFFLINE_PWA.md` verwendet werden. Insbesondere benoetigt er einen Abfragezeitpunkt und muss innerhalb einer noch festzulegenden maximalen Gueligkeitsdauer liegen. Die aktuelle Auswahl einer Nachfuellquelle aus Oxaion ist ein Online-Schritt; eine produktive Buchung wird offline nicht aus einem veralteten Quellenbestand freigegeben.

Nach Wiederherstellung der Verbindung wird der aktuelle serverseitige Zustand erneut validiert. Ein Konflikt wird nicht automatisch aufgeloest oder ueberschrieben.

`navigator.onLine` gilt nicht als verlaesslicher Nachweis der Backend-Erreichbarkeit; dafuer ist ein echter Backend-Health-/Connectivity-Aufruf vorzusehen.

## PWA-Update-Strategie

Eine neue Frontend-Version darf automatisch erkannt und vorgeladen werden. Aktivierung beziehungsweise Reload erfolgen aber erst in einem sicheren Zustand.

Kein erzwungener Reload waehrend:

- laufendem Scan-/Buchungsvorgang
- ungespeicherten lokalen Aenderungen
- laufender Synchronisation
- noch nicht migrationssicher behandelbaren `PENDING_SYNC`-Daten

Service-Worker-Caches und IndexedDB sind strikt getrennt. Das Bereinigen alter App-Caches darf keine Outbox- oder Vorgangsdaten loeschen. IndexedDB-Schemamigrationen muessen noch nicht synchronisierte Daten erhalten.

Details stehen in `docs/OFFLINE_PWA.md`.

## Deployment

- Verbindlicher Serverbetrieb: Windows-Dienst `FAMPulverentnahme` hinter IIS.
- Kestrel lauscht im Dienstbetrieb nur auf Loopback: Hauptanwendung `127.0.0.1:5080`, lokale Administrationsoberflaeche `127.0.0.1:5081`.
- IIS bleibt fuer den externen HTTPS-Zugang und Reverse Proxy auf Port 5080 zustaendig. Port 5081 wird nicht ueber IIS veroeffentlicht.
- Kommunikation zur PWA erfolgt produktiv verschluesselt per HTTPS; Web NFC benoetigt einen sicheren Kontext.
- Installation/Upgrade erfolgt als MSI. Der Installer stoppt einen vorhandenen Dienst, ersetzt die Dateien und startet den Dienst danach automatisch wieder.
- Secrets werden niemals im Repository gespeichert. SQL-Passwort und Oxaion-Passwort werden per Windows-DPAPI `LocalMachine` verschluesselt in der maschinenweiten Konfiguration unter `%ProgramData%\FAM-Pulverentnahme\service-config.json` gespeichert.
- Die lokale Admin-Oberflaeche verwaltet eine gemeinsame native SQL-Anmeldung, die STAGING-/PRODUCTION-Datenbankzuordnung, die Oxaion-HTTP-Ziele und den Oxaion-Laufzeitbenutzer.
- Ein Umgebungswechsel invalidiert bestehende Bedienersessions. Serverseitige JSON-Transaktions- und Auditdateien werden in getrennten `STAGING`-/`PRODUCTION`-Unterverzeichnissen gehalten.
- Die bisherige interaktive STAGING-Startskript-/DPAPI-User-Loesung bleibt nur fuer Entwicklungs-/Legacy-ZIP-Starts bestehen und ist nicht mehr das Ziel fuer den IIS-Serverbetrieb.
- PWA-Assets muessen mit einer kontrollierten Cache- und Versionsstrategie ausgeliefert werden.

## Integrationsgrenzen

- Keine Oxaion-Endpunkte, Programme, Parameter, Tabellenlogik oder Buchungsschluessel werden ohne Bestaetigung angenommen.
- Keine direkten ERP-Buchungen per SQL.
- Syncos-SQL-Zugriff bleibt auf bestaetigte rein lesende Personalzwecke begrenzt.
- Oxaion-SQL-Zugriff bleibt auf die dokumentierte rein lesende RP.*-Lagerbestandsansicht begrenzt.
- Bei unklarem Buchungsergebnis bleibt der Vorgang offen beziehungsweise wird zur manuellen Pruefung markiert; er wird nicht blind wiederholt.
- Offline erfasste Daten sind keine bestaetigten ERP-Buchungen.
- Weitere konkrete Oxaion-Aufrufe, produktive Session-/Rolloutdetails und noch offene Offline-Grenzen sind in `docs/OPEN_POINTS.md` gefuehrt.


### Etikettendruck nach erfolgreichem Tank-Out

Nach erfolgreicher Materialauslagerung wird der optionale Lageretikettendruck als eigene Backend-Operation ausgeführt. Das Backend prüft dazu die eindeutige LE-Zielbewegung des bereits gebuchten Lagerbelegs und verwendet anschließend ausschließlich die im JET-Mitschnitt vom 29.09.2026 bestätigten Oxaion-Druckprogramme. Materialbuchung und Druckstatus bleiben getrennt; die aktuelle Druckerwarteschlange wird aus Oxaion gelesen und nicht fest im Frontend oder Backend hinterlegt.

Für spätere Nachdrucke liefert ein authentifizierter read-only Endpoint die aus dem WebApp-Transaktionsspeicher abgeleiteten erfolgreichen Tank-Out-Kandidaten samt Druckhistorien-Summe und Sperrstatus. Der Nachdruck selbst verwendet denselben schreibenden Druckservice wie der unmittelbare Druck. Ein priorer `UNCERTAIN`-/`MANUAL_REVIEW_REQUIRED`-Druck derselben Tank-Out-ID wird serverseitig als Sperre behandelt. Materialbewegungen werden dabei nicht wiederholt.

### Chargenherkunft

Die Chargenherkunft ist eine rein lesende Oxaion-HTTP-Funktion. Das Backend rekonstruiert die Herkunft nicht selbst per SQL.

Datenfluss:

```text
PWA / API
  -> ChargeOriginService
  -> Oxaion App-Tunnel
  -> US17490J *LOADUSGI / *USGPARAMS (TX_USAGE=CH)
  -> US17476R *GETHDR
  -> US17476R *FIRSTLIST
  -> rekursives Aufklappen von SUBTREES ueber PESSID + PEMPOS
  -> Backend-Filter auf eindeutige Grundchargen
```

Der Endpoint lautet `GET /api/charge-origin`. Der Dienst verwirft Ergebnisse ohne den im Mitschnitt bestaetigten `STOP`-Marker und begrenzt Tiefe, Knotenzahl und Gesamtzeilen fail-closed. Die interne UPOST-`POOBID/FIOBID` ist optional und wird nur weitergegeben, wenn sie bekannt ist; es wird kein Ersatzwert erzeugt. Details siehe `docs/CHARGE_ORIGIN.md`.

### Pulverartikel RP und PB

Die FAM-Pulverartikelkreise `RP.*` (bisheriges Pulver) und `PB.*` (kundenseitig beigestelltes Pulver) werden an der technischen Artikelkreisgrenze gemeinsam erkannt. Die allgemeine, read-only Lager-SQL-Abfrage umfasst beide Praefixe (jeweils fuer `LLPWEP` und `LLAWEP`); die Backend-Verarbeitung validiert beide mit `PowderArticleRules`.

Der bisherige reine Lagerlese-Endpunkt `/api/inventory/rp-stock` bleibt aus Kompatibilitaetsgruenden erhalten. Neuer neutraler Name: `/api/inventory/powder-stock`. Beide liefern dieselbe RP/PB-Pulverauswahl; `/api/inventory/overview` nutzt die gemeinsame Lesequelle.

Artikelvergleich und vorhandene Oxaion-HTTP-Revalidierung bleiben exakt und unveraendert. Eine PB-Kundenbindung ist fachlich offen und kann nicht allein durch Artikelgruppe oder gleicher Pulverbezeichnung ersetzt werden.

### Chargenherkunft PWA-Einstiege (0.1.11)

Die Frontend-Prozessnavigation (`process-mode.js`) stellt die reine Auskunft `charge-origin` mit eigenem Panel bereit und bietet denselben Einstieg in Lagerplatz- und Maschinentankdetails. Ein gemeinsames `charge-origin-ui.js` verarbeitet entweder den bestaetigten QR-Code `Artikel+++Charge` ueber den vorhandenen `scanQrCode`-Dialog oder manuelle Eingabe und ruft nur `GET /api/charge-origin` auf.

Artikel und Charge aus Bestandsdetails werden unveraendert uebernommen, ohne frei editierbare Buchungsmaske. Eine Versions-/Request-Kennung verhindert die Anzeige einer spaet eintreffenden Antwort nach Moduswechsel. Herkunftsergebnisse werden nicht im Service-Worker oder in IndexedDB zwischengespeichert. Die Oxaion-API wurde in `0.1.10` bereits unabhaengig davon realisiert; `0.1.11` fuegt Oberflaeche und PB-Artikelkreis hinzu.

