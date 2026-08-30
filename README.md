# FAM Pulverentnahme / Pulverwechsel

Mobile WebApp fuer die sichere, nachvollziehbare Pulverentnahme, Pulvernachfuellung und den Pulverwechsel in der FAM-Produktion mit geplanter Oxaion-Integration.

## Status

Konzept- und Prototypenphase. Dieses Repository enthaelt aktuell die abgestimmte fachliche Grundlage und Zielarchitektur. Produktive Buchungslogik ist noch nicht implementiert.

## Architektur

Ein HTML-/CSS-/JavaScript-Frontend fuer Android wird als Progressive Web App (PWA) mit Service Worker und `IndexedDB` geplant. Es kommuniziert per REST/JSON mit einem ASP.NET Core Backend unter IIS. Nur das Backend greift ueber freigegebene Oxaion HTTP-Schnittstellen auf die Oxaion-Fachlogik zu. Direkte ERP-Buchungen per SQL sind ausgeschlossen.

Fuer kurze Netzwerkausfaelle werden lokale Vorgangsdaten und eine Outbox in `IndexedDB` zwischengespeichert. Offline erfasste Vorgaenge gelten dabei nicht als erfolgreich gebucht. Nach Reconnect erfolgt eine serverseitige Revalidierung und idempotente Synchronisation. PWA-Updates werden kontrolliert aktiviert, damit laufende oder noch nicht synchronisierte Vorgaenge nicht verloren gehen.

## Technologie

- Frontend: HTML, CSS, JavaScript, PWA, Service Worker, `IndexedDB`, Smartphone-Kamera fuer QR-/Barcodes
- Backend: ASP.NET Core, C#, REST API, IIS
- ERP: Oxaion HTTP-Schnittstelle, bevorzugt vorhandene BDE-/PPS-Fachlogik
- Optional: separate WebApp-Datenbank fuer Transaktionen, Idempotenz, Status und Audit Trail

## Projektwissen fuer ChatGPT / Codex

- Die fachliche Referenz ist [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md).
- Verbindliche Regeln fuer Coding Agents stehen in [`AGENTS.md`](AGENTS.md).
- PWA-, Offline-, Outbox-, Sync- und Update-Regeln stehen in [`docs/OFFLINE_PWA.md`](docs/OFFLINE_PWA.md).
- Fehler-, Retry- und Idempotenzregeln stehen in [`docs/ERROR_HANDLING.md`](docs/ERROR_HANDLING.md).
- Ein kopierbarer Repository-first-Projektprompt steht in [`PROJECT_PROMPT.md`](PROJECT_PROMPT.md).

Der Grundsatz lautet: Vor Antworten und Aenderungen zuerst den aktuellen Stand im Repository lesen und gezielt nach bereits vorhandenen Entscheidungen und Implementierungen suchen.

Oxaion-spezifische Programme, Endpunkte, Buchungsschluessel und weitere Integrationsdetails werden noch untersucht und duerfen bis zur fachlichen Bestaetigung nicht erfunden oder hart codiert werden.
