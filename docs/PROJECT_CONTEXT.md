# FAM Pulverentnahme / Pulverwechsel

## Ziel

Entwicklung einer mobilen WebApp fuer die Produktion zur sicheren, nachvollziehbaren und moeglichst einfachen Durchfuehrung von Pulverentnahmen, Pulvernachfuellungen und Pulverwechseln.

Die WebApp soll mit Oxaion kommunizieren. Das Frontend bleibt bewusst einfach. Komplexe Pruef-, Buchungs- und Fehlerlogik liegt im Backend beziehungsweise in der vorhandenen Oxaion-Fachlogik.

## Systemarchitektur

### Vorhandene Umgebung

- Die Oxaion-Applikation laeuft auf einem eigenen Server.
- Die Oxaion-Datenbank laeuft auf einem separaten Datenbankserver.
- Auf dem Datenbankserver befinden sich aktuell auch SSRS Reporting Services und IIS.
- Fuer Tests kann die WebApp auf dem vorhandenen IIS betrieben werden.

### Bevorzugter Produktivbetrieb

- eigener Web-/Application-Server beziehungsweise eigene VM
- IIS
- ASP.NET Core Backend

Die WebApp schreibt nicht direkt auf die Oxaion-Datenbank.

Grundsaetzlicher Kommunikationsweg:

```text
Android Webbrowser
  -> HTML/JavaScript Frontend
  -> ASP.NET Core Backend
  -> Oxaion HTTP-Schnittstelle
  -> Oxaion Fachlogik
```

Fuer Materialbuchungen soll nach Moeglichkeit die vorhandene Oxaion BDE-/PPS-Logik ueber die HTTP-Schnittstelle verwendet werden.

## Hauptfunktionen fuer den Bediener

Der Bediener soll moeglichst wenige, klar verstaendliche Hauptfunktionen sehen. Aktuell festgelegt sind:

1. Pulver nachfuellen
2. Pulver tauschen

## 1. Pulver nachfuellen / Pulver fuer Fertigungsauftrag vorbereiten

Ausgangsbasis ist der QR-Code des Fertigungsauftrags. Der vorhandene QR-Code enthaelt sinngemaess:

```text
Rohmaterialnummer
+++
Fertigungsauftragsnummer
+++
Maschinen-ID
```

Die Maschinen-ID aus dem Fertigungsauftrag ist die geplante beziehungsweise vorgeschlagene Maschine. Da sich die Maschine kurzfristig in der Produktion aendern kann, zeigt die WebApp nach dem Scan die vorgesehene Maschine und bietet zwei Wege:

- vorgeschlagene Maschine verwenden
- andere Maschine verwenden

Bei Auswahl einer anderen Maschine gilt:

- Ein Maschinenscan ist verpflichtend.
- Die tatsaechlich verwendete Maschine wird protokolliert.
- Die urspruengliche Maschine aus Fertigungsauftrag beziehungsweise QR-Code bleibt ebenfalls im Protokoll erhalten.

Damit werden getrennt gefuehrt:

- Soll-/Planmaschine
- tatsaechlich verwendete Maschine

Die WebApp darf organisatorisch nicht entscheiden, ob eine andere Maschine grundsaetzlich erlaubt ist. Sie prueft jedoch technisch, welches Pulver sich aktuell auf der gewaehlten Maschine befindet.

### Pruefung des Maschinenbestands

Vor dem Nachfuellen wird der aktuelle Oxaion-Bestand der tatsaechlichen Maschine geprueft.

- **Maschine leer:** Nachfuellen ist zulaessig.
- **Maschine enthaelt passendes Pulver:** Nachfuellen ist zulaessig.
- **Maschine enthaelt anderes Pulver oder eine nicht kompatible Mix-Charge:** Nachfuellen ist nicht zulaessig.

Im letzten Fall erscheint die klare Meldung `Pulverwechsel erforderlich` und die Moeglichkeit, direkt in den Prozess **Pulver tauschen** zu wechseln.

Die WebApp darf niemals unterschiedliches oder nicht kompatibles Pulver zusammenmischen.

## 2. Pulver tauschen

Beim Pulverwechsel ist die Maschine der Ausgangspunkt. Ein Maschinenscan ist daher immer verpflichtend.

