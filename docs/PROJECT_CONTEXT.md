# FAM Pulverentnahme / Pulverwechsel

## Ziel

Entwicklung einer mobilen WebApp fuer die Produktion zur sicheren, nachvollziehbaren und moeglichst einfachen Durchfuehrung von Pulverentnahmen, Pulvernachfuellungen und Pulverwechseln.

Die WebApp soll mit Oxaion kommunizieren. Das Frontend bleibt bewusst einfach. Komplexe Pruef-, Buchungs- und Fehlerlogik liegt im Backend beziehungsweise in der vorhandenen Oxaion-Fachlogik.

## Systemarchitektur

### Vorhandene Umgebung

- Die Oxaion-Applikation laeuft auf einem eigenen Server.
- Die Oxaion-Datenbank laeuft auf einem separaten Datenbankserver.
- Auf dem Datenbankserver befinden sich aktuell auch SSRS Reporting Services und IIS.
- Fuer Tests kann die WebApp auf dem vorhandenen IIS betrieben werden.

### Verbindlicher Serverbetrieb ab 02.10.2026

- Die ASP.NET-Core-Anwendung wird als Windows-Dienst `FAMPulverentnahme` installiert und automatisch gestartet.
- Der eigentliche Webdienst lauscht nur auf `127.0.0.1:5080`; IIS bleibt der externe HTTPS-Reverse-Proxy zur PWA.
- Die lokale Administrationsoberflaeche lauscht getrennt auf `127.0.0.1:5081` und wird nicht ueber IIS veroeffentlicht.
- Installation und Updates erfolgen per MSI. Ein vorhandener Dienst wird beim Upgrade gestoppt und nach erfolgreicher Installation wieder gestartet.
- Der vorhandene IIS-/HTTPS-Auftritt kann damit auf denselben Loopback-Port 5080 zeigen; die MSI richtet bewusst keinen oeffentlichen zweiten Webzugang ein.

Die WebApp schreibt nicht direkt auf die Oxaion-Datenbank.

Grundsaetzlicher Kommunikationsweg fuer ERP-Fachlogik und Buchungen:

```text
Android Webbrowser / PWA
  -> HTML/JavaScript Frontend
  -> ASP.NET Core Backend
  -> Oxaion HTTP-Schnittstelle
  -> Oxaion Fachlogik
```

Fuer Materialbuchungen soll nach Moeglichkeit die vorhandene Oxaion BDE-/PPS-Logik ueber die HTTP-Schnittstelle verwendet werden.

### Rein lesende Oxaion-SQL-Lagerbestandsansicht

Fuer die allgemeine Informationsansicht der RP.*- und PB.*-Chargenbestaende ist ab 08.09.2026 ein direkter **rein lesender** SQL-Zugriff des Backends auf die Oxaion-Datenbank freigegeben. Diese Ausnahme gilt nur fuer die in `docs/INVENTORY_VIEW.md` dokumentierte Bestandsabfrage und ist keine Freigabe fuer ERP-Buchungen per SQL.

Verbindlich ab 02.10.2026:

- Fuer Syncos und den rein lesenden Oxaion-SQL-Zugriff wird **eine gemeinsame native SQL-Anmeldung** (Server, Benutzer, Passwort) verwendet. Der SQL-Benutzer erhaelt serverseitig nur die fuer die WebApp benoetigten Rechte.
- Die Umgebung bestimmt die verwendeten Datenbanken. Syncos ist mit `syncos_stg_102` fuer STAGING und `syncos_prd_102` fuer PRODUCTION vorbelegt.
- Die exakten Oxaion-SQL-Katalognamen fuer STAGING und PRODUCTION werden in der lokalen Serverkonfiguration gepflegt; sie werden nicht erfunden oder hart codiert.
- Die Oxaion-HTTP-Umgebung wird mit demselben STAGING/PRODUCTION-Schalter umgeschaltet: bestaetigter STAGING-Port `11118`, bestaetigter PRODUCTION-Port `11108`, Firma weiterhin `103`.
- SQL-Passwort sowie Oxaion-Benutzer/-Passwort werden in `%ProgramData%\FAM-Pulverentnahme\service-config.json` nur DPAPI-verschluesselt (LocalMachine) gespeichert. Klartext-Secrets stehen weder im Frontend noch im Repository.
- Die Konfiguration erfolgt ausschliesslich lokal am Server ueber `http://127.0.0.1:5081/admin`.
- Ein Wechsel STAGING/PRODUCTION macht vorhandene Mitarbeiter-Sessions ungueltig. Transaktions- und Auditdateien werden nach Umgebung getrennt, damit STAGING-Vorgaenge nicht in PRODUCTION weiterverwendet werden.
- Der Bediener-Inaktivitaets-Timeout ist ab Version `0.1.5` maschinenweit in der lokalen Serverkonfiguration einstellbar (5 bis 1440 Minuten, Default 480). Nur echte Bedieneraktivitaet verlaengert die Frist; Hintergrund-/Health-Aufrufe nicht. Nach Ablauf wird die Personal-Session serverseitig verworfen und eine neue Anmeldung verlangt.
- Oxaion-SQL-Zugriff fuer die PWA bleibt auf dokumentierte `SELECT`-/Informationsfunktionen begrenzt.
- Materialbuchungen, Bestandskorrekturen und sonstige ERP-Aenderungen laufen weiterhin ausschliesslich ueber Oxaion-Fachlogik/HTTP.
- Eine Umlagerung aus der Lageruebersicht ist **keine SQL-Buchung**. Die SQL-Lageruebersicht liefert nur die angeklickte Ausgangsposition; die eigentliche Lagerplatz-Umlagerung wird als eigener serverseitiger Vorgang ueber den bereits bestaetigten Oxaion-`LF -> LE`-Materialbelegweg ausgefuehrt. Quelle, Menge und Ziel werden unmittelbar vor dem Schreiben erneut ueber bestaetigte Oxaion-HTTP-/F4-Lesewege validiert.
- Fuer die Lagerplatz-Umlagerung sind Maschinentank-Lagerorte als Ziel verboten. Die Sperre erfolgt serverseitig anhand derselben dynamischen Tankdefinition `ULGSTP.LGLGART = '02'`, die auch fuer die Tankprozesse gilt.

## PWA und Offline-Faehigkeit

Die mobile WebApp wird als Progressive Web App (PWA) geplant, damit sie auf Android-Smartphones wie eine installierte App genutzt und bei kurzen Netzwerkausfaellen kontrolliert weiterbedient werden kann.

Verbindliche Grundregeln:

