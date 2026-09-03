# Offene Punkte

Diese Checkliste wird waehrend des Projekts laufend aktualisiert. Offene Details duerfen nicht erfunden oder ohne Bestaetigung als Implementierungsgrundlage verwendet werden.

## Oxaion-Integration

- [x] HTTP-Buchungsfolge fuer den getesteten Vorgang `alte Mix-Charge + neue Pulvercharge -> neue Mix-Charge` bestaetigt und im STAGING-Prototyp umgesetzt; Details siehe `docs/STAGING_REAL_MIX_PROTOTYPE.md`
- [x] JET-Datenstrom fuer `Chargen pro Lagerort` identifiziert und im Backend als lesender STAGING-Prototyp umgesetzt; Programme/Felder siehe `docs/OXAION_MACHINE_STOCK_LOOKUP.md`
- [x] Filterbedingung technisch aus der Selektionsmaske bestaetigt: `LLAWEP.LALABE <> 0`. Der Backend-Prototyp haengt nicht mehr von einem gespeicherten Filter `mit Bestand`, dessen Freigabe oder Filter-ID ab, sondern liest die vollstaendige Lagerortliste und wertet diese Bedingung direkt aus.
- [x] Artikelunabhaengige Sicht auf den Maschinen-Lagerort im Referenzdatenstrom bestaetigt: die ungefilterte `LB30230R *FIRSTLIST` fuer `EOS1` lieferte 25 Zeilen verschiedener Artikel inklusive Nullbestaenden und `<STOP/>`. Dadurch koennen `Maschine leer`, `anderes Pulver vorhanden` und `mehrere Bestaende` unterschieden werden.
- [x] Serverseitiger Start der Bestandsabfrage mit leerer Eltern-`SSID` in STAGING live bestaetigt: der vom Backend gestartete Lesefluss ermittelt den aktuellen EOS1-Bestand erfolgreich.
- [ ] Maschinengetriebene Artikelableitung live bestaetigen: der neue Ablauf startet dieselbe `LB30230R`-Lagerortauskunft ohne vorgegebenen Artikel (`I_TIDF/TIDF` leer) und leitet Artikel/Bezeichnung aus der einzigen positiven Tankposition ab. Der bisherige Referenzdatenstrom zeigt bereits, dass die Liste selbst artikelunabhaengig ist; der leere Artikel im Startkontext ist noch einmal real in STAGING zu pruefen.
- [x] Mehrere Nachfuellchargen als ein fachlicher Vorgang im Backend/Frontend umgesetzt; Positions-, Verifikations- und Recovery-Logik sind dynamisch. Details siehe `docs/MULTI_BATCH_REPLENISHMENT.md`.
- [ ] Multi-Batch-Verallgemeinerung fuer Position 3+ real in Oxaion STAGING bestaetigen: mindestens zwei zusaetzliche Nachfuellchargen in einem Vorgang buchen und alle erwarteten LM/LN-Bewegungen pruefen. Position 2 ist bereits praktisch bestaetigt.
- [x] Nachfuellquellen-Auswahl aus Oxaion technisch bestaetigt: `LB30340R` liefert Lagerorte/Chargen pro Artikel, `LB30430R` liefert exakte interne Lagerplatzschluessel/Chargen/Bestaende; Lagerorttexte werden ueber `US00006J *GETPLAIN` gelesen. Details siehe `docs/OXAION_SOURCE_STOCK_LOOKUP.md`.
- [x] Fehlerursache `LAP1258` fuer den Referenzfall `H04HRL` geklaert: Oxaion erwartet als `PSLAPL` den internen Schluessel `RE1F3`; eine visuell formatierte Eingabe wie `RE1  F 3` darf nicht als Buchungsschluessel verwendet werden.
- [x] Lagerorte ohne Lagerplatzorganisation technisch erkannt: `LAG1626` ist im Datenstrom bestaetigt. Nur fuer diesen eindeutigen Fall bleibt der Lagerplatz leer und der bestaetigte `LB30230R`-Lagerortbestand wird verwendet.
- [x] Neue Nachfuellquellen-Auswahl in STAGING live bestaetigt: Oxaion-geführte Lagerort-/Lagerplatz-/Chargenauswahl funktioniert im realen STAGING-Test.
- [x] Personalpruefung aus realen JET-Datenstroemen rekonstruiert: fuer die WebApp sind `PEPENU` als Personalnummer und `PEPENA` als vollstaendiger Name bestaetigt. Zusaetzlich ist der feldbezogene exakte Filterweg `US14001R *GETFILTER` -> `US14001 *SAVCURSET` -> `US14001R *GETSLTV/*GETSLTATR/*CHKSLTV` mit `IPENU=0000000450` -> `US14090J *FIRSTLIST` mit `FROM_PGMN=MAINFILTER` bestaetigt. `PESAKZ` und `PENLAE` werden nicht verwendet. Details siehe `docs/OXAION_PERSONNEL_LOOKUP.md`.
- [x] Neue AJAX-Personalsuche in STAGING live bestaetigt: Eingabe `45` liefert nur normalisierte `PEPENU` mit Praefix `45`; die Anzeige verwendet `PEPENU - PEPENA`. Treffer ueber Kostenstelle/andere Felder sowie `PESAKZ`/`PENLAE` werden nicht fuer die Anzeige verwendet.
- [ ] Exakte erneute serverseitige Personalpruefung ueber `IPENU` vor einer Buchung live bestaetigen. Live-Test vom 03.09.2026: `US14001 *SAVCURSET` antwortete nach erfolgreichem HTTP-Aufruf nicht als parsebares XML und blockierte dadurch die Pruefung. Der Backend-Fix toleriert nur diesen spezifischen XML-Parsefehler bei `SAVCURSET`; Transport-/HTTP-Fehler sowie Fehler in `GETSLTV`, `GETSLTATR`, `CHKSLTV`, `FIRSTLIST` oder beim exakten `PEPENU`/`PEPENA`-Abgleich bleiben sperrend. Im naechsten STAGING-Test den kompletten Ablauf bis zur erfolgreichen Vorbuchungspruefung bestaetigen. Die native Oxaion-Syntax fuer einen Praefixfilter direkt in `IPENU` ist weiterhin nicht bestaetigt und wird nicht erfunden.
- [ ] Sperrfreigabe nach erfolgreicher Abschlussverifikation live bestaetigen: am 02.09.2026 blieb der von der App erfolgreich gebuchte Lagerbeleg nach dem erneuten `*OPEN` zur Verifikation gesperrt. Der Backend-Fix sendet nach der finalen `FIRSTLIST`-Pruefung ein zweites explizites `LB20100J *END` und setzt erst danach `SUCCESS`. Im naechsten STAGING-Test pruefen, dass der Beleg unmittelbar danach in Oxaion nicht mehr gesperrt ist.
- [ ] Konkrete Oxaion HTTP-Aufrufe fuer die noch fehlenden Materialbuchungen identifizieren, insbesondere FA-Materialrueckmeldung und Pulverwechsel/Ruecklagerung
- [ ] Konkretes Oxaion BDE-/PPS-Programm fuer die spaetere FA-Materialrueckmeldung identifizieren
- [ ] Oxaion Buchungsschluessel Maschinenlager -> Pulverlager fuer den Pulverwechsel ermitteln
- [ ] Oxaion Buchungsschluessel Pulverlager -> Maschine fuer noch nicht durch den bestaetigten Mix-Ablauf abgedeckte Faelle ermitteln
- [x] Chargenumbuchung fuer den getesteten Nachfuell-/Mix-Vorgang mit `LM` und automatisch erzeugtem `LN` bestaetigt
- [x] Geeignete WebApp-/Backend-Transaktionsreferenz fuer den STAGING-Prototyp in den vorhandenen Oxaion-Freitextfeldern dokumentiert; finale produktive Referenz-/Suchstrategie noch bewerten
- [x] Belastbare Ergebnisabfrage fuer den getesteten Mix-Beleg ueber erneutes Oeffnen und `LB20110R *FIRSTLIST` umgesetzt; fuer andere Buchungsarten weiterhin offen
- [x] Bewusster neuer Versuch nach eindeutigem `REJECTED` umgesetzt: neue `clientOperationId`, identische Buchungsdaten und Verknuepfung ueber `retryOfClientOperationId`; kein Retry derselben abgelehnten Transaktion

