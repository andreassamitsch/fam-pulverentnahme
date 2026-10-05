# Übergeordnete Anmeldung, Vorgangsauswahl und separate Prozess-UI

Stand: 01.10.2026

Diese Datei dokumentiert die verbindlichen UI-Entscheidungen fuer die aktuelle mobile STAGING-PWA. Die fachlichen Buchungs-, Idempotenz-, Recovery- und Oxaion-Pruefregeln aus den spezialisierten Prozessdokumenten bleiben unveraendert.

## Übergeordnete Startseite

Anmeldung und Vorgangsauswahl sind eine übergeordnete Ebene zu den einzelnen Arbeitsvorgaengen.

Verbindlicher Ablauf:

1. App oeffnen.
2. Mitarbeiter per NFC beziehungsweise Fallback anmelden.
3. Nach erfolgreicher Anmeldung einen Vorgang auswaehlen.
4. Erst danach wird die Bedienoberflaeche des gewaehlten Vorgangs angezeigt.
5. Innerhalb des einzelnen Vorgangs werden Anmeldeformular und gesamte Vorgangsauswahl nicht erneut als normale Arbeitsschritte angezeigt.

Die Auswahl eines Vorgangs bleibt bei normalen UI-/Status-Refreshes stabil. Ein Refresh darf den Mitarbeiter nicht auf die Vorgangsauswahl zurueckwerfen. Der bewusste Wechsel erfolgt ueber Android-/Browser-Zurueck beziehungsweise nach eindeutig abgeschlossenem Erfolg.

Ein laufender oder unklarer serverseitiger Buchungsvorgang darf durch die UI-Navigation nicht stillschweigend verworfen oder als neu buchbar dargestellt werden. Die Recovery-/Kein-Blind-Retry-Regeln bleiben vorrangig.

## Angemeldeter Mitarbeiter in der Kopfzeile

Nach erfolgreicher Anmeldung wird in der App-Kopfzeile der **volle Name** des aktuell angemeldeten Mitarbeiters angezeigt.

- Die Anzeige stammt aus der bereits bestaetigten Personal-Session/Personalauswahl.
- Die Kopfzeile ist nur Anzeige und keine neue Authentifizierungsquelle.
- Bei Abmeldung oder abgelaufener Session wird der Name entfernt und die App kehrt zur übergeordneten Startseite zurueck.

## Verhalten nach erfolgreicher Buchung

Eine erfolgreiche Buchung wird weiterhin eindeutig und bestaetigungspflichtig angezeigt.

Erst nachdem der Mitarbeiter die Erfolgsmeldung mit `Verstanden` bestaetigt hat:

- werden sichtbare Eingaben, Scans, Prozesszusammenfassungen und Ergebnisanzeige des abgeschlossenen Vorgangs geleert;
- bleibt die Mitarbeiter-Session bestehen;
- kehrt die App auf die übergeordnete Vorgangsauswahl zurueck und positioniert diese am Seitenanfang.

Bei `UNCERTAIN`, `MANUAL_REVIEW_REQUIRED`, `REJECTED` oder anderen nicht eindeutig erfolgreichen Ergebnissen darf dieser automatische Erfolgs-Reset nicht die erforderliche Recovery-Information beseitigen. Diese Faelle folgen `docs/ERROR_HANDLING.md`.

## Tankauslagerung: Ziel-Lagerort und Lagerplatz

Beim Vorgang `Pulver aus Tank auslagern` werden Ziel-Lagerort und Ziel-Lagerplatz nicht mehr primaer als freie Texteingabe verwendet. Der Mitarbeiter sucht und waehlt die realen Oxaion-Schluessel ueber eine AJAX-Trefferliste.

### Rein lesende SQL-Suchhilfe

Die Suchhilfe verwendet die separate serverseitige Laufzeitverbindung `OxaionSql__ConnectionString` und bleibt rein lesend.

Lagerorte werden aus dem bestaetigten Lagerortstamm `OXAION.ULGSTP` fuer die konfigurierte Firma ermittelt. Fuer den aktuellen Auslagerungsvorgang werden lagerplatzgefuehrte Ziel-Lagerorte angeboten.

Lagerplaetze werden fuer den ausgewaehlten Lagerort aus `OXAION.LLPLAP.LPLAPL` gelesen.

Fuer diese Ziel-Lagerplatzsuche gilt ausdruecklich:

- **kein Filter auf `RP.*`-Artikel**;
- **kein Filter auf positiven oder sonstigen Bestand**;
- nur reale interne Lagerplatzschluessel des ausgewaehlten Lagerorts werden angeboten;
- die SQL-Abfrage ist nur Auswahl-/Suchhilfe und keine Buchungsfreigabe.

Damit kann auch ein leerer, bereits vorhandener interner Lagerplatz als Ziel ausgewaehlt werden, soweit er in der bestaetigten SQL-Datenquelle vorhanden ist.

### Verbindliche Pre-Write-Pruefung bleibt Oxaion

Die AJAX-Auswahl ersetzt die bestaetigte Oxaion-Pruefung nicht.

Unmittelbar vor der schreibenden Auslagerungsbuchung werden Ziel-Lagerort und interner Lagerplatz weiterhin ueber die bereits bestaetigten Oxaion-F4-Auskunftswege validiert. Ein frei eingetippter, aber nicht aus der Trefferliste uebernommener Wert ist keine ausreichende Buchungsgrundlage.

Keine Materialbuchung erfolgt per SQL.

## Neues Pulver in Tank: falscher Artikel

Im Vorgang `Neues Pulver in Tank füllen` wird mit dem ersten gueltigen Chargenscan der Pulverartikel fuer diesen Neubefuellungsvorgang festgelegt.

Wird bei einer weiteren Charge ein anderer Artikel gescannt:

- die Charge wird nicht uebernommen;
- es erscheint dieselbe seitendeckende, blockierende Fehlscan-Darstellung wie beim bestehenden Vorgang `Pulver nachfüllen`;
- Soll-Artikel und gescannter Ist-Artikel/Charge werden deutlich gegenuebergestellt;
- soweit verfuegbar werden die EFA01/EFA02-Erkennungsfarben als visuelle Hilfe dargestellt;
- die Meldung muss bewusst mit `Verstanden` bestaetigt werden;
- der Fehlscan wird ueber den bestehenden WebApp-Auditweg protokolliert;
- es wird dadurch keine Oxaion-Materialbuchung gestartet.

## Pulver auf Fertigungsauftrag buchen

Die Mitarbeiterbezeichnung fuer die Verbrauchseingabe lautet:

`Pulver Verbrauch eingeben`

Der eingegebene Wert ist der **Ist-Verbrauch gesamt**, nicht ein zusaetzlicher Verbrauch. Die detaillierte fachliche und technische Regel steht in `docs/FA_CONSUMPTION_PROCESS.md`.