- Service Worker fuer App-Shell und statische Ressourcen.
- `IndexedDB` fuer lokale fachliche Zwischenspeicherung und Outbox; kein `localStorage` fuer diese Daten.
- Browser-/Geraetespeicher ist nur ein Zwischenpuffer. Backend und Oxaion bleiben fuer produktive Buchungen und den aktuellen fachlichen Zustand fuehrend.
- Offline erfasste Vorgaenge duerfen niemals als erfolgreich gebucht dargestellt werden.
- Jeder offline angelegte Vorgang erhaelt eine eindeutige `clientOperationId`; das Backend ordnet diese eindeutig einer serverseitigen Transaktion zu beziehungsweise nutzt sie als Idempotency Key.
- Nach Wiederherstellung der Verbindung muss der aktuelle fachliche Zustand serverseitig erneut validiert werden, bevor eine produktive Oxaion-Buchung ausgeloest wird.
- Bei geaendertem oder nicht eindeutigem Zustand wird nicht automatisch ueberschrieben oder blind gebucht; der Vorgang geht in Konflikt beziehungsweise manuelle Klaerung.
- Eine neue PWA-Version darf nicht unkontrolliert mitten in einem laufenden Vorgang oder unter Verlust noch nicht synchronisierter Daten aktiviert werden.

Details stehen verbindlich in `docs/OFFLINE_PWA.md`.

## Bedien- und Diagnosekonfiguration ab 01.10.2026

- `Diagnose` und `Dev-Infos` sind im normalen Mitarbeiterbetrieb standardmaessig ausgeblendet.
- Im Windows-Dienstbetrieb erfolgt die Freigabe ausschliesslich ueber die lokale Serverkonfiguration auf `127.0.0.1:5081/admin`; der gespeicherte Wert wird serverseitig als `Prototype:DeveloperToolsEnabled` angewendet.
- Der Browser darf diese Freigabe nicht eigenmaechtig ueber Session-/LocalStorage aktivieren.
- Technische Oxaion-Buchungsschluessel werden in der normalen Bedienoberflaeche nicht als Arbeitsanweisung angezeigt; fachliche Aussagen und konkrete Bedienmassnahmen haben Vorrang.

## Hauptfunktionen fuer den Bediener

Der Bediener soll moeglichst wenige, klar verstaendliche Hauptfunktionen sehen. Aktuell festgelegt sind:

1. Pulver nachfuellen
2. Pulver tauschen

Die neuere Aufteilung in separate sichtbare Vorgaenge ist in `docs/SEPARATE_TANK_PROCESSES.md` verbindlich detailliert und ersetzt fuer den aktuellen Entwicklungsstand diese aeltere Zweier-Kurzliste.

## 1. Pulver nachfuellen / Pulver fuer Fertigungsauftrag vorbereiten

Der vorhandene QR-Code des Fertigungsauftrags enthaelt sinngemaess:

```text
Rohmaterialnummer
+++
Fertigungsauftragsnummer
+++
Maschinen-ID
```

Beispiel:

```text
RP.00010+++FA24FI00118+++EP-M650-1
```

`EP-M650-1` ist dabei die Produktionsmaschinen-ID und nicht der Oxaion-Lagerort des Maschinentanks. Die verbindliche Referenz zwischen Produktionsmaschinen-ID und Maschinentank-Lagerort ist noch offen und darf nicht erfunden werden.

Langfristig bleibt die Planmaschine aus dem Fertigungsauftrag relevant. Da sich die Produktionsmaschine kurzfristig aendern kann, muessen Planmaschine und tatsaechlich verwendete Maschine beziehungsweise der zugehoerige Maschinentank getrennt nachvollziehbar bleiben. Die WebApp darf organisatorisch nicht entscheiden, ob eine kurzfristige Maschinenabweichung erlaubt ist; sie muss aber den tatsaechlichen Tank eindeutig identifizieren und dessen Pulverzustand pruefen.

Bis der FA-getriebene Online-Auftrag und die Maschinen-ID-zu-Tank-Referenz verbindlich definiert sind, gilt fuer den aktuellen STAGING-Nachfuellprozess der organisatorische Uebergang mit muendlicher beziehungsweise papierbasierter Beauftragung. Der tatsaechlich zu befuellende Maschinentank wird physisch per Tank-QR gescannt.

### Maschinentankwahl und Pruefung des Maschinenbestands

Im Online-Fall wird vor dem Nachfuellen der aktuelle Oxaion-Bestand des tatsaechlich gescannten Maschinentanks ueber das Backend geprueft.

Fuer den aktuellen STAGING-Nachfuellprozess gilt verbindlich:

- Die sichtbare manuelle Auswahl des Maschinentanks ist im normalen Ablauf durch den QR-Scan ersetzt.
- Der Maschinentank-QR enthaelt ausschliesslich den Oxaion-Tanklagerort, zum Beispiel `EOS1`.
- Der gescannte Tanklagerort wird dynamisch gegen Oxaion `ULGSTP` validiert. Verbindliche Tankdefinition ist fuer die aktive Firma `LGLGART = '02'`; es gibt keine hart codierte `EOS1`/`EOS2`-Whitelist. Details und der bestaetigte PRD-Snapshot stehen in `docs/MACHINE_TANK_DEFINITION.md`.
- Ein Code mit `+++` ist in diesem Scan-Schritt kein gueltiger Maschinentank-QR.
- Der Pulverartikel wird beim Nachfuellen nicht manuell eingegeben. Er wird aus der einzigen positiven Bestandsposition des gescannten Maschinentanks abgeleitet.
- Artikelnummer und Artikelbezeichnung sind Systeminformationen und nicht editierbar.
- Auf einem Maschinentank wird fuer diesen Prozess genau ein positiver Pulverbestand erwartet. Mehrere positive Bestandspositionen sind nicht eindeutig und sperren die Buchung.
- Die aktuelle Mix-Charge und die komplette Tankmenge werden ebenfalls aus Oxaion uebernommen und nicht manuell eingegeben.
- Nach der Artikelableitung werden die Sachmerkmale `EFA01` und `EFA02` als zweigeteiltes Farbfeld dargestellt, damit der Mitarbeiter die passende Charge physisch leichter erkennt. Diese Farben sind eine Suchhilfe und keine Buchungswahrheit.

Ein komplett leerer Tank kann im Vorgang `Pulver nachfuellen` keinen Artikel liefern und wird deshalb nicht freigegeben. Bei eindeutigem Leerbestand zeigt die App `Tank ist leer.`; fuer die Befuellung ist der separate Vorgang `Neues Pulver in Tank fuellen` zu verwenden.

Details zu den QR-Formaten und der noch offenen Produktionsmaschinen-ID-zu-Tank-Zuordnung stehen in `docs/QR_CODE_WORKFLOW.md`.

### Dynamische Maschinentankdefinition ab 02.10.2026

Maschinentanks werden aus der aktiven Oxaion-SQL-Datenbank gelesen:

```sql
SELECT LGLAGO, LGBEZC
FROM OXAION.ULGSTP
WHERE LGFIRM = @firm
  AND LGLGART = N'02'
ORDER BY LGLAGO;
```

Damit folgt die Tankliste automatisch dem STAGING-/PRODUCTION-Schalter. Der am 02.10.2026 direkt bestaetigte PRD-Stand fuer Firma 103 enthaelt `EOS1`, `EOS2`, `M400-01`, `M400-02` und `M650`. Diese Werte sind ein Snapshot und werden nicht fest codiert.

Die gleiche dynamische Definition gilt fuer Tank-QR-Pruefung, Lageruebersicht und serverseitige Sicherheitspruefungen vor tankbezogenen Buchungen. Die PWA aktualisiert die Tankliste vor einem Tankscan erneut.

