# Neues Pulver in leeren Maschinentank - Oxaion-Schreibfolge

Stand: 09.09.2026

## Fachliche Vorgabe

Die bisherige Regel `immer neue Mix-Charge` wurde am 09.09.2026 praezisiert und ersetzt.

Verbindlich gilt fuer den **finalen Zustand unmittelbar vor der Buchung**:

- Wird genau **eine bereits eingelagerte Mix-Charge** in einen eindeutig leeren Tank gefuellt, bleibt diese Mix-Charge erhalten. Es wird keine weitere Mix-Charge erzeugt.
- Wird eine eingelagerte Mix-Charge zusammen mit mindestens einer weiteren Charge eingefuellt, wird eine neue automatisch erzeugte Mix-Charge verwendet.
- Wird genau eine Nicht-Mix-Charge eingefuellt, wird weiterhin eine neue Mix-Charge erzeugt.
- Die Entscheidung wird aus der final vorhandenen Quellenliste neu berechnet. Wird z. B. eine zweite Charge hinzugefuegt und vor dem Buchen wieder entfernt, gilt wieder der Ein-Quellen-Fall. Es darf keine veraltete Mix-Entscheidung aus einem frueheren UI-Zustand verwendet werden.

Eine eingelagerte Mix-Charge wird fuer diese Regel artikelbezogen an dem bestehenden Schema `<Artikel ohne Punkt>MIX_...` erkannt. Es wird dabei **nicht** verlangt, dass ihr Erzeugungsdatum dem aktuellen Buchungstag entspricht; historische eingelagerten Mix-Chargen muessen erhalten bleiben koennen.

## Live bestaetigter erster Transfer

Der JET-Mitschnitt vom 08.09.2026 fuer `Pulverlager -> leerer Tanklagerort` bestaetigt die erste physische Umbuchung:

- Quelle: Lagerort/Lagerplatz/Charge, z. B. `FAMLAB / RE1F1 / RP00010MIX_20260907_112715`
- Ziel: leerer Tanklagerort, z. B. `EOS1`
- Buchungsschluessel: `LF`
- automatisch entstehende Gegenbewegung: `LE`
- die Charge bleibt bei diesem LF/LE-Schritt unveraendert
- `TX_PON2` bleibt leer

Der erfolgreiche Referenzbeleg war `FA26MB00054` mit exakt zwei Bewegungen auf Position 1: `LF` von der Quelle und `LE` auf `EOS1`, jeweils `149,574 KGM` derselben Charge.

## Bestaetigte Position-1-Initialisierung

Der fruehere WebApp-Versuch legte den Belegkopf `FA26MB00053` an, scheiterte aber bereits bei `POSITION_1_VALIDATING` mit `BEL1422`. Ursache: die Fortsetzungslogik wurde faelschlich auch fuer die erste Position eines leeren Materialbelegs verwendet.

Der erfolgreiche JET-Mitschnitt zeigt fuer Position 1 zusaetzlich:

1. `LB20115J *LOAD` mit `NOHWPgm=LB20115`, aktueller `SSID`, `mode=merge`.
2. `LB20115J *NEW` mit vollstaendigem Beleg-/Positionskontext, insbesondere `PSANWG=LBS`, `PSBGNR=<Beleg>`, `PSBMN1=0,000`, `PSBMN2=0,000`, `keyFields=PSBGNR PSPOSI PSKOPO PSBGZT`.
3. erste Quellvalidierung als `LF` mit leerem `TX_LAG2`; erwarteter Zwischenstatus `LAG1515` (`Lager 2 angeben`).
4. danach Ziel-Lager `EOS1` setzen; `LB20115J *PUTNEW` liefert `TCODE=WIN3`.
5. `LB20115J *LOADWIN3`.
6. finaler `LB20115J *PUTNEW` mit `TX_FIRST=N` und beiden Mengen auf der tatsaechlichen Menge.
7. `LB20110R *UPD` erzeugt und bestaetigt das `LF/LE`-Bewegungspaar.

