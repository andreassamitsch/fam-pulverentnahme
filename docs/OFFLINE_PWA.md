# PWA-, Offline- und Synchronisationskonzept

## Ziel

Die mobile WebApp soll auf Android-Smartphones im Browser als Progressive Web App (PWA) nutzbar sein und kurze Netzwerkunterbrechungen robust ueberstehen.

Offline-Faehigkeit darf jedoch niemals zu einer unsicheren Pulverfreigabe, einer unkontrollierten Oxaion-Buchung oder einer Doppelbuchung fuehren. Oxaion und das Backend bleiben fuer produktive Buchungen und den aktuellen fachlichen Zustand fuehrend.

## Grundsaetze

- Die WebApp wird als PWA mit Web App Manifest und Service Worker aufgebaut.
- Statische App-Ressourcen werden fuer den Offline-Start lokal gecacht.
- Fachliche Offline-Daten werden in `IndexedDB` gespeichert, nicht in `localStorage`.
- Lokaler Speicher ist nur ein robuster Zwischenpuffer. Er ersetzt weder Backend noch Oxaion als fuehrende Instanz.
- Ein offline erfasster Vorgang ist **nicht** automatisch eine erfolgreiche Buchung.
- Die Oberflaeche muss jederzeit klar zwischen `lokal erfasst`, `wartet auf Synchronisation`, `serverseitig angenommen` und `erfolgreich in Oxaion gebucht` unterscheiden.
- Bei Unsicherheit gilt Fail-safe: Vorgang stoppen oder zur Klaerung markieren statt fachliche Annahmen zu treffen.

## Service Worker und App-Shell

Der Service Worker darf die fuer den Start und die Bedienung notwendigen statischen Ressourcen cachen, insbesondere:

- HTML
- JavaScript
- CSS
- Icons und lokale Assets
- Web App Manifest

Schreibende API-Antworten und ERP-Buchungsergebnisse duerfen nicht als fachliche Wahrheit aus einem Service-Worker-Cache verwendet werden.

Lesende Daten mit fachlicher Bedeutung, insbesondere Maschinenzustaende, werden nur ueber die explizit definierte IndexedDB-Cachelogik mit Zeitstempel und Versionsinformation verwendet.

## PWA-Installierbarkeit auf Android

Der STAGING-Prototyp ist technisch als installierbare PWA konfiguriert:

- `wwwroot/manifest.webmanifest` enthaelt App-Name, `start_url`, `scope`, `display: standalone`, Theme-/Hintergrundfarbe und die fuer Chromium-basierte Android-Browser benoetigten Icon-Groessen 192x192 und 512x512.
- Ein separates maskierbares 512x512-Icon ist fuer adaptive Android-App-Icons vorhanden.
- Das Browser-Favicon liegt lokal unter `wwwroot/icons/favicon.svg` und wird in `index.html` referenziert.
- Die PWA-Icons liegen lokal unter `wwwroot/icons/` und werden zusammen mit der App-Shell vom Service Worker gecacht.
- Der vorhandene Service Worker wird beim App-Start durch `app.js` registriert.
- Die App verwendet weiterhin `display: standalone`, damit eine installierte PWA ohne normale Browser-Adressleiste startet.

Fuer eine regulaere PWA-Installation auf Android muss die vom Smartphone aufgerufene WebApp ueber **HTTPS** bereitgestellt werden. `localhost` beziehungsweise Loopback-Adressen sind nur Entwicklungs-Ausnahmen. Ein reiner HTTP-Aufruf eines IIS-Servers im Netzwerk erfuellt diese Voraussetzung nicht. Die HTTPS-Bereitstellung beziehungsweise das Zertifikat wird in der IIS-/Deployment-Konfiguration geloest und nicht durch unsichere Ausnahmen im Frontend umgangen.