Details: `docs/MACHINE_TANK_DEFINITION.md`.

### Auswahl der Nachfuellquellen

Lagerort, Lagerplatz und Charge einer neuen Nachfuellmenge werden nicht frei als Buchungsschluessel eingegeben. Der Bediener scannt die physisch verwendete Charge; die gueltige Buchungsquelle wird danach aus dem aktuellen positiven Oxaion-Bestand ermittelt.

Chargen-QR im aktuellen STAGING-Ablauf:

```text
Artikel+++Charge
```

Beispiele:

```text
RP.00006+++88688
RP.00010+++RP00010MIX_20260903_132212
```

Verbindlich gilt:

- Die Artikelnummer aus dem Chargen-QR muss dem aus dem Maschinentank abgeleiteten Artikel entsprechen.
- Das Backend beziehungsweise die PWA nutzt die bestaetigte Oxaion-Auskunft `Chargen und Lagerorte pro Artikel`, um positive Quell-Lagerorte des Artikels zu bestimmen.
- Sobald der Tankartikel bekannt ist, darf die PWA die bekannten positiven Lagerorte und gegebenenfalls Lagerplaetze mit passendem Pulver als reine Such-/Weghilfe anzeigen. Diese Anzeige ersetzt niemals den Scan der tatsaechlich entnommenen Charge.
- Der aktuell gescannte Maschinentank darf nicht als Nachfuellquelle angeboten oder vom Backend akzeptiert werden.
- Fuer die moeglichen Lagerorte werden die dort vorhandenen positiven Lagerplatz-/Chargenpositionen aus `Lagerplaetze pro Artikel und -ort` gelesen.
- Es werden nur Positionen mit der exakt gescannten Charge beruecksichtigt.
- Gibt es genau eine eindeutige positive Oxaion-Bestandsposition fuer die gescannte Charge, werden Lagerort und gegebenenfalls interner Lagerplatz automatisch uebernommen.
- Gibt es mehrere Lagerorte oder Lagerplaetze fuer dieselbe Charge, muss der Bediener den tatsaechlich verwendeten Entnahmeort bewusst bestaetigen. Die WebApp darf nicht raten oder einen Treffer stillschweigend priorisieren.
- Fuer die Buchung wird immer der von Oxaion gelieferte interne Lagerplatzschluessel verwendet. Eine visuell formatierte Lagerplatzdarstellung darf nicht vom Bediener nachgebildet und als `PSLAPL` uebergeben werden.
- Der Referenzfall `H04HRL / RE1F3 / Charge 84671` bestaetigt, dass `RE1F3` der intern gueltige Buchungsschluessel ist.
- Meldet Oxaion eindeutig `LAG1626` (`Lagerort hat keine Lagerplatzorganisation`), bleibt der Lagerplatz leer; die Charge und der Bestand werden ueber den bestaetigten Ablauf `Chargen pro Lagerort` ermittelt. Es wird kein Lagerplatz erfunden.
- Die Einfuellmenge bleibt eine Bedienereingabe, darf jedoch den aktuell verfuegbaren Oxaion-Bestand der bestaetigten Bestandsposition nicht ueberschreiten.
- Eine Einfuellmenge wird niemals vorbelegt. Der Mitarbeiter muss die tatsaechlich verwendete Menge bewusst eingeben.
- Im gefuehrten Mitarbeiterablauf wird eine weitere Charge erst freigegeben, wenn die vorherige Charge inklusive Entnahmeort und Menge vollstaendig ist.
- Mehrere Nachfuellchargen duerfen in einem Vorgang verwendet werden. Dieselbe exakte Oxaion-Bestandsposition darf innerhalb eines Vorgangs nicht doppelt ausgewaehlt werden.
- Direkt vor der ersten schreibenden Oxaion-Materialbuchung validiert das Backend jede Nachfuellquelle erneut anhand von Artikel, Lagerort, internem Lagerplatzschluessel, Charge und verfuegbarer Menge. Bei Abweichung wird keine Materialbuchung gestartet.
- Neue verbindliche FAM-Entscheidung vom 29.09.2026: **I1 und I2 verwenden beide immer Geschaeftsbereich `PSWERK=21` und Kostenstelle `PSKSTL=5100`.** Die zuvor aus dem historischen STAGING-Mitschnitt uebernommene I2-Kombination 03/6000 ist damit fuer die WebApp abgeloest. Die WebApp setzt 21/5100 bereits in der Korrekturposition; eine Kostenstellen-F4-Auswahl wird dafuer nicht mehr als fachliche Quelle verwendet. Eine Vorbelegung `LBSKSB` im Buchungsschluessel ist nicht erforderlich und darf leer sein.
- Die am 29.09.2026 neu aufgezeichneten erfolgreichen I1-/I2-Buchungen zeigen, dass der **Dialogaufbau vor dem ersten `PUTNEW`** Teil der bestaetigten Sequenz ist. `LB20115J *F4`/Listen werden fuer Buchungsschluessel (`US50002R`), Lagerort (`US16601R`), Charge/Artikel (`US17402R`) und Kostenstelle (`US11001R`) durchlaufen; die Artikelbezeichnung kommt ueber `US00006J *GETPLAIN`. Erst danach wird `LB20115J *PUTNEW` gesendet. Die WebApp spielt diese lesenden Dialogschritte jetzt nach, statt nur einen vermeintlich vollstaendigen Endzustand direkt an `PUTNEW` zu senden.
- `TX_BWKZ`, `TX_IDNR`, `TX_LAGO` und `TX_KSTL` sind in diesem Ablauf die von Oxaion gelieferten Anzeige-/Bezeichnungstexte zu den eigentlichen Schluesseln und werden nicht hart codiert. `TX_B1SB01` ist trotz `TX_`-Praefix **kein Bezeichnungstext**, sondern der bestaetigte Dialog-/Richtungszustand (`2` fuer I2/Abgang, `1` fuer I1/Zugang). Verbindliche Schluessel bleiben u. a. `PSBWKZ`, `PSIDNR`, `PSLAGO`, `PSPONR`, `PSWERK=21`, `PSKSTL=5100` und `KEYTYPE=LKOPF`.
- Oxaion-Quellcodeanalyse vom 29.09.2026 erklaert den wiederholten `NullPointerException` in `entryChkIDNR04` exakt: Dort werden `TX_PCKMS`, `TX_PCKMM` und `TX_PCKMZ` geleert und unmittelbar deren interne Field-Werte per `getInternal().init()` verwendet. Oxaion erzeugt fuer diese Felder die zugehoerigen Spiegelwerte `I_TX_PCKMS`, `I_TX_PCKMM`, `I_TX_PCKMZ`. Der Korrektur-Request muss deshalb alle drei `TX_`-/`I_TX_`-Paare auch im leeren Zustand mitsenden; dies ist Dialogzustand, keine fachliche Packmittelangabe.

Technische Details stehen in `docs/OXAION_SOURCE_STOCK_LOOKUP.md`, `docs/QR_CODE_WORKFLOW.md` und `docs/WORKER_GUIDED_UI.md`.

### Mitarbeiter-Anmeldung

Fuer eine produktive Materialbuchung muss der handelnde Mitarbeiter in einer serverseitigen Personal-Session angemeldet sein.

