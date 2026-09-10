# STAGING-Korrekturen – 10.09.2026

## Anlass

Im Android-STAGING-Test bleibt die Oberfläche beim Prozess `Pulver nachfüllen` nach dem Tankscan weiterhin reproduzierbar leer, wenn zuvor die App-Daten gelöscht wurden und die App danach direkt verwendet wird.

Ein neuer Live-Befund grenzt den Fehler deutlich weiter ein:

- App-Daten löschen;
- App neu starten;
- direkt `Pulver nachfüllen` verwenden -> leere Seite nach Tankscan;
- dagegen nach dem Neustart zuerst in der Vorgangsübersicht einmal per Pull-to-refresh aktualisieren;
- anschließend `Pulver nachfüllen` verwenden -> der Tankscan funktioniert und die Nachfüllseite bleibt sichtbar.

Damit ist technisch nicht bewiesen, dass ausschließlich der Service Worker die Ursache ist. Der reproduzierbare Unterschied zeigt aber eindeutig, dass Erststart und der darauffolgende kontrollierte Seitenstart unterschiedliche Laufzeitzustände besitzen. Nach einem vollständigen Löschen der App-Daten besitzt die erste Seite zunächst keinen aktiven Service-Worker-Controller. Erst eine folgende Navigation beziehungsweise Aktualisierung läuft unter dem inzwischen installierten Worker.

## Korrektur v31

Der manuelle Pull-to-refresh wird nicht als Bediener-Workaround akzeptiert. Der Erststart wird jetzt automatisch normalisiert:

1. `connectivity-status.js` erkennt, ob die aktuelle Seite ohne Service-Worker-Controller gestartet wurde.
2. In genau diesem Erststartzustand werden Prozesswahl und Tankscan kurz abgefangen, damit kein Vorgang vor Abschluss der App-Shell-Initialisierung gestartet werden kann.
3. Die App wartet auf `navigator.serviceWorker.ready`.
4. Sobald der Worker aktiv ist und noch kein Prozess, Scanner oder offener Buchungsvorgang aktiv ist, erfolgt genau ein automatischer Reload.
5. Danach läuft die Seite im gleichen kontrollierten Zustand, den der erfolgreiche manuelle Pull-to-refresh im Live-Test hergestellt hat.
6. Bereits kontrollierte Starts werden niemals deshalb neu geladen.
7. Ein laufender Prozess beziehungsweise ein offener Buchungsvorgang wird nicht automatisch neu geladen.

Diese Änderung betrifft ausschließlich den App-/PWA-Startzustand. Backend-Session, Oxaion-Revalidierung, Transaktionsstatus, `clientOperationId`, Idempotenz und die Regel `kein Blind-Retry` bleiben unverändert.

## Zusätzlicher Nachfüll-Sichtbarkeits-Guard

Unabhängig vom Erststart wurde der bestehende Nachfüll-Guard verschärft:

- Sobald die Oberfläche den Prozess `replenish` selbst als aktiv markiert, dürfen `machineStep`, `sourcesSection`, `mixSection`, `bookingStep` und `result` nicht vollständig über `processModeHidden` verschwinden.
- Intrinsische Sichtbarkeit bleibt erhalten: beispielsweise bleibt `sourcesSection` vor erfolgreichem Tankbestand weiterhin normal `hidden`; der Guard entfernt nur die konkurrierende Prozessmodus-Ausblendung.
- Bei kurzfristig fehlendem Client-Auth-Abgleich bleiben die sichtbaren Karten bestehen, während die vorhandene Auth-Logik die Aktionen sperrt.
- Bei dauerhaft fehlender Authentifizierung bleibt `process-shell.js` zuständig und kehrt nach der bestehenden Grace-Phase sicher zur Übersicht zurück.
- Wenn der Router intern auf `Vorgang auswählen.` zurückfällt, obwohl `replenish` sichtbar aktiv ist, kann der Guard den privaten Routerzustand durch erneute Auswahl von `replenish` wiederherstellen. Dabei wird keine Buchung erzeugt und der vorhandene Legacy-Nachfüllzustand nicht verworfen.

## Diagnosezugriff

Das Diagnoseprotokoll ist nicht mehr von einer korrekt erkannten leeren Prozessseite oder von `Dev-Infos` abhängig.

Im Header ist im STAGING-Stand dauerhaft der Button `Diagnose` sichtbar. Er öffnet ein Diagnosefenster mit `Diagnose kopieren`.

Das Protokoll enthält unter anderem:

- UI-Shell (`home`, `process`, unbekannt);
- sichtbaren und gemerkten Prozessmodus;
- aktuelle Schrittanzeige;
- Authentifizierungszustand ausschließlich als Boolean;
- sichtbare Prozesskarten;
- `stockLoading` und `sourceLoading`;
- aktive relevante JavaScript-Dateien;
- JavaScript-Fehler und unbehandelte Promise-Fehler;
- Online-/Visibility-Zustand;
- Navigationstyp;
- ob die Seite von einem Service Worker kontrolliert wird;
- ob es sich um den automatischen zweiten Start nach einer frischen Worker-Installation handelt.

Nicht protokolliert werden Passwörter, Auth-Tokens, Cookies, Connection Strings, Personalnummern oder Mitarbeiternamen.

## Service Worker v31

Der App-Shell-Cache heißt:

`fam-pulver-staging-v31-first-controlled-start-diag-20260910`

Beim Installieren einer neuen Cache-Generation werden die statischen Assets mit `cache: reload` neu validiert. Dadurch soll nicht versehentlich eine alte HTTP-Cache-Version derselben Script-URL in eine neue Cache-Storage-Generation übernommen werden.

API-Aufrufe bleiben weiterhin vom Service-Worker-Cache ausgeschlossen.

## STAGING-Artefakt-Namenskonvention

Ab 10.09.2026 tragen herunterladbare STAGING-ZIP-Dateien immer Erstellungsdatum und Erstellungsuhrzeit im Dateinamen.

Verbindliches Muster:

`FAM-Pulverentnahme-STAGING-yyyy-MM-dd_HH-mm-ss-win-x64.zip`

Der Zeitstempel wird beim GitHub-Actions-Build in der Zeitzone `Europe/Vienna` erzeugt. Damit ist bei mehreren Testständen sofort erkennbar, welches Paket neuer ist. Das GitHub-Actions-Artefakt erhält denselben Namen ohne die technisch beim Download ergänzte `.zip`-Endung.

## Nächster Live-Test

Der relevante Test ist jetzt bewusst wieder der problematische Erststart:

1. App-Daten löschen.
2. App neu öffnen.
3. Nicht manuell herunterziehen/aktualisieren.
4. Die App muss den Erststart selbst kurz vorbereiten und einmal automatisch neu laden.
5. Anmelden.
6. `Pulver nachfüllen` öffnen.
7. Maschinentank scannen.
8. Prüfen, dass die Seite sichtbar bleibt und der nächste Nachfüllschritt erscheint.

Falls die Seite erneut leer wird, oben im Header `Diagnose` öffnen, `Diagnose kopieren` drücken und den vollständigen Text in den Projektchat übernehmen. Damit kann insbesondere geprüft werden, ob der Fehler trotz kontrolliertem Worker-Start auftritt und welche UI-Schicht zuletzt den Zustand verändert hat.
