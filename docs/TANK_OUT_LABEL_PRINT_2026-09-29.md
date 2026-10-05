# Tank-Out Etikettendruck nach erfolgreicher Auslagerung

Stand: 29.09.2026

## Fachliche Entscheidung

Nach **eindeutig erfolgreichem und verifiziertem** Vorgang `Pulver aus Tank auslagern` fragt die WebApp:

1. `Etiketten drucken?`
2. Bei `Nein`: Vorgang ist abgeschlossen, kein Druckauftrag.
3. Bei `Ja`: Anzahl Etiketten als positive ganze Stückzahl erfassen.
4. Stückzahl nochmals sichtbar bestätigen.
5. Erst danach den Oxaion-Etikettendruck starten.

Der Etikettendruck ist **kein Teil der Materialbuchung**. LF/LE ist bereits erfolgreich abgeschlossen, bevor die Druckfrage erscheint. Ein Druckfehler darf die erfolgreiche Materialbuchung nicht zurücksetzen oder erneut auslösen.

## Technische Quelle

Der Ablauf wurde am 29.09.2026 im JET-/HTTP-Protokoll `Ettikett Drucken Pulver aus Tank in Lager` aufgezeichnet.

Referenzfall:

- Lagerbeleg: `FA26MB00101`
- Artikel: `RP.00010`
- Position: `1`
- zu druckende Zielbewegung: `LE`
- Ziel-Lagerort: `FAMLAB`
- Ziel-Lagerplatz: `KA1`
- Charge: `RP00010MIX_20260909_140218`
- Menge: `127,000 kg`
- `PSBGZT=2026-09-29-16.15.40.289000`
- im Mitschnitt gewählte Etikettenanzahl: `6`

Verbindlich ist nicht der konkrete Beleg, sondern die folgende Aufrufsequenz.

## Bestaetigte Oxaion-Sequenz

### 1. Zielbewegung bestimmen

Gedruckt wird auf Basis der **Zielbewegung `LE`** der erfolgreich verifizierten LF/LE-Auslagerung, nicht auf Basis der Quellbewegung LF.

Die WebApp öffnet den bereits erfolgreichen Lagerbeleg erneut, liest `LB20110R` und akzeptiert nur genau eine Bewegung mit:

- Position `1`
- Buchungsschluessel `LE`
- exakt demselben Artikel
- exakt derselben Charge
- Ziel-Lagerort und Ziel-Lagerplatz aus dem Tank-Out
- exakt derselben ausgelagerten Menge
- nicht leerem `PSBGZT`.

Mehrere oder keine Treffer blockieren den Druck.

### 2. Lagerbewegung fuer Etikett laden

`LB31004R` mit leerer Aktion und dem exakten Positionsschluessel:

- `SLNBNR=<Lagerbeleg>`
- `PSIDNR=<Artikel>`
- `KEYTYPE=LPSDA`
- `PSPOSI=1`
- `PSBGZT=<Zeitstempel der LE-Bewegung>`
- `PSKOPO=0`
- `SLNANW=LBS`
- `SLNPOS=1`
- `PSBGNR=<Lagerbeleg>`

Die Antwort muss wieder exakt die erwartete LE-Zielbewegung bestätigen.

### 3. Popup-/Etikettaufruf

1. `LB20090J *CHKPOPUP` mit demselben Positionsschluessel und der aktuellen Listen-`SSID`.
2. `LB20100J *CALLA4ETI` mit dem Positionsschluessel.
3. Erwartete Antwort: `PGMN=EK99103R` und eine neue `SSID`.

### 4. Etikettenanzahl setzen

1. `EK99103R *LOAD` mit CALLA4ETI-DTA, Positionsschluessel und `WITH_DS=*YES`.
2. Erwartet werden `LFNR=<Lagerbeleg>`, `BGZT=<LE-PSBGZT>`, `POSI=1`.
3. `EK99103R *PRINTCFG` mit:
   - `MENGE=<gewünschte Etikettenanzahl>`
   - `LFNR=<Lagerbeleg>`
   - `BGZT=<LE-PSBGZT>`
   - `POSI=1`.