Verbindlicher aktueller Ablauf:

- **NFC ist der bevorzugte Loginweg.**
- Web NFC liest den vorhandenen Personalchip.
- Reader-Trennzeichen wie `:`, `-` oder Leerzeichen werden aus der RFID-Darstellung entfernt.
- `ITSUSER.RFID` in Syncos wird als alphanumerischer String behandelt; es findet keine Dezimal-, Hex- oder Byte-Reihenfolgen-Konvertierung statt.
- Syncos `ObjectKey` liefert die zehnstellig mit fuehrenden Nullen gespeicherte Personalnummer.
- Danach wird dieselbe Personalnummer ueber den bestaetigten exakten Oxaion-`IPENU`-Ablauf geprueft.
- Erst nach erfolgreicher Syncos- und Oxaion-Pruefung setzt das Backend die Personal-Session.
- Bei physischer NFC-Erkennung erzeugt die PWA unmittelbar einen kurzen Ton ueber die Browser-WebAudio-API. Dieser Ton bestaetigt nur die Chiperkennung, nicht bereits die erfolgreiche Personalzuordnung.

Fallback ohne NFC:

- Personalnummer ohne fuehrende Nullen eingeben.
- Die AJAX-Suche behandelt die Eingabe als Praefix der normalisierten Oxaion-Personalnummer `PEPENU`.
- Die WebApp zeigt Treffer ausschliesslich als `PEPENU - PEPENA`; `PESAKZ` und `PENLAE` werden nicht als Namensquelle verwendet.
- Bediener waehlt den Mitarbeiter bewusst aus und gibt danach sein vorhandenes SYNCOS-Passwort ein.
- Die Passwortpruefung erfolgt ausschliesslich serverseitig nach der dokumentierten, durch Testvektoren bestaetigten Legacy-Logik.
- Bei erfolgreicher Passwortpruefung wird dieselbe Backend-Session gesetzt wie beim NFC-Login.

Vor dem ersten schreibenden Materialbuchungsaufruf prueft das Backend die angemeldete Personalnummer zusaetzlich erneut ueber den bestaetigten feldbezogenen Oxaion-Filter `IPENU` und vergleicht `PEPENU` und `PEPENA` mit dem Buchungsvorgang. Die Session ersetzt diese fachliche Sicherheitspruefung nicht.

Details stehen in `docs/OXAION_PERSONNEL_LOOKUP.md`, `docs/NFC_PERSONNEL_LOOKUP.md` und `docs/PERSONNEL_AUTHENTICATION.md`.

### Mitarbeitergefuehrte Oberflaeche und Dev-Infos

Die PWA hat eine gemeinsame Oberflaeche fuer Produktion und Entwicklung.

Verbindlich:

- `Dev-Infos` ist standardmaessig ausgeschaltet.
- Im Mitarbeitermodus werden nur Informationen angezeigt, die fuer den sicheren aktuellen Ablauf erforderlich oder als Suchhilfe sinnvoll sind.
- Der jeweils naechste Schritt wird deutlich hervorgehoben; noch nicht zulaessige Schritte werden optisch zurueckgenommen und ihre Aktionen deaktiviert.
- Typische Hervorhebungen sind NFC-Anmeldung, Tankscan, ggf. Entnahmeort-Auswahl, Mengeneingabe und Buchungsbutton.
- Backend-Health, Roh-JSON, interne Mix-Daten und Fehler-Simulationen sind Dev-Informationen.
- Fachlich notwendige Recovery-/Fehlermassnahmen duerfen nicht als Dev-Info verborgen werden.
- Der Schalter `Dev-Infos` veraendert ausschliesslich die Sichtbarkeit und niemals Authentifizierung, Backend-Pruefungen, Oxaion-Buchungen, Idempotenz oder Recovery-Regeln.
- Ein einmal bewusst ausgewaehlter Vorgang bleibt bei normalen UI-/Status-Refreshes aktiv. Ein Refresh darf den Bediener nicht wieder zu `Vorgang auswählen` zurueckwerfen oder den Fokus wiederholt aus dem aktuellen Arbeitsschritt ziehen.
- Eine voruebergehende Session-/Auth-Neubestaetigung darf den gewaehlten Vorgang fuer dieselbe Person nur voruebergehend ausblenden, nicht verwerfen. Meldet sich eine andere Person an, muss die vorherige Vorgangsauswahl aus Sicherheitsgruenden neu getroffen werden.

Der verbindliche Mitarbeiterablauf ist:

1. per NFC anmelden, notfalls Personalnummer + Passwort
2. Maschinentank scannen
3. Farbfeld und bekannte passende Lagerorte/Lagerplaetze als Suchhilfe verwenden
4. Nachfuellcharge scannen
5. ggf. tatsaechlichen Entnahmeort bestaetigen
6. Menge bewusst eingeben
7. bei Bedarf weitere Charge nach demselben Schema erfassen
8. buchen
9. eindeutige Erfolgs- oder Fehlermeldung mit konkreter Handlungsanweisung erhalten

Details stehen in `docs/WORKER_GUIDED_UI.md`.

### Neue Mix-Charge und Buchungsdaten

Fuer neue Mix-Chargen ist das Nummernschema verbindlich festgelegt:

```text
<Artikel ohne Punkt>MIX_<yyyyMMdd>_<HHmmss>
```

Beispiel fuer `RP.00010`:

```text
RP00010MIX_20260902_162312
```

Die Nummer wird automatisch aus dem abgeleiteten Artikel und dem aktuellen Erstellungszeitpunkt erzeugt. Sie ist nicht frei editierbar; der Bediener kann lediglich bewusst eine neue Nummer mit neuem Zeitstempel erzeugen.

- `Mix Charge erstellt am` zeigt den Erzeugungszeitpunkt; fuer das bisherige Oxaion-Produktionsdatum wird das aktuelle Datum verwendet.
- Das Buchungsdatum ist beim neuen Nachfuellvorgang immer das aktuelle Datum und wird nicht angezeigt beziehungsweise nicht manuell eingegeben.
- Ziel der neuen Mix-Charge ist immer der gescannte Maschinentank; ein zweites Ziel-Lagerortfeld gibt es im Nachfuellprozess nicht.
- Der Buchungstext ist nicht frei editierbar und lautet dynamisch `Pulver nachfuellen <Maschinen-Lagerort>`, zum Beispiel `Pulver nachfuellen EOS2`.
- Im Mitarbeitermodus werden diese internen Systemdaten nicht unnoetig in den Vordergrund gestellt; ueber `Dev-Infos` bleiben sie fuer Entwicklung und Diagnose sichtbar.

Weitere UI-Regeln stehen in `docs/REPLENISHMENT_INPUT_RULES.md`.

Fuer Offline-Betrieb darf ein zuvor bestaetigter lokaler Maschinenzustand nur nach den Regeln aus `docs/OFFLINE_PWA.md` verwendet werden. Die konkrete maximale Gueligkeitsdauer und der genaue Umfang offline freigegebener Prozessschritte sind noch festzulegen. Nach Reconnect erfolgt vor jeder produktiven Buchung erneut eine serverseitige Validierung.

