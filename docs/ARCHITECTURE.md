# Technische Architektur

## Zielbild

```text
[Android Smartphone]
        |
        | HTTPS
        v
[HTML / JavaScript WebApp]
        |
        | REST / JSON
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
- keine Oxaion-Zugangsdaten, Buchungsschluessel oder vertrauenswuerdige Buchungslogik im Frontend

### ASP.NET Core Backend

- zentrale Vermittlungs- und Kontrollschicht
- Eingabevalidierung und fachliche Ablaufsteuerung
- Vergabe und Persistierung eindeutiger Transaktions- beziehungsweise Vorgangs-IDs
- Idempotenz, Duplicate Prevention und Statusverwaltung
- Aufruf ausschliesslich freigegebener Oxaion HTTP-Schnittstellen
- sichere technische Protokollierung ohne Secrets
- Uebersetzung technischer und fachlicher Oxaion-Ergebnisse in klare Bedienermeldungen

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
- technische und fachliche Status
- Fehlerprotokoll
- Audit Trail
- Zuordnung von Planmaschine und tatsaechlich verwendeter Maschine

Sie ist kein Ersatz fuer Oxaion als fachlich fuehrendes ERP-System.

## Deployment

- Testbetrieb: vorhandener IIS auf dem Datenbankserver ist moeglich.
- Bevorzugter Produktivbetrieb: eigener Web-/Application-Server beziehungsweise eigene VM mit IIS und ASP.NET Core Hosting Bundle.
- Kommunikation erfolgt verschluesselt per HTTPS.
- Secrets werden ueber eine noch festzulegende sichere Laufzeitkonfiguration bereitgestellt und niemals im Repository gespeichert.

## Integrationsgrenzen

- Keine Oxaion-Endpunkte, Programme, Parameter, Tabellenlogik oder Buchungsschluessel werden ohne Bestaetigung angenommen.
- Keine direkten ERP-Buchungen per SQL.
- Bei unklarem Buchungsergebnis bleibt der Vorgang offen beziehungsweise wird zur manuellen Pruefung markiert; er wird nicht blind wiederholt.
- Authentifizierung, konkrete Oxaion-Aufrufe und Datenmodelle sind in `docs/OPEN_POINTS.md` als offen gefuehrt.
