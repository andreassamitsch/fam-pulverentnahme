# Technische Architektur

## Zielbild

```text
[Android Smartphone]
        |
        | PWA: HTML / JavaScript / Service Worker
        | IndexedDB: lokaler Zustand + Outbox
        |
        | HTTPS / REST / JSON
        v
[ASP.NET Core Backend]
        |
        | Oxaion HTTP
        v
[Oxaion Application Server]
        |
        v
[Oxaion DB]

Optional, ausschliesslich fuer die WebApp:

[WebApp Transaction DB]
```

## Komponenten und Verantwortlichkeiten

### Android Smartphone und WebApp

- mobile Bedienoberflaeche fuer Produktionsmitarbeiter
- Scan von Fertigungsauftrags-, Maschinen- und Rohmaterialcodes ueber die Kamera
- Anzeige von Planmaschine, Ist-Maschine, Maschinenbestand, Prozessstatus und konkreten Fehlermassnahmen
- PWA mit Web App Manifest und Service Worker
- Offline-faehige App-Shell fuer kurze Netzunterbrechungen
- `IndexedDB` fuer lokale Vorgangsdaten, Maschinenzustands-Cache und Outbox
- Vergabe einer eindeutigen `clientOperationId` bereits beim lokalen Anlegen eines Vorgangs
- klare Trennung zwischen lokalem Sync-Status und serverseitigem Buchungsstatus
- kontrollierte Synchronisation nach Wiederherstellung der Backend-Verbindung
- kontrollierte PWA-Aktualisierung ohne Datenverlust und ohne erzwungenen Reload waehrend kritischer Vorgaenge
- keine Oxaion-Zugangsdaten, Buchungsschluessel oder vertrauenswuerdige Buchungslogik im Frontend

Der Browser-/Geraetespeicher ist nur ein Zwischenpuffer. Er ist nicht die fachlich fuehrende Datenhaltung.

### Service Worker

Der Service Worker verwaltet ausschliesslich die fuer die PWA geeignete Offline-Infrastruktur:

- Cache der statischen App-Shell
- Erkennung und kontrollierte Bereitstellung neuer Frontend-Versionen
- optional technische Unterstuetzung fuer spaetere Sync-Mechanismen

Schreibende API-Antworten und Oxaion-Buchungsergebnisse duerfen nicht aus einem Service-Worker-Cache als fachliche Wahrheit verwendet werden.

### IndexedDB und lokale Outbox

`IndexedDB` speichert mindestens:

- lokale Scans und noch nicht synchronisierte Bedienvorgaenge
- `clientOperationId`
- lokalen Sync-Status
- letzte eindeutig bestaetigte Maschinenzustaende mit Zeitstempel und, sofern vorhanden, Revision/ETag
- Synchronisationsversuche und serverseitige Referenzen, soweit sicher und erforderlich

Die Outbox muss einen Browser-Neustart und eine kurze Offline-Phase ueberstehen. Ein lokaler Eintrag darf erst dann als fachlich abgeschlossen gelten, wenn das Backend beziehungsweise Oxaion den dafuer erforderlichen Endstatus eindeutig bestaetigt hat.

### ASP.NET Core Backend