### Entnahmeschein-QR aus der frueheren Offline-Version

Der bisherige vierteilige Code, zum Beispiel

```text
RP.00010+++EOS1+++87911+++RP00010MIX_20260903_132212
```

stammt aus dem urspruenglichen Offline-Ablauf. Fuer die aktuelle online mit Oxaion verbundene PWA ist noch festzulegen, wie Produktionsleitung beziehungsweise Stellvertretung einen Tanknachfuell- oder Tankwechselauftrag digital erteilt. Bis diese Beauftragung definiert ist, gilt der organisatorische Uebergangsprozess. Der alte Entnahmeschein-QR ist in der neuen Online-PWA keine verbindliche Quelle fuer aktuelle Lagerort-/Chargen-Buchungsschluessel.

## 2. Pulver tauschen

Beim Pulverwechsel ist der Maschinentank der Ausgangspunkt. Ein Maschinentank-Scan ist daher immer verpflichtend.

Der aktuelle Pulverbestand wird nicht manuell eingegeben. Im Online-Fall fragt das Backend nach dem Maschinentank-Scan ueber die freigegebene Oxaion-Logik den aktuellen Bestand des Tanks ab.

Mindestens anzuzeigen sind:

- Maschinentank
- aktuell vorhandener Pulverartikel
- aktuelle Mix-Charge
- Systembestand beziehungsweise Restmenge

Beim Vorgang `Pulver aus Tank auslagern` ist seit 18.09.2026 die physische Wiegung verpflichtender Bestandteil des Ablaufs. Angezeigt werden Oxaion-Systembestand `Qsys`, gewogene Netto-Pulvermenge `Qphys` und die Differenz. Fuer den aktuellen STAGING-Stand werden Mengen auf 0,001 kg normalisiert. Bei Minderbestand wird die Differenz vor der Umlagerung mit dem technisch bestaetigten Buchungsschluessel `I2 = Bestandskorr. Abgang (Schwund)` auf genau Tank/Artikel/Mix-Charge korrigiert; bei Mehrbestand entsprechend mit `I1 = Bestandskorrektur Zugang`. Nach jeder Korrektur wird der Tank erneut aus Oxaion gelesen und muss exakt `Qphys` entsprechen. Erst danach wird die gewogene Menge mit der bestaetigten `LF/LE`-Umlagerung bewegt. Bei unklarem Korrekturausgang wird kein `LF` gestartet.

Nach eindeutig erfolgreicher und verifizierter LF/LE-Auslagerung fragt die WebApp optional, ob Etiketten gedruckt werden sollen. Bei `Ja` wird eine positive ganze Stückzahl abgefragt und separat bestätigt. Der Druck basiert auf der eindeutig verifizierten **LE-Zielbewegung** und folgt dem am 29.09.2026 aufgezeichneten Ablauf `LB31004R -> LB20090J *CHKPOPUP -> LB20100J *CALLA4ETI -> EK99103R -> MN50100J`. Der Druck ist eine eigene korrelierte Operation und kann die bereits erfolgreiche Materialbuchung nicht rückgängig machen. Bei unklarem Ausgang von `MN50100J *RUN` gilt kein Blind-Reprint.

Zusätzlich gibt es den eigenständigen Bedienvorgang `Etiketten nachdrucken`. Er listet bereits eindeutig erfolgreiche Tank-Auslagerungen aus dem serverseitigen Transaktionsspeicher, suchbar nach Lagerbeleg, Artikel, Charge und Ziel. Ein Nachdruck erzeugt immer eine neue eigene Druck-`clientOperationId`, verwendet aber den unveränderten ursprünglichen Tank-Out als fachliche Quelle und revalidiert vor dem Druck erneut exakt dessen `LE`-Zielbewegung im Oxaion-Lagerbeleg. Der aktuell angemeldete Mitarbeiter darf nachdrucken, auch wenn er nicht der ursprüngliche Auslagerungsmitarbeiter war; der Nachdruck selbst wird mit diesem Mitarbeiter protokolliert. Bereits erfolgreich angeforderte Etiketten werden summiert und angezeigt. Existiert zu derselben Auslagerung ein Druckvorgang mit `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED`, wird ein neuer Druck/Nachdruck blockiert, bis der unklare Druckstatus geklärt ist.

Kann Oxaion keinen eindeutigen Bestand ermitteln, darf die WebApp nicht raten. Der Vorgang wird beispielsweise in folgenden Faellen gestoppt:

- kein Systembestand, obwohl physisch Pulver vorhanden ist
- mehrere unerwartete Bestaende
- mehrere nicht eindeutig zuordenbare Chargen

Die WebApp zeigt dann eine verstaendliche Fehlerbeschreibung und eine konkrete Massnahme an.

Welche Teilschritte eines Pulverwechsels offline lediglich vorbereitet und bis `PENDING_SYNC` zwischengespeichert werden duerfen, ist noch offen. Eine produktive Oxaion-Buchung wird offline nicht simuliert oder als erfolgreich angenommen.

### Entnommenes Pulver

Beim Pulverwechsel wird das aktuell in der Maschine befindliche Pulver vollstaendig entnommen und gesiebt. Danach kann es wieder in das Pulverlager zurueckgestellt werden.

Dafuer ist die bestaetigte Oxaion-`LF/LE`-Umlagerung vorgesehen. Seit dem FAM-STAGING-JET-Mitschnitt vom 18.09.2026 sind auch die vorgelagerten Mengenabgleiche `I2` fuer Schwund und `I1` fuer Mehrbestand technisch bestaetigt. Die Details, einschliesslich der FAM-STAGING-spezifischen Kostenstellen und der Transaktionsgrenzen, stehen in `docs/JOB_ABORT_CORRECTION_AND_TANK_WEIGHING_2026-09-16.md`.

### Neue Befuellung

Nach der Entnahme des bisherigen Pulvers:

1. neues Rohmaterial beziehungsweise Rohmaterialcharge scannen
2. neue Mix-Charge nach dem verbindlichen Mix-Chargenschema erzeugen
3. Pulver dem Maschinentank zuordnen beziehungsweise buchen

## Korrekturbuchung Fertigungsauftrag bei Jobabbruch

Seit 18.09.2026 ist der FAM-STAGING-Ablauf technisch bestaetigt und in der WebApp umgesetzt:

