# Offene Punkte

Diese Checkliste wird waehrend des Projekts laufend aktualisiert. Offene Details duerfen nicht erfunden oder ohne Bestaetigung als Implementierungsgrundlage verwendet werden.

## Oxaion-Integration

- [x] HTTP-Buchungsfolge fuer den getesteten Vorgang `alte Mix-Charge + neue Pulvercharge -> neue Mix-Charge` bestaetigt und im STAGING-Prototyp umgesetzt; Details siehe `docs/STAGING_REAL_MIX_PROTOTYPE.md`
- [ ] Konkrete Oxaion HTTP-Aufrufe fuer die noch fehlenden Materialbuchungen identifizieren, insbesondere FA-Materialrueckmeldung und Pulverwechsel/Ruecklagerung
- [ ] Konkretes Oxaion BDE-/PPS-Programm fuer die spaetere FA-Materialrueckmeldung identifizieren
- [ ] Oxaion Buchungsschluessel Maschinenlager -> Pulverlager fuer den Pulverwechsel ermitteln
- [ ] Oxaion Buchungsschluessel Pulverlager -> Maschine fuer noch nicht durch den bestaetigten Mix-Ablauf abgedeckte Faelle ermitteln
- [x] Chargenumbuchung fuer den getesteten Nachfuell-/Mix-Vorgang mit `LM` und automatisch erzeugtem `LN` bestaetigt
- [ ] Abfrage des aktuellen Maschinenbestands in Oxaion klaeren; bis dahin ist im STAGING-Prototyp die alte Mix-Charge temporaer mit `RP10MIX_20260827_04` vorbelegt und vor jeder Buchung manuell zu pruefen
- [ ] Abfrage der Oxaion-Stammdatensperre klaeren
- [ ] Sperrdatensatz und sperrenden Benutzer technisch zuverlaessig identifizieren
- [x] Geeignete WebApp-/Backend-Transaktionsreferenz fuer den STAGING-Prototyp in den vorhandenen Oxaion-Freitextfeldern dokumentiert; finale produktive Referenz-/Suchstrategie noch bewerten
- [x] Belastbare Ergebnisabfrage fuer den getesteten Mix-Beleg ueber erneutes Oeffnen und `LB20110R *FIRSTLIST` umgesetzt; fuer andere Buchungsarten weiterhin offen
- [x] Bewusster neuer Versuch nach eindeutigem `REJECTED` umgesetzt: neue `clientOperationId`, identische Buchungsdaten und Verknuepfung ueber `retryOfClientOperationId`; kein Retry derselben abgelehnten Transaktion

## Fachliche Entscheidungen

- [ ] Mix-Chargenschema final festlegen, aktuell bevorzugt `MIX-YYMMDD-XX`
- [ ] Kompatibilitaetsregeln fuer vorhandenes Pulver und Mix-Chargen festlegen
- [ ] Endgueltige Lagerort-/Lagerplatzlogik festlegen
- [ ] Reihenfolge, Atomaritaet und Verhalten bei Teilfehlern des Pulverwechsels festlegen

## Anwendung und Betrieb

- [ ] Authentifizierungskonzept fuer Benutzer der WebApp festlegen
- [ ] Endgueltigen produktiven Web-/Application-Server festlegen
- [ ] Sichere Bereitstellung der Oxaion-Zugangsdaten und sonstigen Laufzeit-Secrets final festlegen; STAGING-Prototyp fragt den Oxaion-Benutzer und das Passwort beim Serverstart ab und uebergibt beide als `Oxaion__User` / `Oxaion__Password` an den Backend-Prozess
- [ ] Persistenztechnik fuer produktives Transaktionslog, Idempotenz, Status und Audit Trail festlegen; STAGING-Prototyp verwendet vorerst JSON-Dateien unter `App_Data/transactions`
- [ ] Eindeutigkeitsbedingungen und Aufbewahrungszeit fuer Idempotenzdaten festlegen
- [ ] Timeoutwerte und Retry-Policy nach weiterer Analyse der Oxaion-Schnittstelle festlegen
- [ ] Datenschutz-, Berechtigungs- und Aufbewahrungskonzept fuer Protokolldaten festlegen

## PWA, Offline und Synchronisation

Die grundsaetzliche Entscheidung fuer PWA, Service Worker, IndexedDB, lokale Outbox, `clientOperationId`, serverseitige Revalidierung und kontrollierte Updates ist in `docs/OFFLINE_PWA.md` dokumentiert. Der aktuelle STAGING-Prototyp verwendet bereits `IndexedDB` fuer einen offenen `clientOperationId`-Vorgang und das Backend behandelt diese ID idempotent. Offen sind weiterhin die konkreten Betriebsparameter und Prozessgrenzen:

- [ ] Maximale Gueligkeitsdauer eines lokal gecachten Maschinenzustands fachlich festlegen
- [ ] Pro Buchungsszenario festlegen, welche Schritte offline bis `PENDING_SYNC` vorbereitet werden duerfen
- [ ] Entscheiden, ob Pulverwechsel offline nur erfasst oder teilweise vorbereitet werden darf
- [ ] Produktives IndexedDB-Schema, Store-Namen, Indizes und Versionierung festlegen
- [ ] Migrationsstrategie fuer IndexedDB so definieren, dass `PENDING_SYNC`-Vorgaenge bei App-Updates erhalten bleiben
- [ ] Reihenfolge und Abhaengigkeiten bei der Synchronisation mehrerer lokaler Vorgaenge definieren
- [ ] Verhalten bei vollem, vom Benutzer geloeschtem oder vom Browser bereinigtem lokalem Speicher festlegen
- [ ] Pruefen, ob `navigator.storage.persist()` auf den eingesetzten Android-Geraeten sinnvoll und ausreichend unterstuetzt wird
- [ ] Authentifizierungsverhalten bei abgelaufener Session waehrend Offline-Betrieb definieren
- [x] Health-/Connectivity-Endpunkte fuer den STAGING-Prototyp angelegt (`/api/health` und `/api/health/oxaion`); produktive Health-Policy noch festlegen
- [ ] Frontend-/API-Versionierung und Kompatibilitaetsregeln definieren
- [ ] Technische Update-Erkennung fuer die PWA festlegen, zum Beispiel Versionsressource plus Service-Worker-Lebenszyklus
- [ ] Verhalten bei neuer App-Version und offenen `PENDING_SYNC`-Vorgaengen im Detail festlegen
- [ ] Browser Background Sync nur als optionale Optimierung pruefen; Zuverlaessigkeit darf nicht davon abhaengen
- [ ] Installations-/Rollout-Konzept fuer verwaltete Android-Geraete festlegen