**Wichtig:** Die gewünschte Zahl der Etiketten steht in `EK99103R.MENGE`. `MN50100J.UGANKO` ist die Kopienzahl des Druckjobs und wird nicht für die Bediener-Stückzahl missbraucht.

### 5. Oxaion-Druckkonfiguration

Bestaetigte Reihenfolge:

1. `MN50100J *GET`
2. `MN50100J *GETTABLE`
3. Aus der Tabelle genau die Zeile
   - `PRT=J`
   - `PRTF=*EK99102P`
   - `BEZC=Etikett Wareneingang`
4. `MN50100J *HIDEDLG`
   - Antwort ist im Mitschnitt nur die XML-Deklaration; genau für diesen Call ist das erlaubt.
5. `MN50100J *CHECK`
6. `MN50100J *CHECKTBL`
7. `MN50100J *PUTTBL`
8. `MN50100J *RUN`

Die Druckerwarteschlange wird **nicht hart codiert**. Im Referenzmitschnitt war `UGOUTQ=MARKETING`; die WebApp übernimmt den aktuellen Wert aus der eindeutigen `GETTABLE`-Zeile.

## Transaktion und Fehlerbehandlung

- Der Druck erhält eine eigene `clientOperationId` und eine eigene serverseitige Operation vom Typ `tank-out-label-print`.
- Sie referenziert den bereits erfolgreichen Tank-Out-Vorgang.
- Der Backend-Endpunkt akzeptiert den Druck nur, wenn der referenzierte Tank-Out `SUCCESS` ist.
- Ein Druckfehler ändert den Materialvorgang nicht.
- Vor `MN50100J *RUN` eindeutig gescheiterter Druck bedeutet: kein Drucklauf bestätigt; Materialbuchung bleibt erfolgreich.
- Transport-/Antwortfehler **ab dem Versand von `MN50100J *RUN`** bedeuten `UNCERTAIN` beziehungsweise `MANUAL_REVIEW_REQUIRED`: Der Druckauftrag könnte bereits in der Warteschlange liegen.
- In diesem Fall **kein Blind-Reprint**. Drucker/Oxaion-Druckwarteschlange prüfen.
- Eine erfolgreiche `*RUN`-Antwort bedeutet: Druckauftrag wurde an Oxaion übergeben. Sie beweist nicht, dass das physische Etikett tatsächlich aus dem Drucker gekommen ist.


## Nachdruck

Zusätzlich zum unmittelbaren Druck nach erfolgreichem Tank-Out gibt es einen eigenständigen Nachdruckvorgang.

Verbindlich:

- Quelle ist immer eine bereits gespeicherte Tank-Auslagerung mit `SUCCESS` und Oxaion-Lagerbeleg.
- Auswahl erfolgt aus einer Liste erfolgreicher Auslagerungen; Suchfelder sind Lagerbeleg, Artikel, Artikelbezeichnung, Charge, Ziel-Lagerort und Ziel-Lagerplatz.
- Der Bediener sieht die Summe aller bisher mit `SUCCESS` an Oxaion übergebenen Etiketten dieses Tank-Outs.
- Jeder Nachdruck erhält eine neue `clientOperationId` und einen neuen serverseitigen Druckvorgang; die Tank-Out-ID wird nur als `RelatedOperationId` referenziert.
- Der aktuell angemeldete Mitarbeiter ist der Druckoperator. Er muss nicht mit dem ursprünglichen Auslagerungsmitarbeiter identisch sein.
- Vor jedem Nachdruck wird die ursprüngliche `LE`-Zielbewegung erneut über den Lagerbeleg eindeutig verifiziert. Es wird niemals eine neue LF/LE-, I1- oder I2-Buchung erzeugt.
- Besteht zu derselben Auslagerung bereits ein Druckvorgang mit `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED`, ist ein neuer Druck gesperrt. Erst Drucker/Warteschlange klären.
- Ein früherer `SUCCESS` blockiert keinen Nachdruck. Das erlaubt bewusst zusätzliche Etiketten oder einen neuen Druckauftrag nach einem physisch fehlgeschlagenen Drucker-Auswurf.
- Ein neuer Nachdruck wird serverseitig pro Quell-Tank-Out serialisiert, damit nicht mehrere Druckläufe derselben Auslagerung gleichzeitig gestartet werden.
