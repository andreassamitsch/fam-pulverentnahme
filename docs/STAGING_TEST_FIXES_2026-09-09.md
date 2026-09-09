# STAGING-Korrekturen – 09.09.2026

## Zweck

Dieses Dokument beschreibt die Korrekturen aus den Android-STAGING-Tests vom 09.09.2026. Die bestehenden Sicherheitsregeln bleiben unverändert: die SQL-Zielauswahl ist nur eine rein lesende Bedienhilfe; die wirksame Materialbuchung und die verbindliche Zielprüfung laufen weiterhin über die bestätigte Oxaion-HTTP-/Fachlogik. Bei unklarem Buchungsausgang gibt es keinen Blind-Retry.

## 1. Vollständige Lagerort-/Lagerplatz-Auswahl bei Tankauslagerung

Im Android-Test wurde bei einem Lagerort mit vielen Lagerplätzen festgestellt, dass die AJAX-Auswahlliste nur einen Teil der vorhandenen internen Lagerplätze zeigte. Ursache war eine technische Begrenzung der rein lesenden SQL-Auswahl:

- Lagerorte: `TOP (50)`
- Lagerplätze: `TOP (100)`

Diese Begrenzungen sind entfernt. Bei leerem Suchtext liefern die Endpunkte nun die vollständige Trefferliste für die konfigurierte Firma beziehungsweise den ausgewählten Lagerort:

- `GET /api/target-locations/warehouses`
- `GET /api/target-locations/storage-bins`

Die Suche bleibt explizit case-insensitive über `UPPER(...)` und verwendet weiterhin nur `SELECT` auf `OXAION.ULGSTP` beziehungsweise `OXAION.LLPLAP`. Für die Zielauswahl gelten bewusst kein `RP.*`-Filter und kein Bestandsfilter, weil alle gepflegten internen Ziel-Lagerplätze auswählbar sein sollen.

Die SQL-Treffer sind weiterhin keine Buchungsfreigabe. Direkt vor der wirksamen `LF/LE`-Buchung werden die ausgewählten Schlüssel weiterhin über die bestätigten Oxaion-F4-Wege `US16601R` und `LB13210R` geprüft. Für umfangreiche Oxaion-F4-Listen wird die bereits eingeführte vollständige Listenlesung bis `<STOP/>` verwendet.

## 2. Nachfüllen – leere Oberfläche nach Tankscan

Im Android-Test trat sporadisch ein Zustand auf, in dem die Kopfzeile weiterhin `Nachfüllen` anzeigte, die eigentlichen Nachfüllkarten aber ausgeblendet waren und die Schrittinformation wieder `Vorgang auswählen.` zeigte.

Ursache war ein Frontend-Refresh-Rennen zwischen Prozess-Shell und Worker-Refresh: Der stabile Router leitete den ausgewählten Vorgang ausschließlich aus der CSS-Klasse `.processChoice.active` ab. Wurde diese Klasse während eines asynchronen UI-/Session-Refreshs kurzzeitig nicht gefunden, interpretierte der Router dies als `kein Vorgang ausgewählt` und blendete die Legacy-Nachfüllkarten aus, obwohl die Prozess-Shell weiterhin auf der Vorgangsseite stand.

Korrektur:

- der zuletzt bewusst ausgewählte Vorgang wird im laufenden Frontend als `retainedMode` gehalten;
- solange `processShellProcess` aktiv ist, führt ein kurzzeitig fehlender `.active`-Marker nicht mehr zum Verwerfen des Vorgangs;
- der aktive Marker wird in diesem Zustand wiederhergestellt;
- beim tatsächlichen Wechsel auf die Vorgangsübersicht wird `retainedMode` verworfen;
- ein echter Mitarbeiterwechsel beziehungsweise Auth-Konflikt verwirft den Vorgang weiterhin aus Sicherheitsgründen;
- ein kurzzeitiger Session-/UI-Refresh darf dagegen den laufenden Vorgang nur vorübergehend verbergen, aber nicht löschen.

Damit bleibt `Nachfüllen` auch während der asynchronen Tankbestands-, Artikel-, Farb- und Quellenabfragen sichtbar und der Worker-Flow kann nach Abschluss der jeweiligen Abfrage den nächsten Schritt anzeigen.

## 3. PWA-Cache

Die Service-Worker-Cache-Version wurde angehoben, damit die korrigierte Prozess-Routing-Logik und die vollständige Zielauswahl auf den Android-Geräten nach einem App-Neustart sicher neu geladen werden.

## 4. Tests

Die Backend-Tests für die rein lesende Zielauswahl prüfen jetzt zusätzlich:

- keine `TOP`-Begrenzung der Lagerortliste;
- keine `TOP`-Begrenzung der Lagerplatzliste;
- explizit case-insensitive SQL-Suche;
- weiterhin keine `RP.*`-/Bestandsfilter in der Ziel-Lagerplatzsuche;
- weiterhin ausschließlich lesende SQL-Anweisungen.

Die Frontend-Korrektur wird zusätzlich durch die CI-JavaScript-Syntaxprüfung abgesichert. Der sporadische Android-Refreshfall bleibt als Live-STAGING-Regressionspunkt zu prüfen.

## 5. Live-STAGING-Prüfpunkte

- Einen Lagerort mit deutlich mehr als 100 Lagerplätzen öffnen und prüfen, dass auch Einträge nach dem bisherigen Ende (z. B. nach `LL324`) auswählbar sind.
- Dieselbe Lagerplatzsuche mit unterschiedlicher Groß-/Kleinschreibung testen.
- `Nachfüllen` mehrfach starten, Tank scannen und während/nach den Oxaion-Leseabfragen prüfen, dass die Nachfüllkarten sichtbar bleiben und nicht auf `Vorgang auswählen.` zurückfallen.