1. Der Fertigungsauftrag wird physisch gescannt; ein separater Maschinentank-Scan ist fuer die Jobabbruch-Korrektur nicht erforderlich.
2. Der Vorgang ist nur fuer die bestaetigte komplett abgebuchte Materialposition mit positivem Verbrauch vorgesehen.
3. Die Oxaion-Standard-Stornoliste `PW22021R` gilt als bereits auf gueltige/stornierbare Rueckmeldungen gefiltert. Die WebApp verlangt genau einen passenden Treffer fuer FA, Materialposition, Artikel und urspruengliche Menge. **Tanklagerort und Mix-Charge werden aus dieser Originalrueckmeldung abgeleitet.** Der Lagerort muss ein freigegebener FAM-Maschinentank sein; dessen aktueller Oxaion-Bestand wird danach automatisch gelesen und muss weiterhin denselben Artikel und dieselbe Mix-Charge enthalten. Eine Rueckbuchung auf einen Tank mit inzwischen anderer Mix-Charge ist verboten. Dieselbe Quellpruefung wird unmittelbar vor dem Storno serverseitig wiederholt.
4. Der echte Oxaion-Storno laeuft ueber `PW22000J *LOADNEW` mit `STORNO=J`, `PW22000J *STON` und den gezielten `PW22021R *STORNO` der Rueckmeldung.
5. Eine erfolgreiche HTTP-Antwort allein ist kein Erfolgsbeweis. Vor dem zweiten Schreibschritt muessen Rueckmeldeliste, FA-Materialposition und dieselbe Tank-Mix-Charge den erwarteten Stornozustand bestaetigen.
6. Danach wird der vom Bediener tatsaechlich abgewogene Ist-Verbrauch mit der bereits bestaetigten normalen MK-Logik neu gebucht; bei 0,000 kg wird keine neue MK gesendet.
7. Unklarer Stornoausgang blockiert die neue MK vollstaendig. Ein bestaetigter Storno wird bei spaeterem Fehler niemals automatisch wiederholt.

Details und der Referenzmitschnitt stehen in `docs/JOB_ABORT_CORRECTION_AND_TANK_WEIGHING_2026-09-16.md`.

## Mix-Chargen

Eine Mix-Charge repraesentiert das Pulver, das aus einem oder mehreren Rohmaterialvorgaengen fuer die Produktion bereitgestellt wird.

Die Mix-Charge ist nicht zwingend an eine Maschine gebunden, da dasselbe Pulver prinzipiell auf unterschiedlichen Maschinen eingesetzt werden kann. Die Maschinenzuordnung wird deshalb separat gefuehrt und ist nicht Bestandteil der Mix-Chargennummer.

Verbindliches Nummernschema:

```text
<Artikel ohne Punkt>MIX_<yyyyMMdd>_<HHmmss>
```

## Fehler- und Transaktionshandling

Fehlerhandling ist ein zentraler Bestandteil des Projekts. Bei jedem Buchungsvorgang muss eindeutig nachvollziehbar sein:

- wurde nur lokal erfasst?
- wartet der Vorgang auf Synchronisation?
- wurde er an das Backend uebertragen?
- wurde noch nichts an Oxaion gesendet?
- wurde die Anfrage an Oxaion gesendet?
- wurde erfolgreich gebucht?
- wurde von Oxaion fachlich abgelehnt?
- ist der Ausgang wegen eines Verbindungsabbruchs unbekannt?
- ist ein Datensatz gesperrt?
- ist ein Konflikt nach Offline-Erfassung entstanden?
- ist ein manueller Eingriff notwendig?

Jeder lokal angelegte Vorgang erhaelt eine eindeutige `clientOperationId`. Jeder serverseitig angenommene produktive Buchungsvorgang erhaelt zusaetzlich eine eindeutige Transaktions-ID. Beide werden eindeutig korreliert.

Beispielhafte serverseitige fachliche Status:

- `CREATED`
- `VALIDATING`
- `SENDING_TO_OXAION`
- `SUCCESS`
- `REJECTED`
- `LOCKED`
- `UNCERTAIN`
- `MANUAL_REVIEW_REQUIRED`

Lokale Sync-Zustaende wie `PENDING_SYNC`, `SYNCING`, `SYNCED` oder `CONFLICT` sind davon getrennt zu fuehren.

Die endgueltigen technischen Statusnamen koennen bei der Implementierung sinnvoll angepasst werden. Die fachliche Unterscheidung muss erhalten bleiben.

### Verbindungsabbruch

Es sind zwei Ebenen zu unterscheiden:

1. **Smartphone <-> Backend:** Noch nicht bestaetigte Vorgaenge koennen nach den Regeln aus `docs/OFFLINE_PWA.md` lokal in der Outbox bleiben und spaeter mit derselben `clientOperationId` idempotent synchronisiert werden.
2. **Backend <-> Oxaion:** Bricht die Verbindung ab, nachdem die Anfrage moeglicherweise bereits bei Oxaion angekommen ist, darf nicht einfach erneut gebucht werden. Andernfalls besteht Doppelbuchungsgefahr.

Das Backend muss deshalb:

- jede Anfrage protokollieren
- eine eigene Transaktions-ID fuehren
- die `clientOperationId` eindeutig zuordnen und gegen Mehrfachverarbeitung schuetzen
- nach Moeglichkeit das Oxaion-Ergebnis pruefen
- bei unklarem Zustand `UNCERTAIN` verwenden
- keine unkontrollierten automatischen Wiederholungen von Oxaion-Buchungen ausfuehren

## Oxaion-Sperren

Bei Materialbuchungen auf Fertigungsauftraege kann der entsprechende Oxaion-Datensatz gesperrt sein, etwa weil ein anderer Benutzer den Fertigungsauftrag oder einen relevanten Stammsatz bearbeitet. Dieser Fall wird als eigener fachlicher Zustand behandelt.

Beispielmeldung:

```text
Fertigungsauftrag gesperrt.

Die Materialbuchung wurde nicht durchgefuehrt.

Der Fertigungsauftrag ist derzeit in Oxaion gesperrt.
```

Wenn ueber die Oxaion-Sperrlogik zuverlaessig ermittelbar, wird der sperrende Benutzer zusaetzlich angezeigt, zum Beispiel `Gesperrt durch: MAXM` oder `Gesperrt durch: Max Mustermann`.

`Zuletzt geaendert von` darf nicht als sperrender Benutzer interpretiert werden. Es muss die tatsaechliche Oxaion-Sperrinformation verwendet werden. Ist der Sperrer nicht zuverlaessig ermittelbar, wird `Gesperrt durch: nicht ermittelbar` angezeigt.

Massnahme fuer den Bediener:

```text
Bitte kurz warten und erneut versuchen. Wenn die Sperre bestehen bleibt, Produktionsleitung informieren.
```

Bei einer Sperre gilt:

- Die Buchung wurde nicht durchgefuehrt.
- Es gibt keine automatische Endlosschleife.
- Ein erneuter Versuch erfolgt bewusst und manuell.
- Die Transaktions-ID wird protokolliert.

## Protokollierung

Fuer die spaetere Nachvollziehbarkeit werden mindestens gespeichert:

- `clientOperationId`, wenn der Vorgang im Frontend angelegt wurde
- Transaktions-ID
- Zeitstempel
- Benutzer beziehungsweise Bediener
- Buchungsart
- Fertigungsauftrag
- Rohmaterial
- Rohmaterialcharge
- Mix-Charge
- vorgesehene Maschine
- tatsaechlich verwendete Maschine beziehungsweise Maschinentank
- Menge
- Quelllager
- Ziellager
- lokaler Sync-Status und Synchronisationsversuche, soweit relevant
- verwendeter Maschinen-Cache mit Abfragezeitpunkt/Version, falls eine Offline-Entscheidung darauf beruhte
- Oxaion Request/Referenz, soweit fachlich und datenschutzrechtlich sinnvoll
- Oxaion Response/Status
- Fehlerstatus
- Sperrinformation
- sperrender Benutzer, sofern zuverlaessig ermittelbar
- Wiederholungsversuche
- finaler Zustand

