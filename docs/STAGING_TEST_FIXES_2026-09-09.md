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

## 2. Nachfüllen – leere Prozessseite nach Tankscan

### Bisherige Korrekturen

Mehrere frühere Ursachen beziehungsweise Race-Conditions wurden bereits technisch beseitigt:

- ein kurzzeitig fehlender `.processChoice.active`-Marker darf den bewusst gewählten Nachfüllprozess nicht mehr sofort verwerfen;
- `process-shell.js` besitzt eine fünfsekündige Auth-Grace-Phase, damit ein kurzzeitiger Client-Auth-Abgleich die Prozessseite nicht sofort verlässt;
- `process-mode-focus-fix.js` löscht bei einem kurzfristigen Besitzer-Mismatch nicht mehr vorzeitig alle Prozesskarten;
- ein alter zweiter Bootstrap von `process-mode.js` aus `article-colors.js` wurde entfernt. `process-mode.js` wird im aktuellen App-Shell nur noch einmal aus `index.html` geladen.

Der letzte Punkt war eine reale technische Abweichung und musste behoben werden. Der erneute Android-Test mit geleerten App-Daten zeigt jedoch eindeutig, dass dieser Doppel-Bootstrap **nicht die alleinige Ursache** der weiterhin reproduzierbaren leeren Nachfüllseite war. Die frühere Formulierung „deterministische Ursache“ wird deshalb hiermit korrigiert.

### Video-Nachweis des verbleibenden Zustands

Im vom Bediener aufgenommenen Android-Video ist der Fehler reproduzierbar direkt nach dem Maschinentank-Scan sichtbar:

- die Kopfzeile bleibt auf **`Nachfüllen`**;
- der Prozess-Shell-Zustand bleibt damit erkennbar im gewählten Vorgang;
- die Schrittzeile springt gleichzeitig auf **`Vorgang auswählen.`**;
- darunter sind `machineStep`, `sourcesSection` und `bookingStep` nicht mehr sichtbar.

Dieser Zustand ist besonders aussagekräftig: Der sichtbare Prozessmarker und die Shell kennen weiterhin `replenish`, während eine zweite interne Zustandsvariable bereits auf „kein Modus“ zurückgefallen ist.

### Konkrete Codeursache

`process-mode.js` verwaltet zusätzlich zum sichtbaren `.processChoice.active`-Marker eine private Variable:

```text
let mode = null
```

In `refreshAuth()` stand bislang sinngemäß:

```text
wenn Auth-Abgleich gerade nicht gültig ist:
    mode = null
    Nachfüllkarten ausblenden
```

Ein kurzfristiger Client-Auth-/Session-Abgleich während der asynchronen Tank-/Quellbestandsaktualisierung kann deshalb den **privaten** Routermodus auf `null` setzen. Der separate Focus-/Shell-Schutz lässt den sichtbaren aktiven Button jedoch absichtlich bestehen. Sobald der Auth-Abgleich wieder gültig ist, sieht `process-mode.js` weiterhin `mode == null` und setzt die Anweisung `Vorgang auswählen.` beziehungsweise blendet die Legacy-Nachfüllkarten aus.

Das erklärt exakt die im Video sichtbare Kombination:

- Header: `Nachfüllen`
- Schrittzeile: `Vorgang auswählen.`
- keine Nachfüllkarten

und weiterhin, warum nur `Nachfüllen` betroffen ist: Dieser Vorgang verwendet noch die vorhandenen Legacy-Karten `machineStep`, `sourcesSection`, `mixSection`, `bookingStep` und `result`. Die neueren Prozessarten besitzen eigene `.processPanel`-Container.

### Korrektur – Replenishment Router Guard

Der neue `replenish-router-guard.js` merkt sich ausschließlich den gewählten **UI-Prozessmodus** in `sessionStorage`. Das ist kein fachlicher Buchungszustand und enthält weder Mengen, Chargen, Transaktions-IDs noch Zugangsdaten.

Der Guard erkennt genau den beobachteten Inkonsistenzzustand:

