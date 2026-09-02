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
Android Webbrowser / PWA
  -> HTML/JavaScript Frontend
  -> ASP.NET Core Backend
  -> Oxaion HTTP-Schnittstelle
  -> Oxaion Fachlogik
```

Fuer Materialbuchungen soll nach Moeglichkeit die vorhandene Oxaion BDE-/PPS-Logik ueber die HTTP-Schnittstelle verwendet werden.

## PWA und Offline-Faehigkeit

Die mobile WebApp wird als Progressive Web App (PWA) geplant, damit sie auf Android-Smartphones wie eine installierte App genutzt und bei kurzen Netzwerkausfaellen kontrolliert weiterbedient werden kann.

Verbindliche Grundregeln:

- Service Worker fuer App-Shell und statische Ressourcen.
- `IndexedDB` fuer lokale fachliche Zwischenspeicherung und Outbox; kein `localStorage` fuer diese Daten.
- Browser-/Geraetespeicher ist nur ein Zwischenpuffer. Backend und Oxaion bleiben fuer produktive Buchungen und den aktuellen fachlichen Zustand fuehrend.
- Offline erfasste Vorgaenge duerfen niemals als erfolgreich gebucht dargestellt werden.
- Jeder offline angelegte Vorgang erhaelt eine eindeutige `clientOperationId`; das Backend ordnet diese eindeutig einer serverseitigen Transaktion zu beziehungsweise nutzt sie als Idempotency Key.
- Nach Wiederherstellung der Verbindung muss der aktuelle fachliche Zustand serverseitig erneut validiert werden, bevor eine produktive Oxaion-Buchung ausgeloest wird.
- Bei geaendertem oder nicht eindeutigem Zustand wird nicht automatisch ueberschrieben oder blind gebucht; der Vorgang geht in Konflikt beziehungsweise manuelle Klaerung.
- Eine neue PWA-Version darf nicht unkontrolliert mitten in einem laufenden Vorgang oder unter Verlust noch nicht synchronisierter Daten aktiviert werden.

Details stehen verbindlich in `docs/OFFLINE_PWA.md`.

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

Im Online-Fall wird vor dem Nachfuellen der aktuelle Oxaion-Bestand der tatsaechlichen Maschine ueber das Backend geprueft.

- **Maschine leer:** Nachfuellen ist zulaessig.
- **Maschine enthaelt passendes Pulver:** Nachfuellen ist zulaessig.
- **Maschine enthaelt anderes Pulver oder eine nicht kompatible Mix-Charge:** Nachfuellen ist nicht zulaessig.

Im letzten Fall erscheint die klare Meldung `Pulverwechsel erforderlich` und die Moeglichkeit, direkt in den Prozess **Pulver tauschen** zu wechseln.

Die WebApp darf niemals unterschiedliches oder nicht kompatibles Pulver zusammenmischen.

### Auswahl der Nachfuellquellen

Lagerort, Lagerplatz und Charge einer neuen Nachfuellmenge werden nicht mehr frei als Buchungsschluessel eingegeben. Sie werden aus dem aktuellen positiven Oxaion-Bestand zum Pulverartikel ausgewaehlt.

Verbindlich gilt:

- Das Backend ermittelt die Lagerorte mit positivem Artikelbestand aus der bestaetigten Oxaion-Auskunft `Chargen und Lagerorte pro Artikel`.
- Nach Auswahl eines Lagerortes ermittelt das Backend die dort vorhandenen positiven Lagerplatz-/Chargenpositionen aus `Lagerplaetze pro Artikel und -ort`.
- Fuer die Buchung wird immer der von Oxaion gelieferte interne Lagerplatzschluessel verwendet. Eine visuell formatierte Lagerplatzdarstellung darf nicht vom Bediener nachgebildet und als `PSLAPL` uebergeben werden.
- Der Referenzfall `H04HRL / RE1F3 / Charge 84671` bestaetigt, dass `RE1F3` der intern gueltige Buchungsschluessel ist.
- Meldet Oxaion eindeutig `LAG1626` (`Lagerort hat keine Lagerplatzorganisation`), bleibt der Lagerplatz leer; die Charge und der Bestand werden ueber den bestaetigten Ablauf `Chargen pro Lagerort` ermittelt. Es wird kein Lagerplatz erfunden.
- Die Einfuellmenge bleibt eine Bedienereingabe, darf jedoch den aktuell verfuegbaren Oxaion-Bestand der gewaehlten Bestandsposition nicht ueberschreiten.
- Mehrere Nachfuellchargen duerfen in einem Vorgang verwendet werden. Dieselbe exakte Oxaion-Bestandsposition darf innerhalb eines Vorgangs nicht doppelt ausgewaehlt werden.
- Direkt vor der ersten schreibenden Oxaion-Materialbuchung validiert das Backend jede Nachfuellquelle erneut anhand von Artikel, Lagerort, internem Lagerplatzschluessel, Charge und verfuegbarer Menge. Bei Abweichung wird keine Materialbuchung gestartet.

Technische Details und die bestaetigten Oxaion-Programme/Felder stehen in `docs/OXAION_SOURCE_STOCK_LOOKUP.md`.

Fuer Offline-Betrieb darf ein zuvor bestaetigter lokaler Maschinenzustand nur nach den Regeln aus `docs/OFFLINE_PWA.md` verwendet werden. Die konkrete maximale Gueligkeitsdauer und der genaue Umfang offline freigegebener Prozessschritte sind noch festzulegen. Nach Reconnect erfolgt vor jeder produktiven Buchung erneut eine serverseitige Validierung.

## 2. Pulver tauschen

Beim Pulverwechsel ist die Maschine der Ausgangspunkt. Ein Maschinenscan ist daher immer verpflichtend.

Der aktuelle Pulverbestand wird nicht manuell eingegeben. Im Online-Fall fragt das Backend nach dem Maschinenscan ueber die freigegebene Oxaion-Logik den aktuellen Bestand der Maschine ab.

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

Welche Teilschritte eines Pulverwechsels offline lediglich vorbereitet und bis `PENDING_SYNC` zwischengespeichert werden duerfen, ist noch offen. Eine produktive Oxaion-Buchung wird offline nicht simuliert oder als erfolgreich angenommen.

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

- wurde nur lokal erfasst?
- wartet der Vorgang auf Synchronisation?
- wurde er an das Backend uebertragen?
- wurde noch nichts an Oxaion gesendet?
- wurde die Anfrage an Oxaion gesendet?
- wurde erfolgreich gebucht?
- wurde von Oxaion fachlich abgelehnt?
- ist der Ausgang wegen eines Verbindungsabbruchs unbekannt?
- ist ein Datensatz gesperrt?
- ist ein Konflikt nach Offline-Erfassung entstanden?
- ist ein manueller Eingriff notwendig?

Jeder lokal angelegte Vorgang erhaelt eine eindeutige `clientOperationId`. Jeder serverseitig angenommene produktive Buchungsvorgang erhaelt zusaetzlich eine eindeutige Transaktions-ID. Beide werden eindeutig korreliert.

Beispielhafte serverseitige fachliche Status:

- `CREATED`
- `VALIDATING`
- `SENDING_TO_OXAION`
- `SUCCESS`
- `REJECTED`
- `LOCKED`
- `UNCERTAIN`
- `MANUAL_REVIEW_REQUIRED`

Lokale Sync-Zustaende wie `PENDING_SYNC`, `SYNCING`, `SYNCED` oder `CONFLICT` sind davon getrennt zu fuehren.

Die endgueltigen technischen Statusnamen koennen bei der Implementierung sinnvoll angepasst werden. Die fachliche Unterscheidung muss erhalten bleiben.

### Verbindungsabbruch

Es sind zwei Ebenen zu unterscheiden:

1. **Smartphone <-> Backend:** Noch nicht bestaetigte Vorgaenge koennen nach den Regeln aus `docs/OFFLINE_PWA.md` lokal in der Outbox bleiben und spaeter mit derselben `clientOperationId` idempotent synchronisiert werden.
2. **Backend <-> Oxaion:** Bricht die Verbindung ab, nachdem die Anfrage moeglicherweise bereits bei Oxaion angekommen ist, darf nicht einfach erneut gebucht werden. Andernfalls besteht Doppelbuchungsgefahr.

Das Backend muss deshalb:

- jede Anfrage protokollieren
- eine eigene Transaktions-ID fuehren
- die `clientOperationId` eindeutig zuordnen und gegen Mehrfachverarbeitung schuetzen
- nach Moeglichkeit das Oxaion-Ergebnis pruefen
- bei unklarem Zustand `UNCERTAIN` verwenden
- keine unkontrollierten automatischen Wiederholungen von Oxaion-Buchungen ausfuehren

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

- `clientOperationId`, wenn der Vorgang im Frontend angelegt wurde
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
- lokaler Sync-Status und Synchronisationsversuche, soweit relevant
- verwendeter Maschinen-Cache mit Abfragezeitpunkt/Version, falls eine Offline-Entscheidung darauf beruhte
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

Offline-, Sync- und Buchungsstatus muessen fuer den Bediener klar und eindeutig sichtbar sein. `Lokal gespeichert` darf niemals wie `erfolgreich gebucht` aussehen.

## Offene Punkte

Die folgenden Punkte sind noch nicht final geklaert und duerfen nicht erfunden werden:

- TODO: konkrete Oxaion HTTP-Aufrufe fuer alle Materialbuchungen
- TODO: konkretes Oxaion BDE-/PPS-Programm beziehungsweise Programme
- TODO: Buchungsschluessel fuer Maschinenlager -> Pulverlager
- TODO: Buchungsschluessel Pulverlager -> Maschine
- TODO: eventuell benoetigte Chargenumbuchungen
- TODO: genaue Oxaion-Abfrage der Stammdatensperre
- TODO: technische Ermittlung des sperrenden Benutzers
- TODO: finales Mix-Chargenschema
- TODO: endgueltige Ziel-/Maschinen-Lagerort- und Lagerplatzlogik ausserhalb der bestaetigten Nachfuellquellen-Auswahl
- TODO: genaue Benutzer-Authentifizierung der WebApp
- TODO: endgueltiger produktiver Server fuer die WebApp
- TODO: maximale Offline-Gueligkeitsdauer eines Maschinenzustands
- TODO: konkrete offline zulaessige Prozessschritte je Buchungsszenario
- TODO: IndexedDB-Schema und migrationssichere Versionsstrategie
- TODO: Frontend-/API-Kompatibilitaet bei PWA-Updates
