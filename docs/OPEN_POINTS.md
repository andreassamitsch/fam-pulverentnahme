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
- [x] Chargen-QR-getriebene Quellenwahl umgesetzt: der Bediener scannt `Artikel+++Charge`; die PWA ermittelt die positiven Oxaion-Bestandspositionen ueber die bestaetigten Auskunftswege. Bei genau einer Position wird der Entnahmeort automatisch uebernommen, bei mehreren Positionen muss der tatsaechliche Lagerort beziehungsweise Lagerplatz bestaetigt werden. Die serverseitige Pre-Write-Revalidierung bleibt unveraendert verbindlich.
- [x] Mehrfachscan derselben Charge fachlich auf die exakte Oxaion-Bestandsposition korrigiert: dieselbe Chargennummer darf mehrfach verwendet werden, wenn Lagerort/Lagerplatz unterschiedlich sind. Bereits verwendete exakte Positionen werden beim Folgescan ausgeblendet; `SourceStockService.ValidateSourcesAsync` blockiert weiterhin doppelte exakte Positionen serverseitig.
- [ ] Den Mehrfachscan derselben realen Charge auf zwei Lagerplaetzen im aktuellen Android-STAGING-Stand live bis zur Buchungsfreigabe und - sofern sinnvoll - mit realer Multi-Position-Buchung bestaetigen.
- [x] Sachmerkmals-Datenstrom fuer die Artikel-Erkennungsfarben bestaetigt: `US17000J *SAVKEY` -> `US17000J *PROPERTY` -> `US21001J *LOAD` -> `US21000R *GETHDR` -> `US21000R *FIRSTLIST`. Fuer `RP.00010` liefert `UYASMP.ASSMMN/ASSMMA` die Werte `EFA01=0D0D0D` und `EFA02=7030A0`; `_INTERN.SMMABZ` liefert `Schwarz` und `Violett`. Details siehe `docs/OXAION_ARTICLE_RECOGNITION_COLORS.md`.
- [x] Backend-Sachmerkmalsabruf fuer `EFA01/EFA02` am 03.09.2026 live aus der PWA bestaetigt; das zweigeteilte Erkennungsfarbfeld wird nach dem Tankscan korrekt angezeigt. Der aktuelle sichere Ablauf akzeptiert nur einen vollstaendigen `US21000R *FIRSTLIST` mit `<STOP/>`; fuer einen kuenftigen paginierten Sachmerkmalsfall wird ohne realen Mitschnitt kein `*NEXTLIST` erfunden.
- [x] Fehlerursache `LAP1258` fuer den Referenzfall `H04HRL` geklaert: Oxaion erwartet als `PSLAPL` den internen Schluessel `RE1F3`; eine visuell formatierte Eingabe wie `RE1  F 3` darf nicht als Buchungsschluessel verwendet werden.
- [x] Lagerorte ohne Lagerplatzorganisation technisch erkannt: `LAG1626` ist im Datenstrom bestaetigt. Nur fuer diesen eindeutigen Fall bleibt der Lagerplatz leer und der bestaetigte `LB30230R`-Lagerortbestand wird verwendet.
- [x] Neue Nachfuellquellen-Auswahl in STAGING live bestaetigt: Oxaion-geführte Lagerort-/Lagerplatz-/Chargenauswahl funktioniert im realen STAGING-Test.
- [x] Personalpruefung aus realen JET-Datenstroemen rekonstruiert: fuer die WebApp sind `PEPENU` als Personalnummer und `PEPENA` als vollstaendiger Name bestaetigt. Zusaetzlich ist der feldbezogene exakte Filterweg `US14001R *GETFILTER` -> `US14001 *SAVCURSET` -> `US14001R *GETSLTV/*GETSLTATR/*CHKSLTV` mit `IPENU=0000000450` -> `US14090J *FIRSTLIST` mit `FROM_PGMN=MAINFILTER` bestaetigt. `PESAKZ` und `PENLAE` werden nicht verwendet. Details siehe `docs/OXAION_PERSONNEL_LOOKUP.md`.
- [x] Neue AJAX-Personalsuche in STAGING live bestaetigt: Eingabe `45` liefert nur normalisierte `PEPENU` mit Praefix `45`; die Anzeige verwendet `PEPENU - PEPENA`. Treffer ueber Kostenstelle/andere Felder sowie `PESAKZ`/`PENLAE` werden nicht fuer die Anzeige verwendet.
- [x] Exakte erneute serverseitige Personalpruefung ueber `IPENU` vor einer Buchung am 03.09.2026 live in STAGING bestaetigt. Der bekannte Sonderfall bei `US14001 *SAVCURSET` bleibt bestehen: nach erfolgreichem HTTP-Aufruf kann die Antwort nicht als XML parsebar sein. Der Backend-Fix toleriert nur diesen spezifischen XML-Parsefehler bei `SAVCURSET`; danach muessen `GETSLTV`, `GETSLTATR`, `CHKSLTV`, `FIRSTLIST` und der exakte `PEPENU`/`PEPENA`-Abgleich erfolgreich sein. Transport-/HTTP-Fehler sowie alle Fehler in den nachfolgenden Pruefschritten bleiben sperrend. Die native Oxaion-Syntax fuer einen Praefixfilter direkt in `IPENU` ist weiterhin nicht bestaetigt und wird nicht erfunden.
- [x] Syncos-Zuordnung fuer NFC-Personal fachlich festgelegt: `ITSUSER.RFID` ist die alphanumerische Chip-ID und `ITSUSER.ObjectKey` die zehnstellige Personalnummer mit fuehrenden Nullen; Beispiel `RFID 54320466 -> ObjectKey 0000000446 -> Personalnummer 446`. Reader-Trennzeichen wie `:`, `-` oder Leerzeichen werden entfernt, der RFID-Inhalt wird aber weder numerisch noch als Hex-Wert interpretiert. Nur `ClassID=47`, `IsEnabled=-1`, `IsVisible=-1` werden akzeptiert. Details siehe `docs/NFC_PERSONNEL_LOOKUP.md`.
- [x] NFC-Personalidentifikation am 03.09.2026 live im eingesetzten Android-/Web-NFC-Testaufbau bestaetigt: Personalchip wird gelesen, gegen Syncos aufgeloest, anschliessend ueber den bestaetigten Oxaion-`IPENU`-Ablauf bestaetigt und im Frontend automatisch ausgewaehlt.
- [x] Manueller Login-Fallback mit Personalnummer und SYNCOS-Passwort am 04.09.2026 im aktuellen gefuehrten Android-Teststand live bestaetigt.
- [ ] NFC-Login im kombinierten Stand mit direkt gesetzter Backend-Personal-Session nach der bereits bestaetigten RFID-/Oxaion-Pruefung nochmals live bestaetigen.
- [ ] Zusaetzlich mindestens einen realen Syncos-RFID-Wert mit Buchstaben `A-F` im End-to-End-Test bestaetigen; die Implementierung behandelt RFID bereits verbindlich als alphanumerischen String ohne Hex-Konvertierung.
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
- [x] Die Sachmerkmale `EFA01` und `EFA02` des aus dem Maschinentank abgeleiteten Artikels werden als visuelle Erkennungshilfe angezeigt: quadratisches Farbfeld, linke Haelfte `EFA01`, rechte Haelfte `EFA02`; Farbbezeichnung und HEX-Wert werden zusaetzlich als Text ausgegeben. Die Farben sind keine Buchungsschluessel und ihr Ausfall blockiert keine ansonsten valide Materialbuchung.
- [x] Der aktuelle Maschinen-Tanklagerort darf nie als Quelllager einer Nachfuellcharge verwendet werden.
- [x] Mitarbeiter-Anmeldung: NFC ist der bevorzugte Loginweg. Nach erfolgreicher Syncos-RFID-Zuordnung und exakter Oxaion-Personalpruefung setzt das Backend direkt die Personal-Session. Fallback ist Personalnummer-Auswahl in Oxaion plus SYNCOS-Passwortpruefung. Beide Wege fuehren in dieselbe serverseitige Session; Details siehe `docs/PERSONNEL_AUTHENTICATION.md`.
- [x] Syncos-RFID wird als alphanumerischer String behandelt. Die Web-NFC-/Reader-Darstellung darf lediglich von Trennzeichen wie `:`, `-` oder Leerzeichen bereinigt werden; keine Dezimal-, Hex- oder Byte-Reihenfolgen-Konvertierung.
- [x] Bei NFC-Chiperkennung erzeugt die PWA unmittelbar ein kurzes Tonsignal ueber die Browser-WebAudio-API; es wird keine Audio-Datei benoetigt. Das Tonsignal bestaetigt nur die physische NFC-Erkennung, nicht bereits die erfolgreiche Syncos-/Oxaion-Zuordnung.
- [x] Maschinentankwahl im aktuellen STAGING-Nachfuellprozess erfolgt per QR. Der Tank-QR enthaelt ausschliesslich den Oxaion-Tanklagerort, z. B. `EOS1`, und muss gegen die gepflegte Maschinen-/Tankliste validiert werden. Die sichtbare manuelle Tankauswahl wurde aus dem normalen Ablauf entfernt.
- [x] Chargen-QR im aktuellen Nachfuellprozess hat das Format `Artikel+++Charge`. Lagerort und interner Lagerplatz werden daraus nicht frei abgeleitet, sondern aus den aktuellen positiven Oxaion-Bestandspositionen ermittelt. Bei Mehrdeutigkeit bestaetigt der Bediener den tatsaechlichen Entnahmeort.
- [x] Dieselbe Chargennummer darf in einem Nachfuellvorgang mehrfach gescannt werden, wenn sie auf unterschiedlichen positiven Oxaion-Bestandspositionen liegt. Duplicate Prevention bezieht sich auf die exakte Kombination Lagerort/Lagerplatz/Charge, nicht allein auf die Chargennummer.
- [x] Ein falscher beziehungsweise nicht zulaessiger Chargenscan wird nicht als Nachfuellcharge angelegt. Stattdessen erscheint eine seitendeckende Meldung mit Soll-Farbe und Scanwert, die bewusst mit `Verstanden` bestaetigt werden muss; der Fehlscan wird als separates WebApp-Auditereignis protokolliert.
- [x] QR-Kamera und QR-Erkennung sind bewusst getrennt: Scanner-Dialog oeffnet zuerst nur das Kamerabild und erlaubt das Einstellen des Zooms; `BarcodeDetector` und Scan-Laser starten erst nach bewusstem Druck auf `Scannen`. Damit werden Fehlscans beim Oeffnen/Ausrichten reduziert.
- [x] Der Kamera-Zoom ist eine dauerhafte lokale UI-/Geraetepraeferenz und wird fuer denselben Browser-Origin ueber Buchungsvorgaenge und App-Neustarts hinweg wiederhergestellt, soweit die Kamera Zoom unterstuetzt. Er ist kein fachlicher Zustand.
- [x] Beim Chargenscan bleibt die EFA01/EFA02-Sollfarbe sichtbar; nach QR-Erkennung werden Sollartikel und gescannter Artikel/Charge direkt gegenuebergestellt. Die Farbe bleibt reine visuelle Erkennungshilfe.
- [x] Mitarbeiteransicht und Dev-Ansicht verwenden dieselbe PWA. `Dev-Infos` aendert nur die Sichtbarkeit technischer Informationen und niemals fachliche Freigaben oder Backend-Pruefungen. Der jeweils naechste Bedienerschritt wird optisch hervorgehoben; Details siehe `docs/WORKER_GUIDED_UI.md`.
- [x] Nach dem Tankscan zeigt die Mitarbeiteransicht bekannte positive Lagerorte und ggf. Lagerplaetze mit passendem Pulver als reine Suchhilfe. Die tatsaechliche Buchungsquelle wird weiterhin erst aus dem Chargen-QR und dem aktuellen Oxaion-Bestand bestimmt und vor dem Schreiben erneut validiert.
- [x] Einfuellmengen fuer Nachfuellchargen werden niemals vorbelegt. Jede Menge muss vom Mitarbeiter bewusst eingegeben werden. Eine weitere Nachfuellcharge wird im gefuehrten Ablauf erst freigegeben, wenn die aktuelle Charge inklusive Entnahmeort und Menge vollstaendig ist. Der Button fuer die weitere Charge steht unterhalb der bereits erfassten Charge(n).
- [ ] Produktionsmaschinen-ID aus dem Fertigungsauftrag, z. B. `EP-M650-1`, verbindlich einem Maschinentank/Oxaion-Lagerort zuordnen. Dabei muss eine kurzfristige Aenderung der tatsaechlichen Produktionsmaschine weiterhin sicher moeglich sein. Eine Zuordnung wird bis zur Bestaetigung nicht erfunden.
- [ ] Pruefen, ob der Soll-Entnahmelagerort der Materialposition im Fertigungsauftrag eine geeignete und bei kurzfristiger Maschinenumplanung korrekt aenderbare Datenquelle fuer die Tankvorgabe ist.
- [ ] Online-Beauftragungsprozess fuer `Tank nachfuellen` und `Tank wechseln` durch Produktionsleitung beziehungsweise Stellvertretung definieren. Bis dahin gilt der bestehende organisatorische Uebergangsprozess mit muendlicher beziehungsweise papierbasierter Beauftragung; der alte vierteilige Entnahmeschein-QR ist fuer die neue Online-PWA keine verbindliche Buchungswahrheit.
- [ ] Kompatibilitaetsregeln fuer vorhandenes Pulver und Mix-Chargen ueber die aktuelle Artikelgleichheit hinaus festlegen
- [ ] Reihenfolge, Atomaritaet und Verhalten bei Teilfehlern des Pulverwechsels festlegen

