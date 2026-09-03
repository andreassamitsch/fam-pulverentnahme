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

### Maschinenwahl und Pruefung des Maschinenbestands

Im Online-Fall wird vor dem Nachfuellen der aktuelle Oxaion-Bestand der tatsaechlichen Maschine ueber das Backend geprueft.

Fuer den aktuellen STAGING-Nachfuellprozess gilt verbindlich:

- Die Maschine beziehungsweise der Tank-Lagerort wird aus einer gepflegten Maschinenliste ausgewaehlt; aktuell sind `EOS1` und `EOS2` konfiguriert.
- Der spaetere Maschinen-QR-Code ersetzt die manuelle Auswahl, muss aber gegen dieselbe Maschinenliste validiert werden.
- Der Pulverartikel wird beim Nachfuellen nicht manuell eingegeben. Er wird aus der einzigen positiven Bestandsposition des ausgewaehlten Maschinen-Lagerorts abgeleitet.
- Artikelnummer und Artikelbezeichnung sind Systeminformationen und nicht editierbar.
- Auf einer Maschine wird fuer diesen Prozess genau ein positiver Pulverbestand erwartet. Mehrere positive Bestandspositionen sind nicht eindeutig und sperren die Buchung.
- Die aktuelle Mix-Charge und die komplette Tankmenge werden ebenfalls aus Oxaion uebernommen und nicht manuell eingegeben.

Bis der spaetere FA-/Leerbefuellungsablauf umgesetzt ist, kann ein komplett leerer Tank in diesem Nachfuellprozess keinen Artikel liefern und wird deshalb nicht automatisch freigegeben.

### Auswahl der Nachfuellquellen

Lagerort, Lagerplatz und Charge einer neuen Nachfuellmenge werden nicht frei als Buchungsschluessel eingegeben. Sie werden aus dem aktuellen positiven Oxaion-Bestand zum aus der Maschine abgeleiteten Pulverartikel ausgewaehlt.

Verbindlich gilt:

- Das Backend ermittelt die Lagerorte mit positivem Artikelbestand aus der bestaetigten Oxaion-Auskunft `Chargen und Lagerorte pro Artikel`.
- Der aktuell ausgewaehlte Maschinen-Tanklagerort darf nicht als Nachfuellquelle angeboten oder vom Backend akzeptiert werden.
- Nach Auswahl eines Lagerortes ermittelt das Backend die dort vorhandenen positiven Lagerplatz-/Chargenpositionen aus `Lagerplaetze pro Artikel und -ort`.
- Fuer die Buchung wird immer der von Oxaion gelieferte interne Lagerplatzschluessel verwendet. Eine visuell formatierte Lagerplatzdarstellung darf nicht vom Bediener nachgebildet und als `PSLAPL` uebergeben werden.
- Der Referenzfall `H04HRL / RE1F3 / Charge 84671` bestaetigt, dass `RE1F3` der intern gueltige Buchungsschluessel ist.
- Meldet Oxaion eindeutig `LAG1626` (`Lagerort hat keine Lagerplatzorganisation`), bleibt der Lagerplatz leer; die Charge und der Bestand werden ueber den bestaetigten Ablauf `Chargen pro Lagerort` ermittelt. Es wird kein Lagerplatz erfunden.
- Die Einfuellmenge bleibt eine Bedienereingabe, darf jedoch den aktuell verfuegbaren Oxaion-Bestand der gewaehlten Bestandsposition nicht ueberschreiten.
- Mehrere Nachfuellchargen duerfen in einem Vorgang verwendet werden. Dieselbe exakte Oxaion-Bestandsposition darf innerhalb eines Vorgangs nicht doppelt ausgewaehlt werden.
- Direkt vor der ersten schreibenden Oxaion-Materialbuchung validiert das Backend jede Nachfuellquelle erneut anhand von Artikel, Lagerort, internem Lagerplatzschluessel, Charge und verfuegbarer Menge. Bei Abweichung wird keine Materialbuchung gestartet.

Technische Details stehen in `docs/OXAION_SOURCE_STOCK_LOOKUP.md`.

### Mitarbeiter

Die Personalnummer ist die einzige Personaleingabe. Fuehrende Nullen werden in der Bedienoberflaeche nicht verwendet. Die WebApp sucht die Nummer in Oxaion, zeigt Treffer ausschliesslich als `PEPENU - PEPENA` und verlangt eine bewusste Auswahl. Der vollstaendige Name kommt aus `PEPENA`; eine freie Namenseingabe gibt es nicht.

