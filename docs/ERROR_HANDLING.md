# Fehler- und Transaktionshandling

## Ziel und Grundregel

Jeder produktive Buchungsvorgang muss eindeutig, nachvollziehbar und gegen Doppelbuchungen geschuetzt sein.

Automatischer Retry einer Oxaion-Buchung ist nur zulaessig, wenn technisch zweifelsfrei feststeht, dass Oxaion den urspruenglichen Buchungsauftrag **nicht** verarbeitet hat. In allen anderen unklaren Faellen gilt `UNCERTAIN` beziehungsweise `MANUAL_REVIEW_REQUIRED`.

Ein Transport-Retry zwischen PWA und Backend ist davon getrennt zu betrachten: dieselbe lokal erfasste Operation darf mit derselben `clientOperationId` erneut uebertragen werden, solange das Backend sie idempotent verarbeitet und dadurch keine neue Oxaion-Buchung entsteht.

## Client-Operation-ID und Transaktions-ID

Da ein Vorgang bereits offline entstehen kann, erzeugt das Frontend beim lokalen Anlegen eine global eindeutige, unveraenderliche `clientOperationId`.

Das Backend erzeugt beim ersten serverseitig angenommenen produktiven Vorgang weiterhin eine eigene global eindeutige, unveraenderliche Transaktions-/Vorgangs-ID und ordnet beide IDs eindeutig einander zu.

- `clientOperationId` korreliert lokale Erfassung, Outbox und wiederholte Uebertragung.
- Die Backend-Transaktions-ID korreliert serverseitige Validierung, Backend-Protokoll, einzelne Buchungsschritte und Oxaion-Referenzen.
- Beide IDs werden fuer Diagnose und Audit nachvollziehbar gespeichert.
- Die serverseitige Eindeutigkeitsregel muss verhindern, dass dieselbe `clientOperationId` mehrere wirksame Oxaion-Buchungen erzeugt.
- Ein Pulverwechsel mit mehreren Schritten benoetigt eine nachvollziehbare Korrelation zwischen Gesamtvorgang und Teilschritten.
- `TODO`: Klaeren, ob und wie die Backend-Transaktions-ID oder eine geeignete externe Referenz in den bestaetigten Oxaion-Aufrufen uebergeben und spaeter abgefragt werden kann.

## Idempotency und Duplicate Prevention

- Derselbe fachliche Auftrag darf nicht mehrfach wirksam gebucht werden.
- Das Backend prueft vor dem Versand, ob fuer die `clientOperationId` beziehungsweise Transaktions-ID bereits ein laufender, erfolgreicher oder unklarer Versand existiert.
- Parallel eintreffende identische Anforderungen muessen serialisiert oder durch eine eindeutige Persistenzregel abgefangen werden.
- Eine bereits erfolgreiche `clientOperationId` beziehungsweise Transaktions-ID liefert das gespeicherte Ergebnis und loest keine neue Buchung aus.
- Eine Transaktions-ID im Status `SENDING_TO_OXAION`, `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED` darf keine weitere Buchung ausloesen.
- Clientseitige Button-Sperren verbessern die Bedienung, ersetzen aber niemals die serverseitige Duplicate Prevention.
- `TODO`: Persistenztechnik, Eindeutigkeitsbedingungen und Aufbewahrungszeit festlegen.

## Lokale Sync-Zustaende

Die PWA fuehrt lokale Sync-Zustaende getrennt von den Backend-/Oxaion-Transaktionsstatus.

Mindestens fachlich unterscheidbar sind:

- `LOCAL_DRAFT`: lokal begonnen, noch nicht fuer Synchronisation freigegeben
- `PENDING_SYNC`: lokal vollstaendig genug fuer die vorgesehene Synchronisation, aber noch nicht serverseitig bestaetigt
- `SYNCING`: Uebertragung zum Backend laeuft
- `SYNCED`: lokaler Eintrag wurde eindeutig einem serverseitigen Vorgang/Status zugeordnet
- `CONFLICT`: serverseitige Revalidierung widerspricht dem lokal angenommenen Zustand
- `FAILED`: technischer Sync-Fehler ohne Aussage, dass eine Oxaion-Buchung fehlgeschlagen ist
- `MANUAL_REVIEW_REQUIRED`: automatische Klaerung nicht moeglich oder nicht zulaessig