Die Installierbarkeit aendert keine fachliche Offline-Grenze: Eine installierte PWA darf produktive Oxaion-Buchungen weiterhin weder offline simulieren noch als erfolgreich bestaetigt darstellen.

## IndexedDB

Mindestens folgende Datenbereiche sind vorgesehen:

### Lokaler Vorgang

- `clientOperationId`
- Vorgangsart
- Fertigungsauftrag
- Rohmaterial und Charge
- Planmaschine
- Ist-Maschine
- Mix-Charge, soweit bereits bekannt
- lokale Scan- und Eingabedaten
- Erfassungszeitpunkt
- lokaler Sync-Status

### Maschinenzustands-Cache

- Maschinen-ID
- Pulverartikel
- Mix-Charge beziehungsweise weitere fuer die Kompatibilitaetspruefung benoetigte Chargeninformation
- Systemmenge, soweit relevant und eindeutig ermittelt
- Zeitpunkt der letzten bestaetigten Serverabfrage
- Server-/Datensatzrevision, ETag oder vergleichbare Versionsinformation, sofern die spaetere Schnittstelle dies anbietet
- Quelle beziehungsweise Referenz der Abfrage, soweit sinnvoll

### Outbox

Noch nicht serverseitig bestaetigte Vorgaenge werden in einer persistenten lokalen Outbox abgelegt.

Beispielhafte lokale Sync-Zustaende:

- `LOCAL_DRAFT`
- `PENDING_SYNC`
- `SYNCING`
- `SYNCED`
- `CONFLICT`
- `FAILED`
- `MANUAL_REVIEW_REQUIRED`

Diese lokalen Sync-Zustaende sind von den Backend-/Oxaion-Transaktionsstatus aus `ERROR_HANDLING.md` getrennt zu betrachten.

## Client-Operation-ID und Backend-Transaktions-ID

Da ein Vorgang bereits offline entstehen kann, erzeugt das Frontend beim lokalen Anlegen eine global eindeutige, unveraenderliche `clientOperationId`, zum Beispiel als UUID.

Die bestehende serverseitige Transaktions-/Vorgangs-ID bleibt erhalten. Beim ersten erfolgreichen Kontakt mit dem Backend wird die `clientOperationId` eindeutig einer Backend-Transaktion zugeordnet beziehungsweise als serverseitiger Idempotency Key verwendet.

Verbindliche Regel:

- dieselbe `clientOperationId` darf serverseitig nicht zu mehreren wirksamen Oxaion-Buchungen fuehren;
- ein erneutes Senden derselben Outbox-Nachricht muss idempotent behandelt werden;
- eine bereits bekannte `clientOperationId` liefert den vorhandenen serverseitigen Status statt eine neue Buchung zu erzeugen.

Damit bleibt die zentrale Duplicate Prevention im Backend bestehen und Offline-Erfassung kann trotzdem eindeutig korreliert werden.

## Offline-Verhalten

### Erfassung

Scans und noch nicht serverseitig bestaetigte Bedienerschritte koennen lokal gespeichert werden, sofern der jeweilige Prozessschritt fachlich offline erlaubt ist.

Die Anzeige muss dabei eindeutig sein, zum Beispiel:

`Offline erfasst - noch nicht serverseitig bestaetigt.`

Die App darf dabei niemals `erfolgreich gebucht` anzeigen.

### Verwendung eines lokalen Maschinenzustands

Ein lokaler Maschinenzustand darf nur verwendet werden, wenn:

1. er aus einer zuvor eindeutig bestaetigten Serverabfrage stammt;
2. alle fuer die Entscheidung benoetigten Felder vorhanden und eindeutig sind;
3. der Datensatz nicht aelter als die konfigurierte maximale Offline-Gueligkeitsdauer ist;
4. keine lokale Information auf einen zwischenzeitlich unbekannten oder widerspruechlichen Zustand hindeutet.

Die konkrete maximale Gueligkeitsdauer ist noch fachlich festzulegen und wird nicht hart codiert.

