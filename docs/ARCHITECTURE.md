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
        |                       \
        | Oxaion HTTP            \ rein lesend: Personal/RFID/Passwort
        v                         v
[Oxaion Application Server]    [Syncos SQL]
        |
        v
[Oxaion DB]

Optional, ausschliesslich fuer die WebApp:

[WebApp Transaction DB]
```

Syncos SQL wird in diesem Projekt nur fuer die bestaetigten rein lesenden Personalwege genutzt, derzeit RFID-Zuordnung und Passwort-Fallback. Oxaion bleibt fuer ERP-Stammdaten und Materialbuchungen fachlich fuehrend. Es gibt keine direkten Oxaion-Buchungen per SQL.

## Komponenten und Verantwortlichkeiten

### Android Smartphone und WebApp

- mobile Bedienoberflaeche fuer Produktionsmitarbeiter
- schrittgefuehrter Mitarbeitermodus mit deutlicher Hervorhebung der aktuell erwarteten Aktion
- optionaler Schalter `Dev-Infos`, der ausschliesslich technische Informationen ein-/ausblendet und keine fachliche Freigabe veraendert
- bevorzugte Mitarbeiter-Anmeldung ueber NFC; Fallback Personalnummer + SYNCOS-Passwort
- Scan von Fertigungsauftrags-, Maschinentank- und Rohmaterial-/Chargencodes ueber die Kamera
- Kamera wird zuerst nur geoeffnet; Barcode-Erkennung startet erst nach bewusstem Druck auf `Scannen`
- Anzeige von Maschinentank, Pulverartikel, EFA01/EFA02-Erkennungsfarben, Prozessstatus und konkreten Fehlermassnahmen
- Anzeige bekannter positiver Lagerorte/Lagerplaetze mit passendem Pulver als Suchhilfe; diese Anzeige ist keine Buchungsfreigabe
- Mengenfelder fuer Nachfuellchargen werden nicht vorbelegt
- PWA mit Web App Manifest und Service Worker
- Offline-faehige App-Shell fuer kurze Netzunterbrechungen
- `IndexedDB` fuer lokale Vorgangsdaten, Maschinenzustands-Cache und Outbox
- Vergabe einer eindeutigen `clientOperationId` bereits beim lokalen Anlegen eines Vorgangs
- klare Trennung zwischen lokalem Sync-Status und serverseitigem Buchungsstatus
- kontrollierte Synchronisation nach Wiederherstellung der Backend-Verbindung
- kontrollierte PWA-Aktualisierung ohne Datenverlust und ohne erzwungenen Reload waehrend kritischer Vorgaenge
- keine Oxaion-Zugangsdaten, Syncos-Connection-Strings, Passworttransformationen, Buchungsschluessel oder vertrauenswuerdige Buchungslogik im Frontend
- Nachfuellquellen werden nicht als freie Lagerort-/Lagerplatz-/Chargenschluessel eingegeben, sondern aus den vom Backend gelieferten aktuellen Oxaion-Bestandspositionen bestimmt

Der Browser-/Geraetespeicher ist nur ein Zwischenpuffer. Er ist nicht die fachlich fuehrende Datenhaltung.

### Mitarbeiter-Session

Beide Anmeldewege setzen dieselbe ASP.NET-Core-Session:

- NFC: Web NFC -> Backend -> Syncos RFID/ObjectKey -> exakte Oxaion-Personalpruefung -> Session
- Fallback: Oxaion-Personalauswahl -> Backend-Passwortpruefung gegen Syncos -> Session

Die Session enthaelt nur die benoetigte Mitarbeiteridentitaet. Das Klartextpasswort wird nicht in IndexedDB, Transaktionsdaten oder Logs gespeichert.

Vor `/api/mix` prueft ein Endpoint-Filter, dass Session-Personalnummer und -Name exakt zum Request passen. Innerhalb des Buchungsablaufs wird die Person direkt vor den ersten schreibenden Oxaion-Aufrufen nochmals ueber den bestaetigten Oxaion-Personalweg gelesen. Die Session ersetzt diese fachliche Revalidierung nicht.

Details siehe `docs/PERSONNEL_AUTHENTICATION.md`.

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
- Vergabe und Persistierung eindeutiger serverseitiger Transaktions- beziehungsweise Vorgangs-IDs
- eindeutige Zuordnung der `clientOperationId` zu einer serverseitigen Transaktion
- serverseitige Idempotenz, Duplicate Prevention und Statusverwaltung
- erneute fachliche Validierung nach Reconnect, bevor eine produktive Oxaion-Buchung erfolgt
- Aufruf ausschliesslich freigegebener Oxaion HTTP-Schnittstellen
- sichere technische Protokollierung ohne Secrets
- Uebersetzung technischer und fachlicher Oxaion-Ergebnisse in klare Bedienermeldungen
- fuer den STAGING-Nachfuellprototyp: lesender Maschinenbestand aus der bestaetigten `LB30230R`-Auflistung `Chargen pro Lagerort`; Details in `docs/OXAION_MACHINE_STOCK_LOOKUP.md`
- der Maschinenbestand ist nicht von einem gespeicherten Oxaion-Filter abhaengig: das Backend liest die vollstaendige `LB30230R`-Liste des Lagerorts und wertet direkt die bestaetigte Bedingung `LLAWEP.LALABE != 0` aus
- EFA01/EFA02 werden nach Artikelableitung ueber den bestaetigten Sachmerkmals-Leseweg geladen und als reine Erkennungshilfe an das Frontend geliefert
- lesende Nachfuellquellen-Endpunkte `GET /api/source-stock/warehouses` und `GET /api/source-stock/positions`; sie kapseln die bestaetigten Oxaion-Auskuenfte `LB30340R` und `LB30430R`
- fuer Lagerorte ohne Lagerplatzorganisation wird ausschliesslich bei eindeutigem Oxaion-Code `LAG1626` auf den bestaetigten `LB30230R`-Lagerortbestand zurueckgegriffen; der Lagerplatz bleibt leer
- vor einem neuen Mix-Buchungsversuch: Session zum Mitarbeiter pruefen und Mitarbeiter erneut exakt in Oxaion lesen
- vor einem neuen Mix-Buchungsversuch: erneute Bestandsabfrage und Vergleich von Lagerort, Artikel, Charge und kompletter Maschinenmenge mit dem vom Frontend vorbereiteten Request; bei Abweichung keine schreibende Materialbuchung starten
- zusaetzlich vor einem neuen Mix-Buchungsversuch: jede Nachfuellquelle anhand von Artikel, Lagerort, internem Lagerplatzschluessel, Charge und verfuegbarer Menge erneut aus Oxaion lesen; bei Abweichung oder unzureichendem Bestand keine schreibende Materialbuchung starten

Dasselbe `clientOperationId` darf nicht zu mehreren wirksamen Oxaion-Buchungen fuehren. Wiederholtes Senden derselben Outbox-Nachricht muss serverseitig idempotent behandelt werden.

### Syncos SQL

Der aktuelle bestaetigte Einsatz ist rein lesend:

- RFID -> aktiver/sichtbarer `ITSUSER` -> `ObjectKey` als Personalnummer
- Passwort-Fallback -> vorhandenes `ITSUSER.PASSWORD`

Die SQL-Verbindung wird nur serverseitig als Laufzeit-Secret bereitgestellt. Der Browser erhaelt weder Connection String noch gespeicherten Passwortwert.

Diese Integration ist keine Freigabe fuer Material-, ERP- oder sonstige Oxaion-Buchungen per SQL.

### Oxaion Application Server

- Ausfuehrung der freigegebenen Oxaion-Fachlogik
- bevorzugt Nutzung vorhandener BDE-/PPS-Prozesse
- fuehrende Personal-/Artikel-/Lagerfachdaten fuer den Prozess
- Pruefung und Durchfuehrung der ERP-Buchungen
- Bereitstellung von fachlichen Fehlern, Sperrstatus und Buchungsergebnissen, soweit die zu bestaetigenden Schnittstellen dies unterstuetzen

### Oxaion-Datenbank

- bleibt unter Kontrolle der Oxaion-Applikation
- keine direkten ERP-Buchungen oder Tabellenmanipulationen durch die WebApp

### Optionale WebApp Transaction DB

Eine separate Datenbank darf ausschliesslich WebApp-eigene Informationen verwalten:

- Transaktionslog
- Idempotency Keys beziehungsweise Vorgangs-IDs
- `clientOperationId` und eindeutige Zuordnung zur Backend-Transaktion
- technische und fachliche Status
- Fehlerprotokoll
- Audit Trail
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

- Testbetrieb: vorhandener IIS auf dem Datenbankserver ist moeglich.
- Bevorzugter Produktivbetrieb: eigener Web-/Application-Server beziehungsweise eigene VM mit IIS und ASP.NET Core Hosting Bundle.
- Kommunikation erfolgt produktiv verschluesselt per HTTPS; Web NFC benoetigt bereits technisch einen sicheren Kontext.
- Secrets werden ueber eine noch festzulegende sichere Laufzeitkonfiguration bereitgestellt und niemals im Repository gespeichert.
- Der Oxaion-Laufzeitbenutzer ist nicht fest im Anwendungscode konfiguriert; der STAGING-Starter fragt Benutzer und Passwort interaktiv ab.
- Der STAGING-Starter verwendet denselben zur Laufzeit eingegebenen Syncos-Connection-String fuer RFID-Lookup und Passwort-Fallback, ohne ihn im Repository zu speichern.
- PWA-Assets muessen mit einer kontrollierten Cache- und Versionsstrategie ausgeliefert werden.

## Integrationsgrenzen

- Keine Oxaion-Endpunkte, Programme, Parameter, Tabellenlogik oder Buchungsschluessel werden ohne Bestaetigung angenommen.
- Keine direkten ERP-Buchungen per SQL.
- Syncos-SQL-Zugriff bleibt auf bestaetigte rein lesende Personalzwecke begrenzt.
- Bei unklarem Buchungsergebnis bleibt der Vorgang offen beziehungsweise wird zur manuellen Pruefung markiert; er wird nicht blind wiederholt.
- Offline erfasste Daten sind keine bestaetigten ERP-Buchungen.
- Weitere konkrete Oxaion-Aufrufe, produktive Session-/Rolloutdetails und noch offene Offline-Grenzen sind in `docs/OPEN_POINTS.md` gefuehrt.