Diese Folge wird im Backend fuer die erste LF-Position separat von Fortsetzungspositionen behandelt.

## Dynamische Buchungsketten

### Genau eine bereits eingelagerte Mix-Charge

Die bestaetigte Eigenschaft von `LF -> LE`, die Quellcharge unveraendert auf den leeren Tank zu uebertragen, ist bereits genau das gewuenschte Ergebnis.

Die erwartete Kette besteht deshalb nur aus:

1. Position 1 `LF -> LE`: vorhandene Mix-Charge vom Pulverlager in den leeren Tank, Charge bleibt unveraendert.

Es gibt in diesem Fall **kein** anschliessendes `LM -> LN` und damit keine neue Mix-Charge.

### Nicht-Mix-Quelle oder mehrere Quellen

Sobald die finale Quellenliste nicht aus genau einer vorhandenen Mix-Charge besteht, gilt:

1. Position 1 `LF -> LE`: erste externe Quellcharge in den leeren Tank, Charge bleibt zunaechst gleich.
2. Position 2 `LM -> LN`: die nun im Tank vorhandene erste Charge wird innerhalb des Tanklagerorts auf die automatisch erzeugte neue Mix-Charge umgecharget.
3. Position 3 ff. `LM -> LN`: weitere externe Quellchargen werden derselben neuen Mix-Charge hinzugefuegt.

Die einzelnen Buchungsarten und ihre Bewegungssemantik sind real bestaetigt. Die konkrete Kombination `LF/LE -> LM/LN` innerhalb desselben Belegs bleibt ein **STAGING-Testschritt**, bis sie live End-to-End bestaetigt ist.

## Bedienoberflaeche

- Nach dem Chargenscan wird als Mengen-Vorschlag die aktuell auf der ausgewaehlten Oxaion-Bestandsposition verfuegbare Menge eingesetzt. Der Bediener kann diesen Wert vor der Buchung korrigieren.
- Bei mehreren moeglichen Bestandspositionen wird die Menge erst nach der bewussten Auswahl der konkreten Position vorgeschlagen.
- Die UI zeigt bei genau einer vorhandenen Mix-Charge `Mix-Charge bleibt: <Charge>`.
- Sobald eine weitere Quelle vorhanden ist, wechselt die Anzeige dynamisch auf `Neue Mix-Charge`.
- Wird die weitere Quelle wieder entfernt, wechselt die Anzeige vor der Buchung wieder auf `Mix-Charge bleibt`.
- Serverseitig wird dieselbe Entscheidung unabhaengig vom UI aus der finalen Request-Quellenliste erneut ermittelt.

## Verifikation und Fehlerverhalten

- Vor dem Schreiben: Personal, leerer Tank und alle Quellbestaende erneut pruefen.
- Jeder Vorgang besitzt `clientOperationId` und Backend-Transaktions-ID.
- Nach jeder wirksamen Position muss das erwartete Bewegungspaar exakt vorhanden sein.
- Am Ende muessen alle fuer den finalen Quellenzustand erwarteten LF/LE- und gegebenenfalls LM/LN-Bewegungen exakt einmal im Materialbeleg vorhanden sein.
- Bei Verbindungsabbruch oder nicht eindeutigem Zustand kein Blind-Retry.
- Wenn bereits ein Belegkopf erzeugt wurde, darf die Bedienermeldung nicht behaupten, Oxaion habe gar keinen Beleg angelegt. Belegnummer und Recovery-Hinweis bleiben sichtbar.

## Noch offen

- Live-STAGING-Bestaetigung `eine eingelagerte Mix-Charge -> leerer Tank` mit ausschliesslich `LF/LE` aus der WebApp.
- Live-STAGING-Bestaetigung der kombinierten Ein-Beleg-Kette `LF/LE -> LM/LN` fuer eine Nicht-Mix-Quelle beziehungsweise mehrere Quellen.
- Danach bei Mehrfachquelle zusaetzlich Position 3 ff. im selben Vorgang live bestaetigen.