Wenn diese Voraussetzungen nicht erfuellt sind, darf die App keine sichere Maschinenfreigabe vortaeuschen. Beispielmeldung:

`Der aktuelle Maschinenzustand kann offline nicht sicher geprueft werden. Vorgang derzeit nicht freigegeben.`

### Grenzen der Offline-Faehigkeit

Produktive Oxaion-Buchungen werden nicht lokal simuliert und nicht als erfolgreich angenommen.

Welche Prozessschritte bei gueltigem Maschinen-Cache komplett bis `PENDING_SYNC` vorbereitet werden duerfen, wird pro Buchungsszenario festgelegt. Bis dahin gilt fuer nicht eindeutig freigegebene Schritte die sichere Variante: lokal erfassen beziehungsweise zwischenspeichern, aber keine fachliche Endfreigabe vortaeuschen.

## Wiederherstellung der Verbindung

`navigator.onLine` ist nur ein Hinweis und kein ausreichender Beweis fuer die Erreichbarkeit des Backends. Die WebApp prueft die tatsaechliche Backend-Erreichbarkeit ueber einen geeigneten Health-/Connectivity-Aufruf.

Sobald das Backend wieder erreichbar ist:

1. Outbox-Eintraege mit `PENDING_SYNC` erkennen;
2. geordnet und mit unveraenderter `clientOperationId` senden;
3. serverseitig den aktuellen fachlichen Zustand erneut validieren;
4. insbesondere den aktuellen Maschinenzustand vor einer produktiven Buchung erneut gegen die fuehrenden Daten pruefen;
5. Idempotency-/Duplicate-Prevention pruefen;
6. erst danach eine zulaessige Oxaion-Buchung ausloesen;
7. serverseitige Transaktions-ID und Status lokal speichern;
8. lokalen Sync-Status aktualisieren.

Ein lokaler Cache ersetzt die serverseitige Revalidierung nach Reconnect nicht.

## Konflikte nach Reconnect

Hat sich der serverseitige Zustand seit der Offline-Erfassung geaendert, darf die WebApp nicht automatisch ueberschreiben, vermischen oder blind buchen.

Beispiele:

- Maschine enthaelt inzwischen ein anderes Pulver;
- Mix-Charge oder Bestand ist nicht mehr kompatibel;
- Fertigungsauftrag oder relevante Daten haben sich geaendert;
- derselbe Vorgang wurde bereits anderweitig verarbeitet;
- Server kann den urspruenglich angenommenen Zustand nicht eindeutig bestaetigen.

Dann wird der Vorgang auf `CONFLICT` beziehungsweise `MANUAL_REVIEW_REQUIRED` gesetzt und dem Bediener eine konkrete Massnahme angezeigt.

## Synchronisation und Retry

- Automatische Synchronisation darf denselben lokalen Vorgang mehrfach uebertragen, **wenn** das Backend die `clientOperationId` idempotent verarbeitet.
- Ein Transport-Retry ist nicht gleichbedeutend mit einem erneuten Oxaion-Buchungsversuch.
- Sobald das Backend eine Oxaion-Buchung begonnen hat und deren Ausgang unklar ist, gelten unveraendert die Regeln aus `ERROR_HANDLING.md`: `UNCERTAIN`, kein blinder Retry.
- Synchronisationsversuche, Zeitpunkte, Fehler und serverseitige Referenzen werden nachvollziehbar gespeichert.

Die WebApp soll mindestens bei folgenden Ereignissen erneut synchronisieren:

- App-Start
- Rueckkehr der App in den Vordergrund
- erkannter Wiederherstellung der Backend-Verbindung
- bewusster manueller Sync-/Retry-Aktion

Die Browser Background Sync API darf spaeter als Optimierung verwendet werden, ist aber keine Voraussetzung fuer die fachliche Zuverlaessigkeit, da ihre Verfuegbarkeit browserabhaengig ist.