- Shell steht weiterhin auf Prozessseite;
- sichtbarer beziehungsweise zuletzt bewusst gewählter Modus ist `replenish`;
- Auth-Abgleich ist wieder gültig;
- gleichzeitig lautet die Anweisung `Vorgang auswählen.` oder alle Legacy-Nachfüllkarten sind ausgeblendet.

Dann wird `Pulver nachfüllen` intern erneut ausgewählt. Dadurch erhält die private `mode`-Variable in `process-mode.js` wieder `replenish`. Beim Nachfüllprozess verwirft `selectMode('replenish')` **keinen** bereits gescannten Tank- oder Quellenzustand; `resetStates()` betrifft nur die Zustandsobjekte der separaten Prozesse `tank-out`, `fill-new` und `fa-consumption`.

Die Wiederherstellung ist reine Frontend-Navigation:

- keine Oxaion-Buchung wird ausgelöst;
- keine `clientOperationId` wird erzeugt oder verändert;
- Backend-Session und Autorisierung werden nicht umgangen;
- Pre-Write-Revalidierung, Idempotenz und Recovery bleiben unverändert.

## 3. Kopierbares In-App-Diagnoseprotokoll

Damit weitere UI-Probleme nicht mehr anhand von Vermutungen analysiert werden müssen, ist `ui-diagnostics.js` hinzugekommen.

### Aufgezeichnet wird

Nur technischer UI-Zustand, unter anderem:

- Zeitstempel;
- Shell `home/process`;
- sichtbarer aktiver Prozessmodus und zuletzt gemerkter UI-Modus;
- aktuelle Schrittanweisung;
- Auth-Abgleich nur als `true/false`;
- ob Auswahl/Backend-Authentifizierung vorhanden ist, jeweils nur als `true/false`;
- Name/Hash der aktuell installierten `refreshWorkerFlow`-Funktion;
- Anzahl/URLs der geladenen `process-mode.js`-Scripts;
- Maschinen-Lagerort, Artikel und Maschinenbestandsstatus;
- `stockLoading` / `sourceLoading`;
- welche Prozesskarten tatsächlich sichtbar sind;
- Scanner-/Modal-/Busy-Zustand;
- Browser `online` und `visibilityState`;
- JavaScript-Fehler und unbehandelte Promise-Fehler in bereinigter Form;
- relevante Klicks wie Prozesswahl, Tankscan, Scanner und Buchungsbutton.

### Nicht aufgezeichnet wird

- Passwörter;
- Tokens oder Cookies;
- Authorization-Header;
- Connection Strings;
- Request-Bodies;
- Personalnummern;
- Mitarbeiternamen.

Das Log liegt nur im `sessionStorage` der aktuellen App-Sitzung und ist auf die letzten Einträge begrenzt. Es ist keine ERP-Wahrheit und kein Ersatz für Backend-/Transaktionslogging.

### Bedienung

- Mit aktivierten `Dev-Infos` steht beim Backend-Bereich der Button **`Diagnose kopieren`** zur Verfügung.
- Erkennt die App länger als kurzzeitig eine Prozessseite ohne sichtbare Prozesskarten, erscheint statt einer vollständig leeren Seite die Karte **`Anzeigeproblem erkannt`**.
- Diese Karte bietet **`Anzeige wiederherstellen`** und **`Diagnose kopieren`**.
- Wenn die Clipboard-API nicht verfügbar ist, wird der Diagnosetext in einem markierten Textfeld bereitgestellt.

Der kopierte Text beginnt mit der Diagnoseversion und enthält anschließend den aktuellen Snapshot sowie die zeitlich geordneten Ereignisse. Dieser Text kann direkt in den Projektchat eingefügt werden.

## 4. Langsamer PWA-Start / nur Logo sichtbar

Der frühere Service Worker verwendete für Navigationsrequests `network first`. Bei schlechter oder fehlender Verbindung konnte die installierte Android-PWA deshalb auf den Netzwerk-/TCP-Timeout warten, bevor die bereits lokal vorhandene App-Shell angezeigt wurde. Während dieser Zeit war nur der native PWA-Startbildschirm beziehungsweise das Logo sichtbar.

Der aktuelle Service Worker verwendet für Navigation die lokal gecachte App-Shell sofort und aktualisiert die HTML-Version parallel im Hintergrund. `/api/...`-Requests werden weiterhin nicht vom Service Worker gecacht.