Der aktuelle Pulverbestand wird nicht manuell eingegeben. Nach dem Maschinenscan fragt das Backend ueber die freigegebene Oxaion-Logik den aktuellen Bestand der Maschine ab.

Mindestens anzuzeigen sind:

- Maschine
- aktuell vorhandener Pulverartikel
- aktuelle Mix-Charge
- Systembestand beziehungsweise Restmenge

Der Bediener bestaetigt die Entnahme. Die Menge wird fuer die vollstaendige Maschinenentleerung aus dem Systembestand uebernommen; eine manuelle Mengeneingabe ist dafuer nicht vorgesehen.

Kann Oxaion keinen eindeutigen Bestand ermitteln, darf die WebApp nicht raten. Der Vorgang wird beispielsweise in folgenden Faellen gestoppt:

- kein Systembestand, obwohl physisch Pulver vorhanden ist
- mehrere unerwartete Bestaende
- mehrere nicht eindeutig zuordenbare Chargen

Die WebApp zeigt dann eine verstaendliche Fehlerbeschreibung und eine konkrete Massnahme an.

### Entnommenes Pulver

Beim Pulverwechsel wird das aktuell in der Maschine befindliche Pulver vollstaendig entnommen und gesiebt. Danach kann es wieder in das Pulverlager zurueckgestellt werden.

Dafuer ist ein entsprechender Oxaion Aus-/Wiedereinlagerungs- beziehungsweise Umbuchungsprozess erforderlich. Die konkreten Oxaion-Buchungsschluessel sind noch nicht abschliessend festgelegt. Sie duerfen nicht erfunden oder hart codiert werden.

### Neue Befuellung

Nach der Entnahme des bisherigen Pulvers:

1. neues Rohmaterial beziehungsweise Rohmaterialcharge scannen
2. neue Mix-Charge erzeugen
3. Pulver der Maschine zuordnen beziehungsweise buchen

Das derzeit bevorzugte, aber fachlich noch zu bestaetigende Mix-Chargenschema lautet sinngemaess:

```text
MIX-YYMMDD-XX
```

Beispiel: `MIX-260827-01`

## Mix-Chargen

Eine Mix-Charge repraesentiert das Pulver, das aus einem oder mehreren Rohmaterialvorgaengen fuer die Produktion bereitgestellt wird.

Die Mix-Charge ist nicht zwingend an eine Maschine gebunden, da dasselbe Pulver prinzipiell auf unterschiedlichen Maschinen eingesetzt werden kann. Die Maschinenzuordnung wird deshalb separat gefuehrt und soll nicht Bestandteil der Mix-Chargennummer sein.

Das endgueltige Nummernschema ist als fachliche Entscheidung noch zu bestaetigen.

## Fehler- und Transaktionshandling

Fehlerhandling ist ein zentraler Bestandteil des Projekts. Bei jedem Buchungsvorgang muss eindeutig nachvollziehbar sein:

- wurde noch nichts gesendet?
- wurde die Anfrage an Oxaion gesendet?
- wurde erfolgreich gebucht?
- wurde von Oxaion fachlich abgelehnt?
- ist der Ausgang wegen eines Verbindungsabbruchs unbekannt?
- ist ein Datensatz gesperrt?
- ist ein manueller Eingriff notwendig?

Jeder Buchungsvorgang erhaelt eine eindeutige Transaktions-ID.

Beispielhafte fachliche Status:

- `CREATED`
- `VALIDATING`
- `SENDING_TO_OXAION`
- `SUCCESS`
- `REJECTED`
- `LOCKED`
- `UNCERTAIN`
- `MANUAL_REVIEW_REQUIRED`

Die endgueltigen technischen Statusnamen koennen bei der Implementierung sinnvoll angepasst werden. Die fachliche Unterscheidung muss erhalten bleiben.

### Verbindungsabbruch

Bricht die HTTP-Verbindung ab, nachdem die Anfrage moeglicherweise bereits bei Oxaion angekommen ist, darf nicht einfach erneut gebucht werden. Andernfalls besteht Doppelbuchungsgefahr.

Das Backend muss deshalb:

