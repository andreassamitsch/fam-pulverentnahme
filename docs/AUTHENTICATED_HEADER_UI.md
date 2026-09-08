# Angemeldeter Mitarbeiter in der App-Kopfzeile

Stand: 08.09.2026

Diese Datei dokumentiert die verbindliche Bedienentscheidung fuer die Darstellung einer bereits angemeldeten Person.

## Ziel

Nach erfolgreicher Anmeldung soll die Vorgangsuebersicht nicht weiterhin den kompletten Login-Bereich mit NFC-, Personalnummer- und Passwortfeldern zeigen. Die angemeldete Person bleibt stattdessen dauerhaft kompakt in der Kopfzeile sichtbar.

## Verbindliches Verhalten

- Solange keine gueltige Personal-Session besteht, wird der normale Anmeldebereich vollstaendig angezeigt.
- Nach erfolgreicher Anmeldung wird der Anmeldebereich auf der Vorgangsuebersicht ausgeblendet.
- In der sticky App-Kopfzeile wird ein Benutzer-Symbol zusammen mit dem vollstaendigen Namen der aktuell angemeldeten Person angezeigt.
- Der Name in der Kopfzeile ist bedienbar. Antippen beziehungsweise Aktivieren oeffnet ein kompaktes Popup mit:
  - Benutzer-Symbol,
  - vollstaendigem Namen,
  - Aktion `Abmelden`.
- Auf einer bereits geoeffneten einzelnen Vorgangsseite bleibt die Mitarbeiteranzeige in der Kopfzeile sichtbar. Die Abmeldung ist dort jedoch gesperrt. Der Bediener muss zuerst ueber `Vorgaenge` zur uebergeordneten Vorgangsuebersicht zurueckkehren.
- Ein bereits serverseitig offener beziehungsweise unklarer Buchungsvorgang darf durch die Abmeldung nicht umgangen werden. Besteht ein solcher Vorgang, bleibt die Abmeldung gesperrt und der Buchungsstatus ist zuerst zu klaeren.
- Nach erfolgreicher serverseitiger Abmeldung wird die Seite neu geladen. Dadurch bleiben keine nur im DOM vorhandenen, noch nicht gebuchten Eingaben der vorherigen Person fuer eine nachfolgende Anmeldung erhalten.
- Nach der Abmeldung wird wieder der vollstaendige Anmeldebereich angezeigt.

## Sicherheitsgrenze

Diese Aenderung ist ausschliesslich eine UI-/Navigationsentscheidung. Sie veraendert keine der bestehenden Sicherheitsregeln:

- die ASP.NET-Core-Personal-Session bleibt fuer Buchungen erforderlich;
- vor schreibenden Oxaion-Aufrufen bleibt die erneute serverseitige Personalpruefung aktiv;
- Idempotenz, Recovery und Doppelbuchungsschutz bleiben unveraendert;
- ein offener oder unklarer Vorgang darf nicht durch Benutzerwechsel oder Abmeldung stillschweigend verworfenfen werden.

## Technische Umsetzung

Die bestehende Authentifizierungslogik bleibt in `personnel-auth.js`. Die zusaetzliche Kopfzeilen-/Popup-Darstellung liegt in `wwwroot/header-user-menu.js` und wird durch die bestehende Prozess-Shell geladen.

Die neue Frontend-Datei wird vom Service Worker als App-Shell-Ressource gecacht und in der CI-JavaScript-Syntaxpruefung mitgeprueft.
