# STAGING-Korrekturen – 09.09.2026

## Zweck

Dieses Dokument beschreibt die Korrekturen aus den Android-STAGING-Tests vom 09.09.2026. Die bestehenden Sicherheitsregeln bleiben unverändert: die SQL-Zielauswahl ist nur eine rein lesende Bedienhilfe; die wirksame Materialbuchung und die verbindliche Zielprüfung laufen weiterhin über die bestätigte Oxaion-HTTP-/Fachlogik. Bei unklarem Buchungsausgang gibt es keinen Blind-Retry.

## 1. Tankauslagerung – vollständige PCL-Lagerplatz-Auswahl

### Verlauf der Korrekturen

Im ersten Android-Test endete die Ziel-Lagerplatzliste bei einem großen Lagerort ungefähr bei `LL324`. Zunächst wurden die technischen SQL-Begrenzungen `TOP (50)` beziehungsweise `TOP (100)` entfernt. Der erneute Test zeigte jedoch weiterhin dasselbe Ende, obwohl für `H04KDX` weitere Lagerplätze bis mindestens `LL350` vorhanden sind. Damit war technisch nachgewiesen, dass die ursprüngliche `TOP`-Begrenzung nicht allein die Ursache war.

Die AJAX-Bedienhilfe wurde anschließend von `OXAION.LLPLAP` auf die bekannte PCL-Datei `OXAION.LPCLAP` mit `PCFIRM`, `PCLAGO` und `PCLAPL` umgestellt. Die für die wirksame Zielprüfung bestätigte Oxaion-Liste bleibt `LB13210R` (`Matchcode für PCL-Lagerplätze`). Zusätzlich wurden Anzahl und letzter geladener Lagerplatz in der Oberfläche sichtbar gemacht und die serverseitige Präfixsuche beibehalten.

### Live-Bestätigung 09.09.2026

Im aktuellen Android-STAGING-Stand wurde `H04KDX` erneut getestet. Der Bediener bestätigt, dass **nun alle Lagerplätze angezeigt werden**. Damit ist der zuvor offene Realtest „Einträge nach `LL324` sichtbar und auswählbar“ für diesen Stand erfolgreich bestätigt.

Aktueller sicherer Stand:

- `TargetLocationLookupService` verwendet `OXAION.LPCLAP`.
- Es gibt keine `TOP`-Begrenzung, keinen `RP.*`-Filter und keinen Bestandsfilter.
- Die Suche bleibt case-insensitive.
- Die Oberfläche kann die vollständige Liste sowie eine serverseitige Präfixsuche verwenden.
- Freier Text wird weiterhin nicht als kanonischer Buchungsschlüssel akzeptiert.

Wichtig: Auch ein SQL-Treffer aus `LPCLAP` ist nur eine Bedienhilfe. Direkt vor der wirksamen `LF/LE`-Buchung validiert das Backend den ausgewählten internen Lagerplatz weiterhin über den bestätigten Weg:

`LB20115J *F4 -> LB13210R`

und liest die F4-Liste seitenweise bis zum eindeutigen `<STOP/>`. Oxaion bleibt damit die verbindliche Buchungsprüfung.

## 2. Nachfüllen – deterministische leere Prozessseite nach Tankscan

### Bisherige Schutzmechanismen

Die erste Korrektur hielt den bewusst gewählten Prozess als `retainedMode`, damit ein kurzzeitig fehlender `.processChoice.active`-Marker die Nachfüllkarten nicht mehr entfernt.

Die zweite Korrektur ergänzte in `process-shell.js` eine fünfsekündige Auth-Grace-Phase. Ein kurzzeitig fehlender clientseitiger Auth-Abgleich setzt die Prozess-Shell dadurch nicht mehr sofort auf die Vorgangsübersicht; der aktuelle Vorgang bleibt sichtbar, während auth-abhängige Aktionen gesperrt bleiben.

Die dritte Korrektur verhinderte, dass `process-mode-focus-fix.js` bei einem kurzfristigen Besitzer-Mismatch bereits alle Prozesskarten löscht, während `process-shell.js` noch auf der Prozessseite steht.

Diese Schutzmechanismen bleiben bestehen.

### Reproduzierbarer Restfehler und nachgewiesene Ursache

Der aktuelle Live-Test vom 09.09.2026 grenzt den Restfehler eindeutig ein: Nach dem Scan des Maschinentanks wird die Seite bei **`Pulver nachfüllen` immer leer**; die anderen Prozesse sind nicht betroffen.