- zentrale Vermittlungs- und Kontrollschicht
- Eingabevalidierung und fachliche Ablaufsteuerung
- Vergabe und Persistierung eindeutiger serverseitiger Transaktions- beziehungsweise Vorgangs-IDs
- eindeutige Zuordnung der `clientOperationId` zu einer serverseitigen Transaktion
- serverseitige Idempotenz, Duplicate Prevention und Statusverwaltung
- erneute fachliche Validierung nach Reconnect, bevor eine produktive Oxaion-Buchung erfolgt
- Aufruf ausschliesslich freigegebener Oxaion HTTP-Schnittstellen
- sichere technische Protokollierung ohne Secrets
- Uebersetzung technischer und fachlicher Oxaion-Ergebnisse in klare Bedienermeldungen
- fuer den STAGING-Nachfuellprototyp: lesender Endpunkt `GET /api/machine-stock`, der die im JET-Datenstrom bestaetigte Auflistung `Chargen pro Lagerort` kapselt; Details in `docs/OXAION_MACHINE_STOCK_LOOKUP.md`
- der Maschinenbestand ist nicht von einem gespeicherten Oxaion-Filter abhaengig: das Backend liest die vollstaendige `LB30230R`-Liste des Lagerorts und wertet direkt die bestaetigte Bedingung `LLAWEP.LALABE != 0` aus
- vor einem neuen Mix-Buchungsversuch: erneute Bestandsabfrage und Vergleich von Lagerort, Artikel, Charge und kompletter Maschinenmenge mit dem vom Frontend vorbereiteten Request; bei Abweichung keine schreibende Materialbuchung starten

Dasselbe `clientOperationId` darf nicht zu mehreren wirksamen Oxaion-Buchungen fuehren. Wiederholtes Senden derselben Outbox-Nachricht muss serverseitig idempotent behandelt werden.

### Oxaion Application Server

- Ausfuehrung der freigegebenen Oxaion-Fachlogik
- bevorzugt Nutzung vorhandener BDE-/PPS-Prozesse
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

Bei genau einem positiven `KGM`-Bestand des erwarteten Artikels werden alte Mix-Charge und gesamte Restmenge im Frontend nur angezeigt und nicht manuell eingegeben. Kein Bestand ungleich 0 wird als leerer Maschinen-Lagerort erkannt. Ein positiver Bestand eines anderen Artikels erzwingt Pulverwechsel; mehrere Bestaende ungleich 0, negative Bestaende oder unerwartete Mengeneinheiten sperren den Nachfuellvorgang.

Das Backend liest denselben Zustand vor dem Start einer neuen schreibenden Materialbuchung nochmals und blockiert erkannte Abweichungen. Eine atomare Sperr-/Reservierungsstrategie zwischen letzter Bestandspruefung und erster schreibender Buchung ist weiterhin offen.

Offline darf ein zuvor serverseitig bestaetigter Maschinenzustand nur nach den Regeln aus `docs/OFFLINE_PWA.md` verwendet werden. Insbesondere benoetigt er einen Abfragezeitpunkt und muss innerhalb einer noch festzulegenden maximalen Gueligkeitsdauer liegen.

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
- Kommunikation erfolgt verschluesselt per HTTPS.
- Secrets werden ueber eine noch festzulegende sichere Laufzeitkonfiguration bereitgestellt und niemals im Repository gespeichert.
- Der Oxaion-Laufzeitbenutzer ist nicht fest im Anwendungscode konfiguriert; der STAGING-Starter fragt Benutzer und Passwort interaktiv ab.
- PWA-Assets muessen mit einer kontrollierten Cache- und Versionsstrategie ausgeliefert werden.

## Integrationsgrenzen

- Keine Oxaion-Endpunkte, Programme, Parameter, Tabellenlogik oder Buchungsschluessel werden ohne Bestaetigung angenommen.
- Keine direkten ERP-Buchungen per SQL.
- Bei unklarem Buchungsergebnis bleibt der Vorgang offen beziehungsweise wird zur manuellen Pruefung markiert; er wird nicht blind wiederholt.
- Offline erfasste Daten sind keine bestaetigten ERP-Buchungen.
- Der serverseitige Start des neuen `US30600J`-Bestandslesevorgangs mit leerer Eltern-`SSID` ist im STAGING noch live zu bestaetigen; solange diese rein lesende Abfrage nicht sicher funktioniert, darf keine darauf angewiesene Materialbuchung gestartet werden.
- Authentifizierung, weitere konkrete Oxaion-Aufrufe, Datenmodelle und noch offene Offline-Grenzen sind in `docs/OPEN_POINTS.md` als offen gefuehrt.