Datenschutz und Security sind zu beachten. Passwoerter, Auth-Tokens und sonstige Secrets duerfen weder protokolliert noch in Git gespeichert werden.

## Bedienkonzept

Die Oberflaeche soll fuer Produktionsmitarbeiter moeglichst einfach sein. Die beiden grossen Hauptaktionen sind:

- Pulver nachfuellen
- Pulver tauschen

Die aktuelle separate Prozessauswahl ist in `docs/SEPARATE_TANK_PROCESSES.md` beschrieben.

Im Normalfall gibt es so wenig manuelle Eingaben wie moeglich. Daten werden bevorzugt aus QR-Codes, NFC, Oxaion und dem vorhandenen Maschinentankbestand ermittelt. Manuelle Eingaben sind nur vorgesehen, wo sie fachlich wirklich notwendig sind. Nicht editierbare Systeminformationen werden optisch klar von wichtigen Eingabefeldern getrennt.

Der aktuelle Mitarbeitermodus ist schrittgefuehrt und blendet nicht benoetigte technische Informationen standardmaessig aus. `Dev-Infos` kann dieselben technischen Informationen fuer Entwicklung und Diagnose auf derselben Seite sichtbar machen, ohne die fachliche Logik zu veraendern.

Fuer den aktuellen Nachfuellablauf bedeutet das insbesondere:

- Mitarbeiter bevorzugt per NFC anmelden; ohne NFC Personalnummer und SYNCOS-Passwort verwenden.
- Vorgang `Pulver nachfuellen` bewusst auswaehlen; die Auswahl bleibt waehrend normaler Refreshes stabil.
- Maschinentank physisch per QR scannen.
- Artikel-Erkennungsfarben und bekannte passende Lagerorte/Lagerplaetze als Suchhilfe anzeigen.
- Nachfuellcharge physisch per QR scannen.
- Lagerort/Lagerplatz aus Oxaion automatisch ermitteln; nur bei Mehrdeutigkeit den tatsaechlichen Entnahmeort bestaetigen lassen.
- Menge als leere, bewusst auszufuellende Bedienereingabe erfassen.
- weitere Charge erst nach vollstaendiger vorheriger Charge erfassen.
- Buchung starten.
- Erfolg oder Fehler gross und eindeutig mit konkreter Massnahme anzeigen.

Offline-, Sync- und Buchungsstatus muessen fuer den Bediener klar und eindeutig sichtbar sein. `Lokal gespeichert` darf niemals wie `erfolgreich gebucht` aussehen.

## Offene Punkte

Die aktuell offenen Punkte werden zentral in `docs/OPEN_POINTS.md` gepflegt. Insbesondere bleiben offen:

- konkrete Oxaion HTTP-Aufrufe fuer die noch fehlenden Materialbuchungen
- Buchungsschluessel fuer Maschinenlager -> Pulverlager und noch nicht abgedeckte Gegenrichtungen
- genaue Oxaion-Abfrage der Stammdatensperre und technische Ermittlung des sperrenden Benutzers
- verbindliche Zuordnung der Produktionsmaschinen-ID aus dem Fertigungsauftrag, z. B. `EP-M650-1`, zum tatsaechlichen Maschinentank/Oxaion-Lagerort und Verhalten bei kurzfristiger Maschinenumplanung
- digitaler Online-Beauftragungsprozess fuer Tanknachfuellung und Tankwechsel; bis zur Entscheidung gilt der bestehende organisatorische Uebergangsprozess
- endgueltiger produktiver Server und sichere produktive Bereitstellung von Laufzeit-Secrets
- produktive HTTPS-/Rolloutdetails der Mitarbeiter-Session
- maximale Offline-Gueligkeitsdauer und konkrete offline zulaessige Prozessschritte
- IndexedDB-Schema, Migrationsstrategie und Frontend-/API-Kompatibilitaet bei PWA-Updates
### STAGING-SQL-Verbindungen dauerhaft speichern

Ab 29.09.2026 werden die beiden serverseitigen STAGING-SQL-Verbindungen (`Syncos__ConnectionString` und `OxaionSql__ConnectionString`) beim ersten Start weiterhin verdeckt eingegeben, danach aber verschluesselt fuer denselben Windows-Benutzer auf demselben Rechner gespeichert. Die Speicherung erfolgt ausserhalb des Repositorys unter `%LOCALAPPDATA%\FAM-Pulverentnahme\staging-sql-secrets.clixml` mit Windows-DPAPI. Umgebungsvariablen haben weiterhin Vorrang. Mit `-ResetStoredSqlConnections` kann die lokale Speicherung bewusst geloescht und neu erfasst werden. Oxaion-HTTP-Benutzer/Passwort bleiben davon getrennt und werden weiterhin beim Start abgefragt.

## Chargenherkunft ueber Oxaion HTTP

Verbindliche Entscheidung ab 08.10.2026:

- Die Grundchargen einer Mixcharge werden nicht durch eine eigene SQL-Rekonstruktion der Oxaion-Tabellen ermittelt.
- Das Backend verwendet die in realen Transaktionsmitschnitten bestaetigte Oxaion-Fachlogik `US17490J` / `US17476R` ueber den vorhandenen HTTP-App-Tunnel.
- `US17476R *FIRSTLIST` liefert den Herkunftsbaum. Zeilen mit `SUBTREES=TRUE` werden ueber die von Oxaion gelieferten Schluessel `PESSID` und `PEMPOS` rekursiv aufgeloest.
- Erst nach vollstaendiger Oxaion-Aufloesung filtert das Backend auf eindeutige Grundchargen. Eine Artikel-/Chargen-Kombination, die irgendwo `SUBTREES=TRUE` besitzt, ist eine Zwischen-/Mixcharge und wird nicht als Grundcharge ausgegeben.
- Der Leseweg ist rein read-only und fuehrt keine ERP-Buchung aus.
- Die beim Oxaion-UI-Einstieg sichtbare interne `POOBID/FIOBID` wird niemals erfunden. Ihr technischer Ermittlungspfad aus Artikel+Charge ist noch nicht im Mitschnitt enthalten und bleibt bis zum STAGING-Test offen.
- Details: `docs/CHARGE_ORIGIN.md`.

## Pulverartikelkreise RP.* und PB.* (08.10.2026)

Neue verbindliche fachliche Information:

- `RP.*`: bisheriger Pulverartikelkreis.
- `PB.*`: vom Kunden beigestelltes Pulver. Beispiel `PB.00001` mit Artikelbezeichnung `AlSi10Mg`.
- Beide Artikelkreise muessen in FAM-Lageranzeige, QR-/Artikelnummernerkennung und bestehenden Pulverprozessen grundsaetzlich als Pulverartikel erkannt werden.
- Die Artikelidentitaet bleibt dabei strikt: `PB.00001` ist trotz gleicher Pulverbezeichnung kein Synonym fuer einen `RP.*`-Artikel. Der bestehende exakte Artikelvergleich bei Scans, Materialquellen und Fertigungsauftraegen darf nicht aufgeweicht werden.
- `PB.*` wird in der Lageransicht als `Kundenbeistellung` gekennzeichnet. Diese Kennzeichnung allein bestaetigt keine Entnahmeberechtigung.
- Buchungsprogramme, Buchungsschluessel und Oxaion-Revalidierung bleiben unveraendert; keine Buchungen per SQL.
- Noch offen: Wie die Kunden-/Auftragsbindung der Beistellcharge technisch aus Oxaion geprueft wird, um eine unerlaubte Verwendung fuer einen anderen Kunden oder Auftrag zu verhindern. Keine Kundenzuordnung anhand der Artikelbezeichnung oder der Artikelgruppe erfinden. Vor produktivem PB-Einsatz ist diese fachliche Freigabe zu klaeren.