`SYNCED` bedeutet nicht automatisch `SUCCESS`. Der lokale Sync-Status und der serverseitige Buchungsstatus muessen separat angezeigt und gespeichert werden.

## Fachliche Backend-/Oxaion-Status

Mindestens folgende Zustaende muessen unterscheidbar sein; die technischen Namen duerfen spaeter angepasst werden:

- `CREATED`: serverseitiger Vorgang angelegt, noch nichts an Oxaion gesendet
- `VALIDATING`: Eingaben und Oxaion-Ausgangsdaten werden geprueft
- `SENDING_TO_OXAION`: Versand wurde begonnen; Ergebnis ist noch nicht bestaetigt
- `SUCCESS`: Oxaion hat die erfolgreiche Buchung eindeutig bestaetigt
- `REJECTED`: Oxaion hat den Auftrag fachlich eindeutig abgelehnt
- `LOCKED`: Oxaion-Datensatz ist gesperrt; Buchung wurde nicht durchgefuehrt
- `UNCERTAIN`: Auftrag koennte verarbeitet worden sein, Ergebnis ist nicht eindeutig
- `MANUAL_REVIEW_REQUIRED`: Automatische Klaerung ist nicht moeglich oder nicht zulaessig

Jeder Statuswechsel wird mit Zeitstempel, Ursache und technischer beziehungsweise fachlicher Referenz protokolliert.

## Smartphone <-> Backend: Offline und Reconnect

Ein Verbindungsabbruch zwischen Smartphone und Backend beweist nichts ueber einen spaeteren Oxaion-Zustand. Es muss unterschieden werden, ob das Backend den Vorgang bereits eindeutig angenommen hat.

### Noch nicht serverseitig angenommen

- Vorgang bleibt mit unveraenderter `clientOperationId` in der lokalen Outbox.
- Anzeige: `Offline erfasst - noch nicht serverseitig bestaetigt.`
- Bei Reconnect darf dieselbe Outbox-Nachricht erneut uebertragen werden.
- Das Backend muss die `clientOperationId` idempotent behandeln.
- Vor einer produktiven Oxaion-Buchung werden aktuelle fachliche Daten serverseitig erneut validiert.

### Backend moeglicherweise erreicht

Wenn die PWA wegen Timeout oder Verbindungsabbruch nicht weiss, ob das Backend die Operation bereits angenommen hat, darf sie nicht einfach eine neue fachliche Operation mit neuer ID erzeugen.

Sie sendet beziehungsweise fragt mit derselben `clientOperationId` erneut an. Das Backend liefert den bereits vorhandenen serverseitigen Zustand oder legt genau eine neue Transaktion an, wenn die ID noch unbekannt ist.

### Konflikt nach Offline-Erfassung

Wenn sich der relevante serverseitige Zustand geaendert hat, wird nicht automatisch gebucht oder ueberschrieben.

Beispiele:

- anderes Pulver auf der Maschine
- nicht mehr kompatible Mix-Charge
- nicht eindeutiger Maschinenbestand
- bereits anderweitig verarbeiteter Vorgang

Der lokale Vorgang wird auf `CONFLICT` beziehungsweise `MANUAL_REVIEW_REQUIRED` gesetzt und erhaelt eine konkrete Bedienermassnahme.

## Oxaion-Sperren

- Eine bestaetigte Oxaion-Datensatzsperre ist ein eigener fachlicher Zustand `LOCKED`.
- Bei Sperre gilt die Buchung als nicht durchgefuehrt, sofern die Oxaion-Schnittstelle dies eindeutig bestaetigt.
- Der tatsaechlich sperrende Benutzer wird nur aus zuverlaessiger Oxaion-Sperrinformation angezeigt.
- `Zuletzt geaendert von` ist kein Ersatz fuer die Sperrinformation.
- Ist der Sperrer nicht ermittelbar, lautet die Anzeige `Gesperrt durch: nicht ermittelbar`.
- Es gibt keine automatische Endlosschleife; ein erneuter Versuch wird bewusst durch den Bediener gestartet.
- `TODO`: Oxaion-Sperrabfrage, Fehlerkennung und Benutzerermittlung technisch bestaetigen.

