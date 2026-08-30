# Offene Punkte

Diese Checkliste wird waehrend des Projekts laufend aktualisiert. Offene Details duerfen nicht erfunden oder ohne Bestaetigung als Implementierungsgrundlage verwendet werden.

## Oxaion-Integration

- [ ] Konkrete Oxaion HTTP-Aufrufe fuer alle Materialbuchungen identifizieren
- [ ] Konkretes Oxaion BDE-/PPS-Programm beziehungsweise Programme identifizieren
- [ ] Oxaion Buchungsschluessel Maschinenlager -> Pulverlager ermitteln
- [ ] Oxaion Buchungsschluessel Pulverlager -> Maschine ermitteln
- [ ] Eventuell benoetigte Chargenumbuchungen klaeren
- [ ] Abfrage des aktuellen Maschinenbestands in Oxaion klaeren
- [ ] Abfrage der Oxaion-Stammdatensperre klaeren
- [ ] Sperrdatensatz und sperrenden Benutzer technisch zuverlaessig identifizieren
- [ ] Klaeren, ob die WebApp-Transaktions-ID als Oxaion-Referenz uebergeben und abgefragt werden kann
- [ ] Belastbare Status-/Ergebnisabfrage fuer unklare Buchungsergebnisse identifizieren

## Fachliche Entscheidungen

- [ ] Mix-Chargenschema final festlegen, aktuell bevorzugt `MIX-YYMMDD-XX`
- [ ] Kompatibilitaetsregeln fuer vorhandenes Pulver und Mix-Chargen festlegen
- [ ] Endgueltige Lagerort-/Lagerplatzlogik festlegen
- [ ] Reihenfolge, Atomaritaet und Verhalten bei Teilfehlern des Pulverwechsels festlegen

## Anwendung und Betrieb

- [ ] Authentifizierungskonzept fuer Benutzer der WebApp festlegen
- [ ] Endgueltigen produktiven Web-/Application-Server festlegen
- [ ] Sichere Bereitstellung der Oxaion-Zugangsdaten und sonstigen Laufzeit-Secrets festlegen
- [ ] Persistenztechnik fuer Transaktionslog, Idempotenz, Status und Audit Trail festlegen
- [ ] Eindeutigkeitsbedingungen und Aufbewahrungszeit fuer Idempotenzdaten festlegen
- [ ] Timeoutwerte und Retry-Policy nach Analyse der Oxaion-Schnittstelle festlegen
- [ ] Datenschutz-, Berechtigungs- und Aufbewahrungskonzept fuer Protokolldaten festlegen

## PWA, Offline und Synchronisation

Die grundsaetzliche Entscheidung fuer PWA, Service Worker, IndexedDB, lokale Outbox, `clientOperationId`, serverseitige Revalidierung und kontrollierte Updates ist in `docs/OFFLINE_PWA.md` dokumentiert. Offen sind noch die konkreten Betriebsparameter und Prozessgrenzen:

- [ ] Maximale Gueligkeitsdauer eines lokal gecachten Maschinenzustands fachlich festlegen
- [ ] Pro Buchungsszenario festlegen, welche Schritte offline bis `PENDING_SYNC` vorbereitet werden duerfen
- [ ] Entscheiden, ob Pulverwechsel offline nur erfasst oder teilweise vorbereitet werden darf
- [ ] IndexedDB-Schema, Store-Namen, Indizes und Versionierung festlegen
- [ ] Migrationsstrategie fuer IndexedDB so definieren, dass `PENDING_SYNC`-Vorgaenge bei App-Updates erhalten bleiben
- [ ] Reihenfolge und Abhaengigkeiten bei der Synchronisation mehrerer lokaler Vorgaenge definieren
- [ ] Verhalten bei vollem, vom Benutzer geloeschtem oder vom Browser bereinigtem lokalem Speicher festlegen
- [ ] Pruefen, ob `navigator.storage.persist()` auf den eingesetzten Android-Geraeten sinnvoll und ausreichend unterstuetzt wird
- [ ] Authentifizierungsverhalten bei abgelaufener Session waehrend Offline-Betrieb definieren
- [ ] Health-/Connectivity-Endpunkt des Backends definieren; `navigator.onLine` allein ist nicht ausreichend
- [ ] Frontend-/API-Versionierung und Kompatibilitaetsregeln definieren
- [ ] Technische Update-Erkennung fuer die PWA festlegen, zum Beispiel Versionsressource plus Service-Worker-Lebenszyklus
- [ ] Verhalten bei neuer App-Version und offenen `PENDING_SYNC`-Vorgaengen im Detail festlegen
- [ ] Browser Background Sync nur als optionale Optimierung pruefen; Zuverlaessigkeit darf nicht davon abhaengen
- [ ] Installations-/Rollout-Konzept fuer verwaltete Android-Geraete festlegen
