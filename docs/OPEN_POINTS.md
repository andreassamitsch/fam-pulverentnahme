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