## Anwendung und Betrieb

- [x] Mitarbeiter-Authentifizierung fuer den aktuellen PWA-Ablauf festgelegt: bevorzugt NFC, alternativ Personalnummer + SYNCOS-Passwort, serverseitige Session und zusaetzliche exakte Oxaion-Personalrevalidierung vor der Materialbuchung. Produktive HTTPS-/Rolloutdetails bleiben separat offen.
- [ ] Endgueltigen produktiven Web-/Application-Server festlegen
- [ ] Sichere Bereitstellung der Oxaion-Zugangsdaten und sonstigen Laufzeit-Secrets final festlegen; STAGING-Prototyp fragt den Oxaion-Benutzer und das Passwort beim Serverstart ab und uebergibt beide als `Oxaion__User` / `Oxaion__Password` an den Backend-Prozess. Es gibt keinen fest vorgegebenen Oxaion-Laufzeitbenutzer im Repository.
- [ ] Produktive Bereitstellung der Syncos-SQL-Verbindung fuer RFID- und Passwort-Lookup festlegen. Der STAGING-Prototyp verwendet denselben zur Laufzeit eingegebenen Connection String fuer `Syncos__ConnectionString` und `PersonnelAuthentication__ConnectionString`; der Connection String steht weder im Frontend noch im Repository.
- [ ] Persistenztechnik fuer produktives Transaktionslog, Idempotenz, Status und Audit Trail festlegen; STAGING-Prototyp verwendet vorerst JSON-Dateien unter `App_Data/transactions`. Abgelehnte Chargenscans werden vorerst separat unter `App_Data/scan-events` protokolliert.
- [ ] Aufbewahrungsdauer, produktiver Speicherort, Zugriffsrechte und Auswertung fuer das Fehlscan-Audit festlegen.
- [ ] Eindeutigkeitsbedingungen und Aufbewahrungszeit fuer Idempotenzdaten festlegen
- [ ] Timeoutwerte und Retry-Policy nach weiterer Analyse der Oxaion-Schnittstelle festlegen
- [ ] Datenschutz-, Berechtigungs- und Aufbewahrungskonzept fuer Protokolldaten festlegen
- [ ] Concurrency-/Sperrstrategie fuer den Zeitraum zwischen erfolgreicher Maschinen-/Quellbestands-Revalidierung und erster schreibender Oxaion-Materialbuchung festlegen; die aktuelle STAGING-Pruefung blockiert erkannte Aenderungen, ist aber noch keine atomare Reservierung