- jede Anfrage protokollieren
- eine eigene Vorgangs-ID fuehren
- nach Moeglichkeit das Oxaion-Ergebnis pruefen
- bei unklarem Zustand `UNCERTAIN` verwenden
- keine unkontrollierten automatischen Wiederholungen ausfuehren

## Oxaion-Sperren

Bei Materialbuchungen auf Fertigungsauftraege kann der entsprechende Oxaion-Datensatz gesperrt sein, etwa weil ein anderer Benutzer den Fertigungsauftrag oder einen relevanten Stammsatz bearbeitet. Dieser Fall wird als eigener fachlicher Zustand behandelt.

Beispielmeldung:

```text
Fertigungsauftrag gesperrt.

Die Materialbuchung wurde nicht durchgefuehrt.

Der Fertigungsauftrag ist derzeit in Oxaion gesperrt.
```

Wenn ueber die Oxaion-Sperrlogik zuverlaessig ermittelbar, wird der sperrende Benutzer zusaetzlich angezeigt, zum Beispiel `Gesperrt durch: MAXM` oder `Gesperrt durch: Max Mustermann`.

`Zuletzt geaendert von` darf nicht als sperrender Benutzer interpretiert werden. Es muss die tatsaechliche Oxaion-Sperrinformation verwendet werden. Ist der Sperrer nicht zuverlaessig ermittelbar, wird `Gesperrt durch: nicht ermittelbar` angezeigt.

Massnahme fuer den Bediener:

```text
Bitte kurz warten und erneut versuchen. Wenn die Sperre bestehen bleibt, Produktionsleitung informieren.
```

Bei einer Sperre gilt:

- Die Buchung wurde nicht durchgefuehrt.
- Es gibt keine automatische Endlosschleife.
- Ein erneuter Versuch erfolgt bewusst und manuell.
- Die Transaktions-ID wird protokolliert.

## Protokollierung

Fuer die spaetere Nachvollziehbarkeit werden mindestens gespeichert:

- Transaktions-ID
- Zeitstempel
- Benutzer beziehungsweise Bediener
- Buchungsart
- Fertigungsauftrag
- Rohmaterial
- Rohmaterialcharge
- Mix-Charge
- vorgesehene Maschine
- tatsaechlich verwendete Maschine
- Menge
- Quelllager
- Ziellager
- Oxaion Request/Referenz, soweit fachlich und datenschutzrechtlich sinnvoll
- Oxaion Response/Status
- Fehlerstatus
- Sperrinformation
- sperrender Benutzer, sofern zuverlaessig ermittelbar
- Wiederholungsversuche
- finaler Zustand

Datenschutz und Security sind zu beachten. Passwoerter, Auth-Tokens und sonstige Secrets duerfen weder protokolliert noch in Git gespeichert werden.

## Bedienkonzept

Die Oberflaeche soll fuer Produktionsmitarbeiter moeglichst einfach sein. Die beiden grossen Hauptaktionen sind:

- Pulver nachfuellen
- Pulver tauschen

Im Normalfall gibt es so wenig manuelle Eingaben wie moeglich. Daten werden bevorzugt aus QR-Codes, Oxaion und dem vorhandenen Maschinenbestand ermittelt. Manuelle Eingaben sind nur vorgesehen, wo sie fachlich wirklich notwendig sind.

## Offene Punkte

Die folgenden Punkte sind noch nicht final geklaert und duerfen nicht erfunden werden:

- TODO: konkrete Oxaion HTTP-Aufrufe fuer alle Materialbuchungen
- TODO: konkretes Oxaion BDE-/PPS-Programm beziehungsweise Programme
- TODO: Buchungsschluessel fuer Maschinenlager -> Pulverlager
- TODO: Buchungsschluessel fuer Pulverlager -> Maschine
- TODO: eventuell benoetigte Chargenumbuchungen
- TODO: genaue Oxaion-Abfrage des aktuellen Maschinenbestands
- TODO: genaue Oxaion-Abfrage der Stammdatensperre
- TODO: technische Ermittlung des sperrenden Benutzers
- TODO: finales Mix-Chargenschema
- TODO: endgueltige Lagerort-/Lagerplatzlogik
- TODO: genaue Benutzer-Authentifizierung der WebApp
- TODO: endgueltiger produktiver Server fuer die WebApp