`PESAKZ` wird nicht verwendet, da das Feld nicht fuer jeden Mitarbeiter gepflegt ist. `PENLAE` wird fuer den vollstaendigen Mitarbeiternamen in diesem Ablauf ebenfalls nicht verwendet. Der bestaetigte Ablauf und die Feldzuordnung sind in `docs/OXAION_PERSONNEL_LOOKUP.md` dokumentiert. Vor dem ersten schreibenden Materialbuchungsaufruf prueft das Backend `PEPENU` und `PEPENA` erneut in Oxaion.

### Neue Mix-Charge und Buchungsdaten

Fuer neue Mix-Chargen ist das Nummernschema verbindlich festgelegt:

```text
<Artikel ohne Punkt>MIX_<yyyyMMdd>_<HHmmss>
```

Beispiel fuer `RP.00010`:

```text
RP00010MIX_20260902_162312
```

Die Nummer wird automatisch aus dem abgeleiteten Artikel und dem aktuellen Erstellungszeitpunkt erzeugt. Sie ist nicht frei editierbar; der Bediener kann lediglich bewusst eine neue Nummer mit neuem Zeitstempel erzeugen.

- `Mix Charge erstellt am` zeigt den Erzeugungszeitpunkt; fuer das bisherige Oxaion-Produktionsdatum wird das aktuelle Datum verwendet.
- Das Buchungsdatum ist beim neuen Nachfuellvorgang immer das aktuelle Datum und wird nicht angezeigt beziehungsweise nicht manuell eingegeben.
- Ziel der neuen Mix-Charge ist immer die ausgewaehlte Maschine; ein zweites Ziel-Lagerortfeld gibt es im Nachfuellprozess nicht.
- Der Buchungstext ist nicht frei editierbar und lautet dynamisch `Pulver nachfuellen <Maschinen-Lagerort>`, zum Beispiel `Pulver nachfuellen EOS2`.

Weitere UI-Regeln stehen in `docs/REPLENISHMENT_INPUT_RULES.md`.

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
2. neue Mix-Charge nach dem verbindlichen Mix-Chargenschema erzeugen
3. Pulver der Maschine zuordnen beziehungsweise buchen

## Mix-Chargen

Eine Mix-Charge repraesentiert das Pulver, das aus einem oder mehreren Rohmaterialvorgaengen fuer die Produktion bereitgestellt wird.

Die Mix-Charge ist nicht zwingend an eine Maschine gebunden, da dasselbe Pulver prinzipiell auf unterschiedlichen Maschinen eingesetzt werden kann. Die Maschinenzuordnung wird deshalb separat gefuehrt und ist nicht Bestandteil der Mix-Chargennummer.

Verbindliches Nummernschema:

```text
<Artikel ohne Punkt>MIX_<yyyyMMdd>_<HHmmss>
```

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

Im Normalfall gibt es so wenig manuelle Eingaben wie moeglich. Daten werden bevorzugt aus QR-Codes, Oxaion und dem vorhandenen Maschinenbestand ermittelt. Manuelle Eingaben sind nur vorgesehen, wo sie fachlich wirklich notwendig sind. Nicht editierbare Systeminformationen werden optisch klar von wichtigen Eingabefeldern getrennt. Abhaengige Dropdowns werden erst eingeblendet, wenn die jeweils erforderliche Elternauswahl getroffen wurde.

Offline-, Sync- und Buchungsstatus muessen fuer den Bediener klar und eindeutig sichtbar sein. `Lokal gespeichert` darf niemals wie `erfolgreich gebucht` aussehen.

## Offene Punkte

Die aktuell offenen Punkte werden zentral in `docs/OPEN_POINTS.md` gepflegt. Insbesondere bleiben offen:

- konkrete Oxaion HTTP-Aufrufe fuer die noch fehlenden Materialbuchungen
- Buchungsschluessel fuer Maschinenlager -> Pulverlager und noch nicht abgedeckte Gegenrichtungen
- genaue Oxaion-Abfrage der Stammdatensperre und technische Ermittlung des sperrenden Benutzers
- finale Maschinenliste und QR-Zuordnung; STAGING nutzt derzeit `EOS1` und `EOS2`
- genaue Benutzer-Authentifizierung der WebApp
- endgueltiger produktiver Server
- maximale Offline-Gueligkeitsdauer und konkrete offline zulaessige Prozessschritte
- IndexedDB-Schema, Migrationsstrategie und Frontend-/API-Kompatibilitaet bei PWA-Updates
