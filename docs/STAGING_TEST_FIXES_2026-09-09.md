# STAGING-Korrekturen – 09.09.2026

## Zweck

Dieses Dokument beschreibt die Korrekturen aus den Android-STAGING-Tests vom 09.09.2026. Die bestehenden Sicherheitsregeln bleiben unverändert: die SQL-Zielauswahl ist nur eine rein lesende Bedienhilfe; die wirksame Materialbuchung und die verbindliche Zielprüfung laufen weiterhin über die bestätigte Oxaion-HTTP-/Fachlogik. Bei unklarem Buchungsausgang gibt es keinen Blind-Retry.

## 1. Tankauslagerung – vollständige PCL-Lagerplatz-Auswahl

### Erste Korrektur war nicht ausreichend

Im ersten Android-Test endete die Ziel-Lagerplatzliste bei einem großen Lagerort ungefähr bei `LL324`. Zunächst wurden die technischen SQL-Begrenzungen `TOP (50)` beziehungsweise `TOP (100)` entfernt. Der erneute Test zeigte jedoch weiterhin dasselbe Ende, obwohl für `H04KDX` weitere Lagerplätze bis mindestens `LL350` vorhanden sind.

Damit ist technisch nachgewiesen: Die Begrenzung war **nicht** die eigentliche Ursache für die fehlenden Lagerplätze.

### Korrigierte Ursache

Die AJAX-Bedienhilfe las Ziel-Lagerplätze bisher aus `OXAION.LLPLAP`. Diese Datei ist für die gewünschte vollständige PCL-Zielauswahl nicht die richtige Quelle. Aus der vorhandenen Oxaion-Logik ist ersichtlich, dass `LLPLAP` im Rahmen von Lagerbuchungen fortgeschrieben beziehungsweise bei Bedarf angelegt wird. Ein leerer beziehungsweise noch nicht artikelbezogen verwendeter physischer PCL-Lagerplatz muss dort daher nicht zwingend als Auswahlzeile vorhanden sein.

Die für die wirksame Zielprüfung bereits bestätigte Oxaion-Liste ist dagegen `LB13210R`. Oxaion bezeichnet `LB13210` ausdrücklich als **„Matchcode für PCL-Lagerplätze“**. Die dazu passende PCL-Lagerplatzdatei ist `LPCLAP`; die bestätigten Schlüssel sind `PCFIRM`, `PCLAGO` und `PCLAPL`.

Deshalb verwendet `TargetLocationLookupService` für die rein lesende Ziel-Lagerplatzsuche jetzt:

- `OXAION.LPCLAP`
- Firma: `PCFIRM`
- Lagerort: `PCLAGO`
- interner Lagerplatz: `PCLAPL`

Die vorherige Abfrage über `LLPLAP` wurde entfernt. Es gibt weiterhin keine `TOP`-Begrenzung, keinen `RP.*`-Filter und keinen Bestandsfilter. Die Suche bleibt case-insensitive.

Wichtig: Auch ein SQL-Treffer aus `LPCLAP` ist weiterhin nur eine Bedienhilfe. Direkt vor der wirksamen `LF/LE`-Buchung validiert das Backend den ausgewählten internen Lagerplatz weiterhin über den bestätigten Weg:

`LB20115J *F4 -> LB13210R`

und liest die F4-Liste seitenweise bis zum eindeutigen `<STOP/>`. Damit bleibt Oxaion die verbindliche Buchungsprüfung.

## 2. Nachfüllen – zweite Stabilisierung gegen leere Prozessseite

Die erste Korrektur hielt den bewusst gewählten Prozess zusätzlich als `retainedMode`, damit ein kurzzeitig fehlender `.processChoice.active`-Marker die Nachfüllkarten nicht mehr entfernt.

Der erneute Android-Test zeigte, dass die leere Seite trotzdem noch auftreten konnte. Eine zweite Ursache lag in `process-shell.js`: Bei jedem kurzzeitig nicht passenden clientseitigen Auth-Zustand setzte die Shell sofort `page='home'`. Diese Prüfung lief sowohl bei DOM-Änderungen als auch periodisch. Dadurch konnte ein sehr kurzer Auth-/UI-Refresh den Prozessseitenzustand verwerfen, bevor der `retainedMode` ihn wiederherstellen konnte.

Korrektur:

- Ein kurzzeitig fehlender clientseitiger Auth-Abgleich setzt die Prozess-Shell **nicht mehr sofort** auf die Vorgangsübersicht.
- Der gewählte Vorgang und seine Karten bleiben während einer fünfsekündigen Grace-Phase sichtbar.
- Während dieser Phase zeigt die Schrittzeile `Anmeldung wird geprüft. Der aktuelle Vorgang bleibt erhalten.`; auth-abhängige Aktionen bleiben gesperrt.
- Ist die Authentifizierung nach fünf Sekunden weiterhin nicht bestätigt, kehrt die App sicher zur Anmelde-/Vorgangsübersicht zurück.
- Ein tatsächlich bestätigter Wechsel auf einen anderen Mitarbeiter verwirft den laufenden UI-Prozess weiterhin.
- Der `retainedMode` bleibt zusätzlich bestehen; die beiden Schutzmechanismen ergänzen sich.