## PWA-Update-Strategie

Eine neue App-Version soll automatisch erkannt und heruntergeladen werden koennen, aber nicht unkontrolliert mitten in einem kritischen Vorgang aktiviert werden.

### Versionspruefung

Die App prueft beim Start und regelmaessig auf eine neue Frontend-Version. Die konkrete technische Umsetzung kann beispielsweise ueber eine explizite Versionsressource und den Service-Worker-Lebenszyklus erfolgen.

### Sichere Aktivierung

Eine neue Version darf keinen automatischen Reload erzwingen, solange mindestens einer der folgenden Zustaende vorliegt:

- aktiver Scan-/Buchungsvorgang;
- ungespeicherte lokale Aenderungen;
- `PENDING_SYNC`-Eintraege, deren Daten durch ein Update gefaehrdet waeren;
- laufende Synchronisation.

Die App zeigt stattdessen einen klaren Hinweis, zum Beispiel:

`Neue Version verfuegbar. Aktualisierung erfolgt, sobald der aktuelle Vorgang sicher abgeschlossen ist.`

Nach Erreichen eines sicheren Zustands darf die neue Version aktiviert werden.

### Datenbestand bei Updates

- Service-Worker-Cache und IndexedDB sind getrennt zu behandeln.
- Das Bereinigen alter App-Caches darf keine Outbox- oder Vorgangsdaten loeschen.
- IndexedDB-Schemamigrationen muessen rueckwaertskompatibel beziehungsweise migrationssicher ausgefuehrt werden.
- Ein Update darf niemals zum Verlust noch nicht synchronisierter Vorgaenge fuehren.

## Lokaler Speicher und Lebensdauer

Browserdaten sind an Browserprofil, Origin/Domain und Geraet gebunden und koennen durch Benutzer, Browser oder Betriebssystem geloescht werden.

Deshalb gilt:

- IndexedDB ist ein robuster Zwischenpuffer, kein dauerhaftes zentrales Archiv.
- Erfolgreich synchronisierte Daten werden serverseitig nachvollziehbar persistiert.
- Secrets, Passwoerter, Oxaion-Zugangsdaten und Auth-Tokens duerfen nicht als frei lesbare fachliche Offline-Daten gespeichert werden.
- Die Nutzung der Persistent Storage API (`navigator.storage.persist()`) kann geprueft werden, darf aber nicht als garantierte Datensicherung angenommen werden.

## Bedieneranzeige

Mindestens folgende Zustaende muessen visuell eindeutig unterscheidbar sein:

- Online / Backend erreichbar
- Offline
- lokal gespeichert
- wartet auf Synchronisation
- Synchronisation laeuft
- serverseitig angenommen
- erfolgreich gebucht
- Konflikt
- Buchungsergebnis unklar
- manuelle Klaerung erforderlich

Insbesondere duerfen `lokal gespeichert`, `serverseitig angenommen` und `erfolgreich gebucht` nicht gleich dargestellt oder bezeichnet werden.

## Noch offene Entscheidungen

Die konkreten offenen Punkte werden zentral in `docs/OPEN_POINTS.md` gepflegt. Fuer dieses Konzept sind insbesondere noch festzulegen:

- maximale Gueligkeitsdauer eines lokalen Maschinenzustands;
- welche konkreten Prozessschritte offline bis `PENDING_SYNC` vorbereitet werden duerfen;
- IndexedDB-Schema und Migrationsstrategie;
- genaue Synchronisationsreihenfolge bei voneinander abhaengigen Vorgaengen;
- Verhalten bei vollem oder vom Browser geloeschtem lokalem Speicher;
- Authentifizierungsverhalten bei abgelaufener Session waehrend Offline-Betrieb;
- genaue Frontend-/API-Versionierung und Kompatibilitaetsregeln;
- technisches Installations-/Rollout-Konzept fuer verwaltete Android-Geraete.
