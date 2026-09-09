# STAGING-Korrekturen – 09.09.2026

## Zweck

Dieses Dokument beschreibt die Korrekturen aus den Android-STAGING-Tests vom 09.09.2026. Die bestehenden Sicherheitsregeln bleiben unverändert: die SQL-Zielauswahl ist nur eine rein lesende Bedienhilfe; die wirksame Materialbuchung und die verbindliche Zielprüfung laufen weiterhin über die bestätigte Oxaion-HTTP-/Fachlogik. Bei unklarem Buchungsausgang gibt es keinen Blind-Retry.

## 1. Tankauslagerung – vollständige PCL-Lagerplatz-Auswahl

### Erste Korrektur war nicht ausreichend

Im ersten Android-Test endete die Ziel-Lagerplatzliste bei einem großen Lagerort ungefähr bei `LL324`. Zunächst wurden die technischen SQL-Begrenzungen `TOP (50)` beziehungsweise `TOP (100)` entfernt. Der erneute Test zeigte jedoch weiterhin dasselbe Ende, obwohl für `H04KDX` weitere Lagerplätze bis mindestens `LL350` vorhanden sind.

Damit ist technisch nachgewiesen: Die Begrenzung war **nicht** die eigentliche Ursache für die fehlenden Lagerplätze.

### Zweite Korrekturannahme `LPCLAP` ist ebenfalls noch nicht ausreichend bestätigt

Die AJAX-Bedienhilfe las Ziel-Lagerplätze zunächst aus `OXAION.LLPLAP`. Diese Datei ist für eine vollständige PCL-Zielauswahl fachlich nicht ideal, weil sie artikel-/bestandsbezogene Lagerplatzzeilen enthalten kann und leere PCL-Plätze nicht zwingend vollständig repräsentiert.

Die für die wirksame Zielprüfung bestätigte Oxaion-Liste ist `LB13210R`; Oxaion bezeichnet `LB13210` als **„Matchcode für PCL-Lagerplätze“**. Deshalb wurde die SQL-Bedienhilfe auf `OXAION.LPCLAP` mit `PCFIRM`, `PCLAGO` und `PCLAPL` umgestellt.

Der nachfolgende Android-Test vom selben Tag zeigt jedoch: Auch mit diesem Stand endet die sichtbare Liste für `H04KDX` weiterhin bei `LL324`. Die frühere Aussage, der Wechsel auf `LPCLAP` löse den Realfall vollständig, ist damit **nicht bestätigt** und darf nicht mehr als erledigt gelten.

Aktueller sicherer Stand:

- `TargetLocationLookupService` verwendet weiterhin die bekannte PCL-Datei `OXAION.LPCLAP`.
- Es gibt keine `TOP`-Begrenzung, keinen `RP.*`-Filter und keinen Bestandsfilter.
- Die Suche bleibt case-insensitive.
- Es wird **keine weitere Oxaion-Tabelle oder Selektionslogik erfunden**, solange nicht geklärt ist, wo `LB13210R` die in STAGING sichtbaren späteren PCL-Lagerplätze tatsächlich herleitet.
- Die Bedienoberfläche zeigt bei jeder Lagerplatzabfrage jetzt zusätzlich die Anzahl der vom Backend gelieferten eindeutigen Lagerplätze und den letzten gelieferten Schlüssel. Damit lässt sich beim nächsten Android-Test eindeutig unterscheiden, ob bereits die Backend-/SQL-Antwort bei `LL324` endet oder nur die Darstellung/Scroll-Liste unvollständig wirkt.
- Durch Eingabe eines Präfixes wie `LL35` wird weiterhin eine direkte serverseitige Suche gegen dieselbe Quelle ausgeführt; freier Text wird trotzdem nicht als kanonischer Buchungsschlüssel akzeptiert.

Wichtig: Auch ein SQL-Treffer aus `LPCLAP` ist nur eine Bedienhilfe. Direkt vor der wirksamen `LF/LE`-Buchung validiert das Backend den ausgewählten internen Lagerplatz weiterhin über den bestätigten Weg:

`LB20115J *F4 -> LB13210R`

und liest die F4-Liste seitenweise bis zum eindeutigen `<STOP/>`. Oxaion bleibt damit die verbindliche Buchungsprüfung.

## 2. Nachfüllen – weitere Stabilisierung gegen leere Prozessseite

### Bisherige Schutzmechanismen