## Fachliche Entscheidungen

- [x] Mix-Chargenschema fuer neue Mix-Chargen festgelegt: `<Artikel ohne Punkt>MIX_<yyyyMMdd>_<HHmmss>`, z. B. `RP00010MIX_20260902_162312`. Die Nummer wird automatisch erzeugt und nicht frei editiert.
- [x] Nachfuell-Buchungstext festgelegt: `Pulver nachfuellen <Maschinen-Lagerort>`, dynamisch aus der ausgewaehlten Maschine.
- [x] Buchungsdatum beim neuen Nachfuellvorgang ist immer das aktuelle Datum und keine Bedienereingabe; `Mix Charge erstellt am` wird aus dem aktuellen Erzeugungszeitpunkt der neuen Mix-Charge gebildet.
- [x] Artikel und Artikelbezeichnung werden beim Nachfuellen aus dem eindeutigen aktuellen Maschinenbestand abgeleitet und sind keine Bedienereingaben.
- [x] Der aktuelle Maschinen-Tanklagerort darf nie als Quelllager einer Nachfuellcharge verwendet werden.
- [ ] Kompatibilitaetsregeln fuer vorhandenes Pulver und Mix-Chargen ueber die aktuelle Artikelgleichheit hinaus festlegen
- [ ] Maschinenliste/QR-Zuordnung finalisieren. Der aktuelle STAGING-Stand verwendet eine gepflegte Maschinenlager-Whitelist mit `EOS1` und `EOS2`; der spaetere Maschinen-QR soll gegen dieselbe Liste validiert werden.
- [ ] Reihenfolge, Atomaritaet und Verhalten bei Teilfehlern des Pulverwechsels festlegen

