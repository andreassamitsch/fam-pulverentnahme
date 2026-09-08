# Neues Pulver in leeren Maschinentank - Oxaion-Schreibfolge

Stand: 08.09.2026

## Fachliche Vorgabe

Beim Vorgang `Neues Pulver in Tank fuellen` wird weiterhin immer eine neue automatisch erzeugte Mix-Charge verwendet. Diese Entscheidung bleibt unveraendert, auch wenn nur eine Quellcharge eingefuellt wird oder die Quellcharge selbst bereits eine Mix-Charge ist.

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

## Neue Mix-Charge danach

`LF/LE` allein erzeugt keine neue Charge. Deshalb darf die bestehende fachliche Vorgabe `immer neue Mix-Charge` nicht dadurch ersetzt werden.

Der STAGING-Ablauf kombiniert deshalb folgende bereits einzeln bestaetigten Bausteine in **einem Materialbeleg**:

1. Position 1 `LF -> LE`: erste externe Quellcharge in den leeren Tank, Charge bleibt zunaechst gleich.
2. Position 2 `LM -> LN`: die nun im Tank vorhandene erste Charge wird innerhalb des Tanklagerorts auf die automatisch erzeugte neue Mix-Charge umgecharget.
3. Position 3 ff. `LM -> LN`: weitere externe Quellchargen werden derselben neuen Mix-Charge hinzugefuegt.

Die einzelnen Buchungsarten und ihre Bewegungssemantik sind real bestaetigt. Die konkrete Kombination `LF/LE -> LM/LN` innerhalb desselben Belegs ist mit diesem Stand bewusst ein **STAGING-Testschritt** und muss noch einmal live End-to-End bestaetigt werden, bevor sie als produktiv bewiesen gilt.

## Verifikation und Fehlerverhalten

- Vor dem Schreiben: Personal, leerer Tank und alle Quellbestaende erneut pruefen.
- Jeder Vorgang besitzt `clientOperationId` und Backend-Transaktions-ID.
- Nach jeder wirksamen Position muss das erwartete Bewegungspaar exakt vorhanden sein.
- Am Ende muessen alle erwarteten LF/LE- und LM/LN-Bewegungen exakt einmal im Materialbeleg vorhanden sein.
- Bei Verbindungsabbruch oder nicht eindeutigem Zustand kein Blind-Retry.
- Wenn bereits ein Belegkopf erzeugt wurde, darf die Bedienermeldung nicht behaupten, Oxaion habe gar keinen Beleg angelegt. Belegnummer und Recovery-Hinweis bleiben sichtbar.

## Noch offen

- Live-STAGING-Bestaetigung der kombinierten Ein-Beleg-Kette `LF/LE -> LM/LN` fuer `Neues Pulver in Tank fuellen`.
- Danach bei Mehrfachquelle zusaetzlich Position 3 ff. im selben Vorgang live bestaetigen.