## Chargenherkunft in der FAM-Bedienoberflaeche (08.10.2026)

Ab Version `0.1.11` wird die bereits ueber Oxaion HTTP implementierte reine Chargenherkunft-Auskunft in der PWA angeboten: einerseits direkt in den Details einer konkreten Pulverlagercharge bzw. eines eindeutig gefuellten Maschinentanks, andererseits als eigenstaendiger Vorgang `Chargenherkunft anzeigen` mit Scanner fuer `Artikel+++Charge` oder manueller Artikel-/Chargeneingabe.

- Die Herkunft bleibt read-only, online-only und basiert ausschliesslich auf `/api/charge-origin` und der bestehenden Oxaion-Fachlogik `US17490J` / `US17476R`.
- Die PWA zeigt eindeutige Grundchargen und deren vorhandene Herkunftsangaben; die vollstaendige technische Rekursion verbleibt im Backend.
- RP.* und PB.* sind als getrennte Pulverartikelkreise akzeptiert. Eine gleiche Werkstoffbezeichnung erlaubt keine Vermischung der Artikel oder kundenuebergreifende PB-Verwendung.
- Die Kunden-/Auftragsbindung von PB-Kundenbeistellung ist fachlich weiterhin offen und wird nicht durch die Chargenherkunft-Anzeige freigegeben.
- Das neue UI wird gemeinsam mit der PB-Artikelkreis-Erweiterung in `0.1.11` getestet. Die bereitgestellte `0.1.10` MSI bleibt unveraendert.

## Chargenherkunft UI-Korrektur 0.1.12 (08.10.2026)

Im Android-STAGING-Test der 0.1.11 funktionierte die Herkunft in den Bestandsdetails, nicht aber die Scanner-/Suchbedienung im eigenen Vorgang. Ursache: Der Chargenherkunft-Handler wurde bereits bei DOMContentLoaded registriert, waehrend das dynamische Prozess-Panel erst danach durch `ensureUi()` eingefuegt wurde. `0.1.12` verbindet die Controls explizit nach Panel-Erzeugung, idempotent, und laedt die Herkunfts-Stile unabhaengig von der Panel-Existenz. Eine neue JavaScript-Laufzeitpruefung sichert diesen Ablauf ab.

Anzeigeentscheidung: Grundchargen als lesbare getrennte Karten. Artikel, Charge und gesicherte beschaffungsbezogene Zusatzdaten (Lieferant, Bestellung, Lieferschein, Wareneingang) anzeigen, soweit von Oxaion geliefert. `productionOrder` aus den Herkunftszeilen wird **nicht als Ursprung angezeigt**, da ein dort enthaltener Fertigungsauftrag auch ein verbrauchender FA sein kann. Wenn keine Bestellung/kein Wareneingang vorhanden ist, bleiben diese Detailfelder leer/ausgeblendet. In Bestandsdetails und im Herkunftsdialog gibt es rote `Schliessen`-Buttons; im Herkunftsdialog bleibt daneben `Zurueck zu Details`.

## Chargenherkunft-Zusatzspalten 0.1.13 (08.10.2026)

Verbindliche Entscheidung: In der Herkunftsanzeige bleiben **Externe Chargennummer (Charge Lieferant)**, **Lieferantenname** und **Lieferdatum aus dem Wareneingang** sichtbar, sofern Oxaion diese Angaben liefert. Ein **Bestelldatum wird nicht ermittelt oder angezeigt**. Die vorhandene Bestellnummer und Wareneingangsnummer bleiben erhalten.

Nachgewiesen im neuen realen Oxaion-Transaktionsmitschnitt `Chargenherkunft mit Lieferant, L-Charge und Datum.7z`: Die bisherige lesende Oxaion-Funktion `US17476R *FIRSTLIST` liefert diese drei Spalten bereits als `PONR.POCHNL` (Charge Lieferant), `T_TEXT_PELINR_UPOVEP.T_TEXT_PELINR_UPOVEP_TX_PKOAZL1` (Lieferantenname) und `UPOVEP.PELFDT` (Lieferdatum). **Keine weitere Oxaion-Transaktion erforderlich.**

Der Lieferantenname und das Lieferdatum werden nur aus dem zur jeweiligen Grundcharge eindeutig passenden Wareneingangsdatensatz uebernommen. Eine externe Charge kann in anderen Herkunftszeilen derselben Artikel-/Chargenkombination stehen, wird nur bei widerspruchsfreiem Wert uebernommen. Fehlende/mehrdeutige Angaben bleiben leer. Keine eigene Oxaion-SQL-Herkunftsrekonstruktion, keine Buchung und kein Offline-Cache. Diese reine Leseerweiterung ist fuer die neue MSI `0.1.13` vorgesehen; `0.1.12` bleibt unveraendert. Weitere Details in `docs/CHARGE_ORIGIN.md`.

## Freigabe 0.1.13: benutzerabhaengige Oxaion-Herkunftssicht (08.10.2026)

Verbindliche Benutzerentscheidung: Die Erweiterung der Chargenherkunft in Version `0.1.13` wird mit der aktuell vorhandenen, benutzerabhaengigen Oxaion-Sicht von `US17476R` als betrieblicher Zwischenloesung freigegeben. Der fuer den FAM-Backend-App-Tunnel konfigurierte Oxaion-HTTP-Benutzer soll unveraendert bleiben. Die in seiner Sicht freigeschalteten Spalten `PONR.POCHNL`, `T_TEXT_PELINR_UPOVEP.T_TEXT_PELINR_UPOVEP_TX_PKOAZL1` und `UPOVEP.PELFDT` sind eine Betriebsanforderung. Das Wechseln des in der PWA angemeldeten Produktionsmitarbeiters ist davon getrennt und wechselt nicht den technischen Oxaion-HTTP-Benutzer.

Bewusst akzeptiertes Restrisiko: Wird die technische Oxaion-Kennung oder die zugeordnete `US17476R`-Sicht spaeter geaendert, koennen Zusatzfelder im XML fehlen und werden aktuell ohne automatische Warnung leer angezeigt. Bei jedem solchen Wechsel sind deshalb die drei Spalten und mindestens ein bekannter Chargenherkunftsfall vor Nutzung zu pruefen. Keine Aenderung der bestehenden Oxaion-Aufrufe, keine Pflicht fuer eine neue MSI. Eine automatische Spaltenvertragspruefung bleibt eine moegliche spaetere Haertung, nicht Bestandteil dieser Freigabe.