## Anwendung und Betrieb

- [ ] Authentifizierungskonzept fuer Benutzer der WebApp festlegen
- [ ] Endgueltigen produktiven Web-/Application-Server festlegen
- [ ] Sichere Bereitstellung der Oxaion-Zugangsdaten und sonstigen Laufzeit-Secrets final festlegen; STAGING-Prototyp fragt den Oxaion-Benutzer und das Passwort beim Serverstart ab und uebergibt beide als `Oxaion__User` / `Oxaion__Password` an den Backend-Prozess. Es gibt keinen fest vorgegebenen Oxaion-Laufzeitbenutzer im Repository.
- [ ] Persistenztechnik fuer produktives Transaktionslog, Idempotenz, Status und Audit Trail festlegen; STAGING-Prototyp verwendet vorerst JSON-Dateien unter `App_Data/transactions`
- [ ] Eindeutigkeitsbedingungen und Aufbewahrungszeit fuer Idempotenzdaten festlegen
- [ ] Timeoutwerte und Retry-Policy nach weiterer Analyse der Oxaion-Schnittstelle festlegen
- [ ] Datenschutz-, Berechtigungs- und Aufbewahrungskonzept fuer Protokolldaten festlegen
- [ ] Concurrency-/Sperrstrategie fuer den Zeitraum zwischen erfolgreicher Maschinen-/Quellbestands-Revalidierung und erster schreibender Oxaion-Materialbuchung festlegen; die aktuelle STAGING-Pruefung blockiert erkannte Aenderungen, ist aber noch keine atomare Reservierung

## PWA, Offline und Synchronisation

Die grundsaetzliche Entscheidung fuer PWA, Service Worker, IndexedDB, lokale Outbox, `clientOperationId`, serverseitige Revalidierung und kontrollierte Updates ist in `docs/OFFLINE_PWA.md` dokumentiert. Der aktuelle STAGING-Prototyp verwendet bereits `IndexedDB` fuer einen offenen `clientOperationId`-Vorgang und das Backend behandelt diese ID idempotent. Offen sind weiterhin die konkreten Betriebsparameter und Prozessgrenzen:

- [x] STAGING-PWA mit installierbarem Web App Manifest, lokalen 192x192-/512x512-App-Icons, maskierbarem Android-Icon, Favicon und Service-Worker-App-Shell konfiguriert; fuer Installation auf realen Android-Geraeten bleibt HTTPS am IIS/Reverse Proxy Voraussetzung.
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