### Eigene Sperren nach Abschluss-/Recovery-Verifikation

Ein von der WebApp selbst fuer eine Ergebnispruefung geoeffneter Oxaion-Beleg darf nach Abschluss der Pruefung nicht offen beziehungsweise gesperrt zurueckbleiben.

Fuer den bestaetigten Mix-Beleg gilt deshalb:

- Nach den schreibenden Positionen wird der Lagerbeleg mit `LB20100J *END` geschlossen.
- Fuer die Abschlussverifikation darf er erneut rein lesend geoeffnet werden.
- Nach erfolgreicher `LB20110R *FIRSTLIST`-Verifikation wird derselbe Beleg **nochmals explizit mit `LB20100J *END` geschlossen**.
- Der allgemeine app-tunnel `/disconnect` ist kein Ersatz fuer dieses explizite fachliche `*END`.
- `SUCCESS` darf erst gesetzt werden, wenn die Bewegungen vollstaendig verifiziert und der fuer die Verifikation geoeffnete Beleg erfolgreich geschlossen wurde.
- Der notwendige Close-Cleanup nach bereits persistierter/verifizierter Buchung darf nicht allein deshalb entfallen, weil der Browserrequest zwischenzeitlich abgebrochen wurde.
- Ist die Buchung bereits vollstaendig verifiziert, aber das abschliessende `*END` kann nicht eindeutig bestaetigt werden, darf nicht erneut gebucht werden. Der Sperrzustand muss manuell geprueft werden; der Vorgang ist nicht als einfacher Buchungsfehler zu behandeln.

Hintergrund: Am 02.09.2026 wurde in STAGING beobachtet, dass der bisherige Prototyp nach erfolgreicher Buchung den zur Abschlussverifikation erneut geoeffneten Beleg nicht mehr explizit mit `*END` schloss. Dadurch blieb der Datensatz in Oxaion gesperrt.

## Backend <-> Oxaion: HTTP Timeout und Connection Reset

Timeout oder Connection Reset beweisen allein nicht, dass Oxaion den Auftrag nicht verarbeitet hat.

- Fehler vor zweifelsfreiem Versand: kontrollierter Retry kann gemaess definierter Regel zulaessig sein.
- Fehler waehrend oder nach moeglichem Versand: Status `UNCERTAIN`; keine erneute Buchung.
- Das Backend protokolliert Versandbeginn, Abbruchzeitpunkt, Zieloperation, Transaktions-ID und vorhandene Oxaion-Referenzen.
- Nach Moeglichkeit wird der Ausgang ueber eine fachlich bestaetigte Status- oder Referenzabfrage geklaert.
- `TODO`: Timeoutwerte und verifizierbare Oxaion-Ergebnisabfrage fuer weitere Buchungsarten festlegen.

## HTTP-Fehlercodes

HTTP-Status und fachliches Ergebnis werden getrennt bewertet:

- Ein HTTP-Erfolgscode ist nicht automatisch eine erfolgreiche Materialbuchung; die fachliche Antwort muss geprueft werden.
- Ein HTTP-Fehlercode ist nicht automatisch der Beweis, dass keine Buchung erfolgte; Versandphase und Oxaion-Verhalten sind entscheidend.
- Authentifizierungs- und Autorisierungsfehler fuehren zu keiner automatischen Wiederholung und muessen administrativ geklaert werden.
- Rate-Limit-, Gateway- und Serverfehler duerfen nur wiederholt werden, wenn Nichtverarbeitung zweifelsfrei nachgewiesen ist.
- Konkrete Zuordnung von HTTP-Codes zu Oxaion-Ergebnissen bleibt bis zur Schnittstellenanalyse `TODO`.

## Fachliche Oxaion-Fehler

- Eine eindeutige fachliche Ablehnung wird als `REJECTED` gespeichert.
- Die bereinigte Oxaion-Meldung wird in eine verstaendliche Bedienermeldung mit konkreter Massnahme ueberfuehrt.
- Ein Retry ohne fachliche Korrektur ist nicht zulaessig.
- Unbekannte Codes werden nicht interpretiert oder erfunden, sondern als offen dokumentiert und gegebenenfalls zur manuellen Pruefung gegeben.