Die Codeanalyse zeigt eine deterministische Ursache: `process-mode.js` wurde im bisherigen App-Shell **zweimal geladen**:

1. einmal regulär und beabsichtigt über `index.html`;
2. ein zweites Mal über einen alten dynamischen Bootstrap am Ende von `article-colors.js`.

Jede Ausführung von `process-mode.js` besitzt ihren eigenen lokalen `mode`-Zustand. Nur die Instanz, deren Prozesswahl-Handler tatsächlich verwendet wurde, kennt den ausgewählten Modus `replenish`. Die zweite Instanz bleibt bei `mode === null`.

Der Nachfüllprozess verwendet im Gegensatz zu den neueren Prozessarten weiterhin die vorhandenen Legacy-Karten `machineStep`, `sourcesSection`, `mixSection`, `bookingStep` und `result`. Nach einem Tankscan führen die asynchronen Bestands- und Quellenabfragen mehrere `refreshWorkerFlow()`-Aufrufe aus. Dabei konnte die zweite `process-mode.js`-Instanz mit `mode === null` `hideLegacy(true)` ausführen und die Nachfüllkarten ausblenden. Gleichzeitig blieb `process-shell.js` auf `processShellProcess`, wodurch Login und Vorgangsauswahl ebenfalls verborgen waren. Das Ergebnis war die vollständig leere Prozessseite.

Damit ist auch erklärt, warum der Fehler nur bei `Nachfüllen` auftrat: Die anderen Prozessarten verwenden eigene `.processPanel`-Bereiche und hängen nicht von den ausgeblendeten Legacy-Nachfüllkarten ab.

### Korrektur

- Der veraltete dynamische Bootstrap von `process-mode.js` wurde vollständig aus `article-colors.js` entfernt.
- `index.html` ist damit die einzige autoritative Einbindestelle von `process-mode.js`.
- `article-colors.js` erhält einen neuen Cache-Key im App-Shell.
- Der Service-Worker-Cache wurde auf `fam-pulver-staging-v29-replenish-single-router-20260909` angehoben, damit die korrigierte Datei auf Android in den neuen App-Shell übernommen wird.
- Ein neuer Regressionstest `FrontendBootstrapTests.ProcessModeRouterIsLoadedExactlyOnce` prüft statisch, dass `index.html` genau eine `process-mode.js`-Einbindung enthält und `article-colors.js` keinen zweiten Bootstrap mehr enthält.

Diese Korrektur betrifft ausschließlich Frontend-Bootstrap und Sichtbarkeitssteuerung. Backend-Session, Oxaion-Prüfungen, Transaktions-IDs, Idempotenz, Pre-Write-Revalidierung und die Regel „kein Blind-Retry“ bleiben unverändert.

Der neue Stand muss noch einmal auf dem Android-Gerät live bestätigt werden. Wegen des kontrollierten Service-Worker-Lebenszyklus ohne `skipWaiting` soll die installierte PWA nach dem Serverupdate vollständig geschlossen und neu gestartet werden, damit der neue App-Shell aktiv werden kann.

## 3. Langsamer PWA-Start / nur Logo sichtbar

Der frühere Service Worker verwendete für Navigationsrequests `network first`. Bei schlechter oder fehlender Verbindung konnte die installierte Android-PWA deshalb auf den Netzwerk-/TCP-Timeout warten, bevor die bereits lokal vorhandene App-Shell angezeigt wurde. Während dieser Zeit war nur der native PWA-Startbildschirm beziehungsweise das Logo sichtbar.

Der aktuelle Service Worker verwendet für Navigation die lokal gecachte App-Shell sofort und aktualisiert die HTML-Version parallel im Hintergrund. `/api/...`-Requests werden weiterhin nicht vom Service Worker gecacht.

Im Follow-up wurde zusätzlich eine Aktualisierungslücke geschlossen: Die Service-Worker-Registrierung in `app.js` lief erst nach mehreren Startinitialisierungen. `connectivity-status.js` registriert beziehungsweise prüft den Worker jetzt bereits beim frühen Laden des Dokuments mit `updateViaCache: 'none'`, ohne auf Backend-/Oxaion-Initialisierung zu warten.