## PWA, Offline und Synchronisation

Die grundsaetzliche Entscheidung fuer PWA, Service Worker, IndexedDB, lokale Outbox, `clientOperationId`, serverseitige Revalidierung und kontrollierte Updates ist in `docs/OFFLINE_PWA.md` dokumentiert. Der aktuelle STAGING-Prototyp verwendet bereits `IndexedDB` fuer einen offenen `clientOperationId`-Vorgang und das Backend behandelt diese ID idempotent. Offen sind weiterhin die konkreten Betriebsparameter und Prozessgrenzen:

- [x] STAGING-PWA mit installierbarem Web App Manifest, lokalen 192x192-/512x512-App-Icons, maskierbarem Android-Icon, Favicon und Service-Worker-App-Shell konfiguriert; fuer Installation auf realen Android-Geraeten bleibt HTTPS am IIS/Reverse Proxy Voraussetzung.
- [x] Vorhandener FAM-Personalchip auf dem aktuell eingesetzten Android-/Browser-Testaufbau ueber Web NFC lesbar und Ende-zu-Ende fuer die Personalauswahl bestaetigt. Ohne sicheren HTTPS-Kontext oder ohne lesbare UID bleibt der manuelle Login mit Personalnummer und Passwort verfuegbar.
- [x] Kamera-QR-Scanner fuer den aktuellen STAGING-Ablauf ohne externe Bibliotheken umgesetzt: `getUserMedia`, native `BarcodeDetector`-QR-Erkennung, sichtbarer Kameraausschnitt, Kamera-Zoom soweit vom Geraet unterstuetzt sowie browsergeneriertes Tonsignal/Vibration bei Erkennung. Die QR-Erkennung startet erst nach bewusstem Druck auf `Scannen`; reines Oeffnen/Ausrichten/Zoomen erkennt noch keinen Code.
- [x] Kamera-Zoom wird als reine nicht-fachliche UI-Praeferenz lokal gespeichert und beim naechsten Kameraoeffnen wieder angewendet; fachliche PWA-Daten bleiben davon getrennt in IndexedDB.
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