## Unklare Buchungsergebnisse

Ein Ergebnis ist unklar, wenn Oxaion die Anfrage moeglicherweise erhalten oder verarbeitet hat, aber keine eindeutige bestaetigte Antwort vorliegt.

Dann gilt:

1. Status auf `UNCERTAIN` setzen.
2. Weitere automatische und manuelle Doppel-Ausloesung derselben Transaktions-ID blockieren.
3. Vorhandene Referenzen und Protokolle sichern, ohne Secrets zu speichern.
4. Eine bestaetigte Ergebnisabfrage versuchen, sofern diese rein lesend und fachlich geklaert ist.
5. Bei fehlender Klaerbarkeit auf `MANUAL_REVIEW_REQUIRED` setzen.
6. Ergebnis der manuellen Pruefung mit Benutzer, Zeitpunkt, Begruendung und finalem Status protokollieren.

## Retry-Regeln

### PWA -> Backend

- Derselbe Outbox-Eintrag darf mit derselben `clientOperationId` nach Transportfehler erneut uebertragen werden.
- Eine neue `clientOperationId` fuer denselben fachlichen Vorgang darf nicht als einfacher Retry erzeugt werden.
- Wiederholte Uebertragung darf serverseitig keine zweite Transaktion beziehungsweise Oxaion-Buchung erzeugen.

### Backend -> Oxaion

- Kein Retry bei `SUCCESS`, `REJECTED`, `LOCKED`, `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED`.
- Ein automatischer Retry ist nur bei eindeutigem Nichtversand beziehungsweise nachgewiesener Nichtverarbeitung zulaessig.
- Anzahl, Abstand und technische Bedingungen erlaubter Retries werden begrenzt und protokolliert.
- Ein manueller erneuter Versuch nach `LOCKED` ist ein bewusster Bedienvorgang und muss nachvollziehbar dem vorherigen Vorgang zugeordnet werden.
- `TODO`: Konkrete Retry-Policy erst nach Analyse der Oxaion-Schnittstelle festlegen.

## PWA-Update und Fehlerfall

Ein App-Update darf weder einen laufenden Vorgang abbrechen noch noch nicht synchronisierte Outbox-Daten verlieren.

- Kein erzwungener Reload waehrend eines aktiven kritischen Vorgangs oder laufender Synchronisation.
- Service-Worker-Cache-Bereinigung darf IndexedDB nicht loeschen.
- IndexedDB-Schemamigrationen muessen `PENDING_SYNC`-Daten erhalten.
- Wenn eine neue Frontend-Version mit dem aktuell erreichbaren Backend nicht kompatibel ist, muss die App einen klaren administrativen Fehler anzeigen statt Buchungen mit unklarem Verhalten zu versuchen.

Details stehen in `docs/OFFLINE_PWA.md`.

## Manuelle Nachbearbeitung

Eine manuelle Nachbearbeitung benoetigt mindestens:

- `clientOperationId`, soweit vorhanden
- Transaktions-ID und Zeitstempel
- Benutzer und Buchungsart
- Fertigungsauftrag, Material, Charge und Mix-Charge
- Plan- und Ist-Maschine
- angeforderte Menge, Quell- und Ziellager
- lokaler Sync-Status und verwendeter Maschinen-Cache, wenn relevant
- Versandstatus und bereinigte Oxaion-Referenz/-Antwort
- bisherigen Statusverlauf
- dokumentierte Pruefung in Oxaion
- Entscheidung, Begruendung, verantwortliche Person und finalen Status

Die Nachbearbeitung darf keine unkontrollierte direkte SQL-Buchung in Oxaion verwenden.

## Protokoll- und Sicherheitsregeln

- Keine Passwoerter, Tokens, Connection Strings oder sonstigen Secrets protokollieren.
- Personenbezogene Daten nur im erforderlichen Umfang speichern und angemessen schuetzen.
- Request- und Response-Daten vor der Persistierung filtern beziehungsweise redigieren.
- Audit-Daten gegen unbeabsichtigte Aenderung schuetzen und eine noch festzulegende Aufbewahrungsregel anwenden.
- Lokale Browserdaten sind nur Zwischenpuffer und duerfen nicht als einziges dauerhaftes Audit-Archiv verwendet werden.