Bewusst **nicht** verwendet wird `skipWaiting`: Eine neu geladene Frontend-Version darf einen bereits laufenden Buchungsvorgang nicht unkontrolliert übernehmen. Ein bereits installierter alter Worker kann daher für seine Ablösung weiterhin einen sauberen Seiten-/App-Lebenszyklus benötigen.

Wichtig für die Interpretation: Solange Android noch ausschließlich den nativen PWA-Splashscreen zeigt, ist noch kein HTML der App sichtbar; dort kann JavaScript keinen dynamischen Verbindungstext einblenden. Ziel ist deshalb, diese Phase durch den cache-first App-Shell-Start möglichst kurz zu halten. Sobald das HTML gerendert ist, zeigt die App ihren eigenen Start-/Verbindungsstatus.

## 4. Sichtbarer Verbindungsstatus

Der Produktionsmodus zeigt unabhängig von `Dev-Infos` einen echten Connectivity-Status:

- **gelb**: Verbindung wird gerade geprüft;
- **grün**: Backend und Oxaion wurden beim letzten Check erreicht;
- **rot**: Backend oder Oxaion sind derzeit nicht erreichbar.

In der Kopfzeile bleibt eine kompakte Statusanzeige sichtbar. Beim Start wird zusätzlich ein Klartextstatus angezeigt, zum Beispiel:

- `Verbindungsaufbau: Backend wird geprüft …`
- `Backend erreichbar. Verbindung zu Oxaion wird geprüft …`
- `Backend erreichbar, aber aktuell keine Verbindung zu Oxaion.`
- `Keine Verbindung zum Backend. Die App ist lokal verfügbar, Oxaion kann derzeit nicht geprüft werden.`

Nach erfolgreicher Prüfung verschwindet der Klartextstatus nach kurzer Zeit; die grüne Anzeige bleibt. Bei einem Fehler bleibt die rote Meldung sichtbar. Die App prüft erneut beim Vordergrundwechsel, beim Browser-`online`-Ereignis und periodisch.

Für die regelmäßige Oxaion-Ampel wird jetzt der leichte Endpunkt `GET /api/connectivity/oxaion` verwendet. Er öffnet und schließt nur eine Oxaion-App-Tunnel-Session. Der deutlich schwerere `/api/health/oxaion`-Dialog-Smoke-Test bleibt für den manuellen Dev-Verbindungstest bestehen und wird nicht mehr periodisch für die Bediener-Ampel ausgeführt.

Die Ampel ist ausschließlich eine Erreichbarkeitsanzeige. Sie ersetzt keine aktuelle Pre-Write-Revalidierung und keinen Transaktionsstatus.

## 5. Tests und CI

Die automatisierten Tests beziehungsweise CI-Prüfungen decken weiterhin ab:

- Ziel-Lagerplatzsuche verwendet aktuell `OXAION.LPCLAP` mit `PCFIRM/PCLAGO/PCLAPL`;
- keine `TOP`-Begrenzung;
- weiterhin rein lesende SQL-Abfrage ohne Bestands-/RP-Filter;
- `process-mode.js` darf im Frontend-Bootstrap nur einmal geladen werden;
- JavaScript-Syntaxprüfung umfasst `connectivity-status.js`, `process-mode-focus-fix.js`, `target-location.js`, Service Worker und die übrigen Worker-/Prozessskripte;
- .NET Build und Unit-Tests laufen auf jedem Push des Branches `feature/separate-processes`.

Die Vollständigkeit der realen `H04KDX`-Lagerplätze kann nicht durch einen statischen Unit-Test bewiesen werden; sie wurde deshalb im Android-STAGING-Test bestätigt.

## 6. Nächste Live-STAGING-Prüfpunkte

- Nach Bereitstellung dieses Standes die installierte PWA vollständig schließen und neu starten, damit der neue Service Worker/App-Shell aktiv werden kann.
- `Nachfüllen` starten, Tank scannen und prüfen, dass anschließend Tankbestand, passende Lagerorte und der Button zum Scannen der Nachfüllcharge sichtbar bleiben.
- `Nachfüllen` danach mehrfach hintereinander wiederholen, um zu bestätigen, dass die Oberfläche auch während der asynchronen Oxaion-Leseabfragen nicht mehr leer wird.
- Die bereits bestätigte vollständige H04KDX-Lagerplatzanzeige muss für diesen Fix nicht erneut untersucht werden; die verbindliche F4-Pre-Write-Prüfung bleibt unabhängig davon bestehen.