Die erste Korrektur hielt den bewusst gewählten Prozess als `retainedMode`, damit ein kurzzeitig fehlender `.processChoice.active`-Marker die Nachfüllkarten nicht mehr entfernt.

Die zweite Korrektur ergänzte in `process-shell.js` eine fünfsekündige Auth-Grace-Phase. Ein kurzzeitig fehlender clientseitiger Auth-Abgleich setzt die Prozess-Shell dadurch nicht mehr sofort auf die Vorgangsübersicht; der aktuelle Vorgang bleibt sichtbar, während auth-abhängige Aktionen gesperrt bleiben.

### Im Follow-up gefundene dritte Race-Condition

Der erneute Android-Test zeigte, dass ein leeres Fenster trotzdem noch kurz auftreten konnte. Im stabilen Prozess-Router gab es noch einen zweiten Besitzervergleich: Wenn `modeOwnerKey` und der gerade rekonstruierte Mitarbeiter-Schlüssel während eines Session-/UI-Refreshs kurz voneinander abwichen, rief der Router `clearForeignMode()` auf.

Dabei wurden `retainedMode`, der aktive Prozessmarker und alle Prozesskarten sofort gelöscht, während `process-shell.js` zu diesem Zeitpunkt noch `page='process'` halten konnte. In genau diesem Zwischenzustand blendet die Shell Login und Vorgangsauswahl aus, der Router hat aber bereits alle Prozesskarten versteckt: sichtbares Ergebnis ist ein leeres Prozessfenster.

Korrektur:

- Der Refresh-Router löscht bei diesem Besitzer-Mismatch die Prozesskarten nicht mehr selbst.
- Er hält den zuletzt bewusst gewählten Vorgang sichtbar und zeigt `Anmeldung wird geprüft. Der aktuelle Vorgang bleibt erhalten.`.
- Die verbindliche Entscheidung über einen tatsächlich bestätigten Mitarbeiterwechsel bleibt ausschließlich bei `process-shell.js`.
- Ein echter bestätigter Mitarbeiterwechsel führt weiterhin sicher zurück zur Anmelde-/Vorgangsübersicht.
- Backend-Session, Buchungsautorisierung und Pre-Write-Prüfungen werden dadurch nicht gelockert.

Damit gibt es im Refresh-Layer keinen vorgesehenen Übergang mehr, bei dem `processShellProcess` aktiv bleibt und gleichzeitig absichtlich alle Prozesskarten gelöscht werden.

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
- JavaScript-Syntaxprüfung umfasst `connectivity-status.js`, `process-mode-focus-fix.js`, `target-location.js`, Service Worker und die übrigen Worker-/Prozessskripte;
- .NET Build und bestehende Unit-Tests laufen auf jedem Push des Branches `feature/separate-processes`.

Die Vollständigkeit von `H04KDX` bis `LL350` kann nicht durch einen statischen Unit-Test bewiesen werden, weil dafür die realen STAGING-Stammdaten beziehungsweise der reale `LB13210R`-Datenstrom maßgeblich sind.

## 6. Nächste Live-STAGING-Prüfpunkte

- App vollständig schließen und neu starten; die App-Shell soll schnell sichtbar werden. Direkt danach müssen Starttext und gelb/grün/rote Oxaion-Anzeige erscheinen.
- `Nachfüllen` mehrfach starten, Tank scannen und auch während langsamer Oxaion-Leseabfragen prüfen, dass der gewählte Prozess sichtbar bleibt.
- Bei `Pulver aus Tank auslagern` `H04KDX` wählen und die neue Statuszeile unter dem Lagerplatzfeld ablesen: Anzahl der geladenen Lagerplätze und letzter geladener Schlüssel.
- Zusätzlich im Lagerplatz-Suchfeld gezielt `LL35` beziehungsweise `LL350` eingeben. Damit wird geprüft, ob der spätere Schlüssel bereits aus der aktuellen `LPCLAP`-Abfrage zurückkommt, auch wenn die ungefilterte Liste weiterhin bei `LL324` zu enden scheint.
- Falls Backend-/SQL-Antwort tatsächlich nur bis `LL324` reicht, muss als nächster technischer Schritt der reale `LB13210R`-F4-Datenstrom beziehungsweise die zugrunde liegende STAGING-Stammdatenquelle mit `LPCLAP` verglichen werden. Bis dahin wird keine unbestätigte Oxaion-Quelle als produktive Lösung eingebaut.