## Mehrschritt-Korrekturen: Tankwiegung und FA-Jobabbruch

Seit 18.09.2026 gelten fuer die technisch bestaetigten Korrekturprozesse zusaetzliche feste Transaktionsgrenzen.

### Tankwiegung vor LF/LE

- Wenn ein I1/I2-Teilvorgang bereits einen Oxaion-Lagerbelegkopf erzeugt hat und danach im Dialogaufbau oder bei `LB20115J *PUTNEW` technisch abbricht, bleibt der Teilvorgang `MANUAL_REVIEW_REQUIRED`. Auch wenn noch keine bestaetigte Lagerbewegung vorliegt, darf der Gesamtvorgang nicht blind mit neuer `clientOperationId` wiederholt werden, bevor der Teilvorgang beziehungsweise der angelegte Oxaion-Beleg abgeglichen wurde.
- Sonderfall aus den frischen I1/I2-Mitschnitten: `US00006J *GETPLAIN` fuer `PSKSTL` liefert erfolgreich nur die XML-Deklaration ohne `PARM`/`DTA`. Dieser leere Response darf nur an diesem explizit bestaetigten Dialogschritt als erwarteter Erfolg behandelt werden; er ist **kein** allgemeines Signal, leere Oxaion-Antworten zu akzeptieren.

- Eine erforderliche I1-/I2-Bestandskorrektur ist eine eigene idempotente Teiltransaktion mit eigener `clientOperationId`-Ableitung.
- Bei `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED` der Korrektur darf **kein** LF/LE-Transfer gestartet werden.
- Nach bestaetigter I1/I2-Bewegung muss der Tankbestand erneut gelesen werden und exakt der gewogenen, auf 0,001 kg normalisierten Menge entsprechen.
- Ist I1/I2 bestaetigt, aber der spaetere LF/LE-Transfer unklar, wird die Korrektur weder automatisch storniert noch erneut gebucht. Nur der LF/LE-Teil darf ueber seine vorhandene lesende Verifikation geklaert werden.
- Ein Reconcile des Gesamtvorgangs startet niemals nachtraeglich automatisch einen noch nicht begonnenen LF/LE-Schreibschritt.

### FA-Jobabbruch

- Bereits beim FA-Scan wird die gueltige Oxaion-Stornorueckmeldung rein lesend gegen den zuvor gescannten Tank und dessen Mix-Charge geprueft. Weicht Tank oder Mix-Charge von der Originalrueckmeldung ab, ist dies ein fachlicher Konflikt **vor jedem Schreibaufruf**: keine Mengeneingabe, kein Storno, keine neue MK. Bedienermeldung: Tankcharge hat sich seit der urspruenglichen FA-Buchung geaendert; Fall in Oxaion pruefen und gegebenenfalls manuell ueber Lagerbelege korrigieren.
- Diese Vorpruefung ersetzt nicht die erneute serverseitige Quellpruefung unmittelbar vor dem Storno.

- `PW22021R *STORNO` kann im bestaetigten FAM-Referenzfall bei HTTP-Erfolg nur die XML-Deklaration zurueckgeben. Diese Antwort ist **kein** Erfolgskriterium.
- Nach einem Stornoaufruf gilt der Ausgang solange als nicht bestaetigt, bis alle drei Beweise vorliegen:
  1. exakter Rueckmeldeschluessel ist aus der `PW22021R`-Liste verschwunden;
  2. FA-Materialposition zeigt den bestaetigten Stornozustand `AMMATV=0 / AMMPST=0`;
  3. dieselbe Tank-Mix-Charge ist exakt um die urspruenglich stornierte Menge erhoeht.
- Fehlt einer dieser Beweise, wird keine neue MK gestartet und der Storno niemals blind wiederholt.
- Nach bestaetigtem Storno wird die korrigierte MK als eigene deterministisch korrelierte Teiltransaktion ausgefuehrt.
- Ist der Storno sicher bestaetigt, aber die neue MK nicht eindeutig erfolgreich, darf der Gesamtvorgang nicht von vorne gestartet werden. Der Zwischenzustand geht in `MANUAL_REVIEW_REQUIRED`; nur der MK-Teilvorgang wird nach den bestehenden MK-Reconcile-Regeln untersucht.
