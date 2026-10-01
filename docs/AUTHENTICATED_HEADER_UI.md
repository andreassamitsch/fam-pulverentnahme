# Angemeldeter Mitarbeiter und Navigation in der App-Kopfzeile

Stand: 01.10.2026

Diese Datei dokumentiert die verbindliche Bedienentscheidung fuer die kompakte Kopfzeile und die Darstellung einer bereits angemeldeten Person.

## Ziel

Nach erfolgreicher Anmeldung soll die Vorgangsuebersicht nicht weiterhin den kompletten Login-Bereich mit NFC-, Personalnummer- und Passwortfeldern zeigen. Die angemeldete Person bleibt stattdessen dauerhaft kompakt in der Kopfzeile sichtbar. Die Kopfzeile soll auf dem Android-Smartphone bewusst niedrig und einzeilig bleiben.

## Verbindliches Verhalten

- Solange keine gueltige Personal-Session besteht, wird der normale Anmeldebereich vollstaendig angezeigt.
- Nach erfolgreicher Anmeldung wird der Anmeldebereich auf der Vorgangsuebersicht ausgeblendet.
- In der sticky App-Kopfzeile wird ein Benutzer-Symbol zusammen mit dem vollstaendigen Namen der aktuell angemeldeten Person angezeigt.
- Der Name in der Kopfzeile ist bedienbar. Antippen beziehungsweise Aktivieren oeffnet ein kompaktes Popup mit Benutzer-Symbol, vollstaendigem Namen und Aktion `Abmelden`.
- Auf der Vorgangsuebersicht lautet der App-Titel nur `Pulververwaltung`.
- Auf einer geoeffneten einzelnen Vorgangsseite wird statt `Pulververwaltung` eine kurze, einzeilige Vorgangsbezeichnung angezeigt, z. B. `Nachfuellen`, `Auslagern`, `Tank befuellen`, `Fertigungsauftrag` oder `Lagerbestand`.
- Titel, Verbindungsstatus und Benutzeranzeige bleiben in einer Zeile; die Textgroesse darf fuer kleine Displays moderat reduziert werden, muss aber lesbar bleiben.
- Der bisherige sichtbare Kopfzeilenbutton `Vorgaenge` entfaellt.
- Von einer einzelnen Vorgangsseite wechselt die Android-/Browser-Zurueck-Funktion zur Vorgangsuebersicht. Ein noch nicht gebuchter UI-Zustand dieser Seite wird dabei verworfen.
- Waehrend eine Buchung aktiv verarbeitet wird oder ein blockierender Scan-/Buchungsdialog offen ist, darf Zurueck nicht stillschweigend einen laufenden beziehungsweise unklaren Buchungsvorgang verlassen.
- Auf einer bereits geoeffneten einzelnen Vorgangsseite bleibt die Mitarbeiteranzeige in der Kopfzeile sichtbar. Die Abmeldung ist dort gesperrt. Der Bediener wechselt zuerst mit Zurueck zur Vorgangsuebersicht.
- Ein bereits serverseitig offener beziehungsweise unklarer Buchungsvorgang darf durch die Abmeldung nicht umgangen werden. Besteht ein solcher Vorgang, bleibt die Abmeldung gesperrt und der Buchungsstatus ist zuerst zu klaeren.
- Nach erfolgreicher serverseitiger Abmeldung wird die Seite neu geladen. Dadurch bleiben keine nur im DOM vorhandenen, noch nicht gebuchten Eingaben der vorherigen Person fuer eine nachfolgende Anmeldung erhalten.
- Nach der Abmeldung wird wieder der vollstaendige Anmeldebereich angezeigt.

## Diagnose und Dev-Infos

Im normalen Produktionsbetrieb werden `Diagnose` und `Dev-Infos` in der Kopfzeile nicht angezeigt und koennen clientseitig nicht freigeschaltet werden.

Die Freigabe erfolgt ausschliesslich serverseitig ueber die ASP.NET-Core-Konfiguration `Prototype:DeveloperToolsEnabled`. Beim mitgelieferten STAGING-Start erfolgt die bewusste Freigabe per `START_STAGING.bat -DeveloperTools` beziehungsweise `./start-staging-published.ps1 -DeveloperTools`; ohne diesen Parameter setzt das Skript den Wert explizit auf `false`. Bei anderem IIS-/Diensthosting kann entsprechend die Umgebungsvariable `Prototype__DeveloperToolsEnabled=true` gesetzt werden; nach einer Aenderung ist der Anwendungsprozess/AppPool neu zu starten.

Der Browser erhaelt nur den booleschen Freigabestatus ueber `/api/ui-config`. Die Freigabe ist eine Bedien-/Diagnosefunktion und veraendert keine fachliche Buchungsberechtigung.

## Sticky aktuelle Schrittinformation

Die aktuelle Schrittinformation (`workerNextInstruction`) bleibt direkt unter der sticky Kopfzeile sichtbar. Ihr Sticky-Abstand wird aus der tatsaechlichen aktuellen Kopfzeilenhoehe ermittelt und nicht mehr mit einem starren Pixelwert angenommen. Dadurch darf die Kopfzeile Meldungen wie `Neue Befuellung pruefen und buchen` beim Scrollen nicht ueberdecken.

## Sicherheitsgrenze

Diese Aenderung ist ausschliesslich eine UI-/Navigationsentscheidung. Sie veraendert keine der bestehenden Sicherheitsregeln:

- die ASP.NET-Core-Personal-Session bleibt fuer Buchungen erforderlich;
- vor schreibenden Oxaion-Aufrufen bleibt die erneute serverseitige Personalpruefung aktiv;
- Idempotenz, Recovery und Doppelbuchungsschutz bleiben unveraendert;
- ein offener oder unklarer Vorgang darf nicht durch Benutzerwechsel oder Abmeldung stillschweigend verworfen werden;
- Android-Zurueck darf waehrend einer bereits gestarteten Buchungsverarbeitung keinen Blind-Retry oder Abbruch mit falscher Erfolgs-/Fehlerannahme erzeugen.

## Technische Umsetzung

Die bestehende Authentifizierungslogik bleibt in `personnel-auth.js`. Die Mitarbeiter-Popup-Darstellung liegt in `wwwroot/header-user-menu.js`. Die uebergeordnete Prozess-Shell liegt in `wwwroot/process-shell.js`; die Android-History-/Header-/Sticky-Optimierungen liegen in `wwwroot/process-ux-optimizations.js`.

Die Frontend-Dateien werden vom Service Worker als App-Shell-Ressourcen gecacht und in der CI-JavaScript-Syntaxpruefung mitgeprueft.