Damit soll ein asynchroner Tankbestands-, Farb-, Quellen- oder Session-Refresh niemals mehr zu einer vollständig leeren `Nachfüllen`-Ansicht führen.

## 3. Langsamer PWA-Start / nur Logo sichtbar

Der bisherige Service Worker verwendete für Navigationsrequests `network first`. Bei schlechter oder fehlender Verbindung konnte die installierte Android-PWA deshalb auf den Netzwerk-/TCP-Timeout warten, bevor die bereits lokal vorhandene App-Shell angezeigt wurde. Während dieser Zeit war nur der native PWA-Startbildschirm beziehungsweise das Logo sichtbar.

Das Verhalten wurde geändert:

- Navigationsrequests starten jetzt sofort aus der lokal gecachten App-Shell, wenn diese vorhanden ist.
- Parallel wird die aktuelle HTML-Version im Hintergrund vom Server geladen und der Cache aktualisiert.
- `/api/...`-Requests werden weiterhin **nicht** vom Service Worker gecacht.
- Ein lokaler App-Start sagt daher nichts über die ERP-Erreichbarkeit aus und gibt keine Buchung frei.

Damit kann die Oberfläche auch bei einer gestörten Verbindung unmittelbar erscheinen, statt bis zum Browser-Netzwerktimeout nur das Logo zu zeigen.

## 4. Sichtbarer Verbindungsstatus

Der Produktionsmodus zeigt nun unabhängig von `Dev-Infos` einen echten Connectivity-Status:

- **gelb**: Verbindung wird gerade geprüft;
- **grün**: Backend und Oxaion wurden beim letzten Health-Check erfolgreich erreicht;
- **rot**: Backend oder Oxaion sind derzeit nicht erreichbar.

In der Kopfzeile bleibt dafür eine kompakte Statuslampe sichtbar. Beim Start wird zusätzlich ein Klartextstatus angezeigt, zum Beispiel:

- `Verbindungsaufbau: Backend wird geprüft …`
- `Backend erreichbar. Verbindung zu Oxaion wird geprüft …`
- `Backend erreichbar, aber aktuell keine Verbindung zu Oxaion.`
- `Keine Verbindung zum Backend. Die App ist lokal verfügbar, Oxaion kann derzeit nicht geprüft werden.`

Nach erfolgreicher Prüfung verschwindet der Klartextstatus nach kurzer Zeit; die grüne Lampe bleibt. Bei einem Fehler bleibt die rote Meldung sichtbar. Die App prüft erneut beim Vordergrundwechsel, beim Browser-`online`-Ereignis und periodisch. Als technische Prüfungen werden die bestehenden Endpunkte `/api/health` und `/api/health/oxaion` verwendet.

Die Lampe ist ausschließlich eine Erreichbarkeitsanzeige. Sie ersetzt keine aktuelle Pre-Write-Revalidierung und keinen Transaktionsstatus.

## 5. Tests und CI

Die automatisierten Tests prüfen jetzt zusätzlich:

- Ziel-Lagerplatzsuche verwendet `OXAION.LPCLAP` mit `PCFIRM/PCLAGO/PCLAPL`;
- `OXAION.LLPLAP` darf nicht wieder als vollständige PCL-Zielliste verwendet werden;
- keine `TOP`-Begrenzung;
- weiterhin rein lesende SQL-Abfrage ohne Bestands-/RP-Filter;
- JavaScript-Syntaxprüfung umfasst nun auch `connectivity-status.js` und den aktuellen Prozess-Hotfix;
- Service Worker, Prozess-Shell und Prozess-Router werden weiterhin von der JavaScript-Syntaxprüfung erfasst.

## 6. Live-STAGING-Prüfpunkte

- `H04KDX` öffnen und prüfen, dass die Ziel-Lagerplatzliste nun auch die bisher fehlenden PCL-Lagerplätze nach `LL324`, insbesondere bis `LL350`, enthält.
- Einen dieser späteren Lagerplätze auswählen und die anschließende `LB13210R`-Pre-Write-Prüfung bis zur erfolgreichen Tankauslagerung bestätigen.
- `Nachfüllen` mehrfach starten, Tank scannen und auch während langsamer Oxaion-Leseabfragen prüfen, dass die Prozesskarten sichtbar bleiben.
- App einmal mit normaler Verbindung und einmal mit nicht erreichbarem Backend/Oxaion starten: die App-Shell muss schnell sichtbar werden und der Verbindungsstatus muss den Zustand verständlich anzeigen.