Im Follow-up wurde zusätzlich eine Aktualisierungslücke geschlossen: Die Service-Worker-Registrierung in `app.js` lief erst nach mehreren Startinitialisierungen. `connectivity-status.js` registriert beziehungsweise prüft den Worker jetzt bereits beim frühen Laden des Dokuments mit `updateViaCache: 'none'`, ohne auf Backend-/Oxaion-Initialisierung zu warten.

Bewusst **nicht** verwendet wird `skipWaiting`: Eine neu geladene Frontend-Version darf einen bereits laufenden Buchungsvorgang nicht unkontrolliert übernehmen. Ein bereits installierter alter Worker kann daher für seine Ablösung weiterhin einen sauberen Seiten-/App-Lebenszyklus benötigen.

Wichtig für die Interpretation: Solange Android noch ausschließlich den nativen PWA-Splashscreen zeigt, ist noch kein HTML der App sichtbar; dort kann JavaScript keinen dynamischen Verbindungstext einblenden. Ziel ist deshalb, diese Phase durch den cache-first App-Shell-Start möglichst kurz zu halten. Sobald das HTML gerendert ist, zeigt die App ihren eigenen Start-/Verbindungsstatus.

## 5. Sichtbarer Verbindungsstatus

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

Für die regelmäßige Oxaion-Ampel wird der leichte Endpunkt `GET /api/connectivity/oxaion` verwendet. Er öffnet und schließt nur eine Oxaion-App-Tunnel-Session. Der deutlich schwerere `/api/health/oxaion`-Dialog-Smoke-Test bleibt für den manuellen Dev-Verbindungstest bestehen und wird nicht periodisch für die Bediener-Ampel ausgeführt.

Die Ampel ist ausschließlich eine Erreichbarkeitsanzeige. Sie ersetzt keine aktuelle Pre-Write-Revalidierung und keinen Transaktionsstatus.

## 6. Service Worker / App-Shell

Der neue App-Shell verwendet den Cache:

`fam-pulver-staging-v30-replenish-guard-diag-20260909`

Neu gecacht werden insbesondere:

- `/ui-diagnostics.js?v=20260909-ui-diag-1`
- `/replenish-router-guard.js?v=20260909-replenish-guard-1`

Es wird weiterhin bewusst kein unkontrolliertes `skipWaiting` verwendet.

## 7. Tests und CI

Die automatisierten Tests beziehungsweise CI-Prüfungen decken weiterhin ab:

- Ziel-Lagerplatzsuche verwendet aktuell `OXAION.LPCLAP` mit `PCFIRM/PCLAGO/PCLAPL`;
- keine `TOP`-Begrenzung;
- weiterhin rein lesende SQL-Abfrage ohne Bestands-/RP-Filter;
- `process-mode.js` darf im Frontend-Bootstrap nur einmal geladen werden;
- Diagnose- und Guard-Script müssen in `index.html` eingebunden sein;
- der Guard muss den beobachteten Zustand `Vorgang auswählen.` im aktiven Nachfüllprozess erkennen und die interne Auswahl wiederherstellen können;
- der Service Worker muss Diagnose und Guard im App-Shell enthalten;
- der Diagnoselogger enthält statische Schutzprüfungen gegen die Aufnahme von Personalnummer, Name oder Passwortfeldern;
- JavaScript-Syntaxprüfung und .NET Build/Tests laufen auf jedem Push des Branches `feature/separate-processes`.

## 8. Nächster Live-STAGING-Test

Nach Bereitstellung dieses Standes:

1. installierte PWA vollständig schließen und neu starten;
2. `Nachfüllen` wählen;
3. Maschinentank scannen;
4. prüfen, ob Tankbestand und Nachfüllschritt sichtbar bleiben beziehungsweise unmittelbar wiederhergestellt werden;
5. falls die Seite wieder inkonsistent wird, auf **`Diagnose kopieren`** drücken und den kompletten Text in den Projektchat einfügen.

Für diesen Test muss `H04KDX` nicht erneut untersucht werden; dessen vollständige Lagerplatzanzeige ist im aktuellen Stand live bestätigt.
