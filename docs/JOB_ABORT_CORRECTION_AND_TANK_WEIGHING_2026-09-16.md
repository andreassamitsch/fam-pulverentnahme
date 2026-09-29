# Korrekturbuchung Fertigungsauftrag und Tank-Wiegung

Stand: 16.09.2026

Dieses Dokument beschreibt die neue verbindliche fachliche Anforderung fuer

- `Korrekturbuchung Fertigungsauftrag (Jobabbruch)` und
- die Wiegung beim Vorgang `Pulver aus Tank auslagern`.

Die allgemeinen Projektregeln zu eindeutiger `clientOperationId`, serverseitiger Transaktions-ID, Oxaion-Revalidierung, Idempotenz, `UNCERTAIN`, `MANUAL_REVIEW_REQUIRED` und Kein-Blind-Retry gelten unveraendert.

## 1. Korrekturbuchung Fertigungsauftrag (Jobabbruch)

### Fachlicher Hintergrund

Das Pulver soll kuenftig bereits beim Start eines Druckjobs auf den Fertigungsauftrag gebucht werden. Wird der Druckjob danach abgebrochen, entspricht die bereits gebuchte Materialmenge nicht dem tatsaechlichen Verbrauch.

Der fehlerhafte Verbrauch darf nicht durch eine frei erfundene Gegenbuchung korrigiert werden. Der fachliche Sollablauf ist:

1. die urspruengliche Materialrueckmeldung des Fertigungsauftrags eindeutig ermitteln;
2. genau diese Oxaion-Rueckmeldung stornieren;
3. den Storno eindeutig verifizieren;
4. dadurch muss die urspruenglich entnommene Menge wieder dem urspruenglichen Tanklager und der zugehoerigen Mix-Charge zugeordnet sein;
5. den tatsaechlich verbrauchten Wert erfassen;
6. danach eine neue, korrekte Materialrueckmeldung auf denselben Fertigungsauftrag ausfuehren;
7. den finalen FA- und Tankzustand erneut eindeutig verifizieren.

Oxaion dokumentiert fuer Fertigungsauftragsrueckmeldungen ausdruecklich, dass fehlerhafte Rueckmeldungen zuerst storniert und danach korrekt neu rueckgemeldet werden sollen. Die konkrete HTTP-/JET-Sequenz fuer den Storno ist fuer die FAM-STAGING-Umgebung jedoch noch nicht aufgezeichnet und darf nicht aus allgemeinen Feldnamen abgeleitet werden.

### Bereits technisch bestaetigt

Fuer die normale Materialrueckmeldung ist bereits bestaetigt:

- Materialposition und Artikelbezug werden aus Oxaion ermittelt;
- `AMMATB` = Soll-/Materialbedarf;
- `AMMATV` = bereits tatsaechlich gebuchter Verbrauch;
- `AMMPST` = Materialpositionsstatus;
- exakte Position kann mit `PW20201J *READ` erneut gelesen werden;
- normale Materialkomplettentnahme `MK` laeuft ueber `PW22000J` / `PW22031J`;
- `ARAKKZ=MK` ist bestaetigt;
- vor `PW22031J *PUTNEW` wird der bestaetigte Zusatzdialogzustand geprueft;
- nach der Schreiboperation wird nur lesend auf den erwarteten Endzustand geprueft;
- ein unklarer Ausgang wird niemals blind erneut gebucht.

Im aktuellen MK-Code wird beim Dialogaufbau explizit `STORNO=N` uebergeben. Dies ist nur ein technischer Hinweis darauf, dass der Oxaion-Dialog einen Stornokontext kennt. Es ist **keine** Bestaetigung, dass fuer einen Storno lediglich `STORNO=J` gesetzt werden muss. Ein solcher Ablauf darf nicht erfunden werden.

### Noch fehlender Oxaion-Nachweis

Der vollstaendige JET-/HTTP-Mitschnitt eines realen MK-Stornos in STAGING liegt seit 18.09.2026 vor; die bestaetigte Sequenz ist in Abschnitt 6 dokumentiert.

Der Mitschnitt muss mindestens zeigen:

- wie die konkrete bereits gebuchte Rueckmeldung ausgewaehlt bzw. eindeutig referenziert wird;
- welche Programme und Commands beim Storno aufgerufen werden;
- welche Felder den Storno kennzeichnen;
- welche Pflichtfelder aus der Originalrueckmeldung wiederverwendet werden;
- welchen Status die Materialposition nach erfolgreichem Storno erhaelt;
- welchen Wert `AMMATV` danach besitzt;
- wie die Lagerbewegung zurueck auf den Tank erfolgt;
- ob Lagerort, Lagerplatz und insbesondere dieselbe Mix-Charge automatisch wiederhergestellt werden;
- welche Antwort einen erfolgreichen Storno eindeutig beweist;
- wie ein unklarer Stornoausgang rein lesend geklaert werden kann.

### Geplanter Bedienablauf

Der sichtbare Vorgang heisst verbindlich:

`Korrekturbuchung Fertigungsauftrag (Jobabbruch)`

Geplanter Ablauf:

1. Mitarbeiter anmelden.
2. Maschinentank scannen.
3. Fertigungsauftrag scannen.
4. Pulverartikel aus FA und Tank muessen uebereinstimmen.
5. Exakte Materialposition und aktuell gebuchten Verbrauch aus Oxaion lesen.
6. Die urspruengliche zu stornierende Materialrueckmeldung eindeutig bestimmen.
7. Vor dem Storno FA-Zustand, Originalrueckmeldung, Tanklager und Mix-Charge anzeigen.
8. Bediener bestaetigt bewusst `Job abgebrochen / Buchung korrigieren`.
9. Originalrueckmeldung stornieren.
10. Storno ausschliesslich ueber bestaetigte Oxaion-Lesewege verifizieren.
11. Erst wenn der Storno eindeutig erfolgreich ist, den tatsaechlichen Ist-Verbrauch erfassen.
12. Neue korrekte Materialrueckmeldung fuer den tatsaechlichen Ist-Verbrauch ausfuehren.
13. Final FA-Materialposition, Tankbestand und Mix-Charge erneut lesen und exakt verifizieren.

### Transaktions- und Fehlergrenzen

Der Vorgang ist fachlich **ein Gesamtvorgang mit mindestens zwei schreibenden Teilschritten**:

- Teil A: Storno der urspruenglichen FA-Materialrueckmeldung;
- Teil B: neue korrekte FA-Materialrueckmeldung.

Beide Teilschritte muessen unter einer gemeinsamen Korrekturtransaktion korreliert werden und jeweils eine eindeutige technische Teilreferenz erhalten.

Verbindlich:

- Teil B darf erst gestartet werden, wenn Teil A eindeutig als erfolgreich verifiziert ist.
- Ist der Storno `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED`, wird keine neue MK-Buchung gestartet.
- Wird der Storno eindeutig erfolgreich, aber die neue MK-Buchung schlaegt eindeutig vor Versand fehl, bleibt der fachliche Zwischenzustand sichtbar und darf nicht automatisch durch eine weitere Gegenbuchung kompensiert werden.
- Ist der Ausgang der neuen MK-Buchung unklar, gilt ebenfalls Kein-Blind-Retry.
- Ein Storno wird niemals nur aufgrund eines vermuteten Endzustands erneut gesendet.

### Noch offene Eingabesemantik der Wiegung nach Jobabbruch

Vor der UI-Implementierung ist noch festzulegen, welcher physische Wert direkt gewogen und eingegeben wird:

- Variante A: tatsaechlich verbrauchtes Pulver wird direkt als Ist-Verbrauch gewogen/eingegeben; oder
- Variante B: nach dem Abbruch zurueckgewonnenes Pulver wird gewogen und der Verbrauch aus urspruenglicher Buchungsmenge minus Rueckgewinnung berechnet.

Die App darf diese Bedeutung nicht stillschweigend festlegen. Angezeigt werden sollen in jedem Fall urspruenglich gebuchte Menge, korrigierter Ist-Verbrauch und resultierende Differenz.

## 2. Pulver aus Tank auslagern mit Wiegung

### Neue verbindliche Fachlogik

Beim Auslagern eines Maschinentanks ist die physisch gewogene Pulvermenge die Information darueber, wie viel Pulver tatsaechlich aus dem Tank entnommen wird.

Vor der Buchung werden getrennt erfasst bzw. angezeigt:

- `Qsys`: aktueller Oxaion-Systembestand des Tanks;
- `Qphys`: tatsaechlich gewogene Netto-Pulvermenge;
- `Delta = Qphys - Qsys`.

Die Systemmenge wird weiterhin unmittelbar vor einer schreibenden Buchung erneut aus Oxaion gelesen. Die Waage ersetzt nicht die Oxaion-Revalidierung.

### Fall A: keine relevante Abweichung

Wenn `Qphys` und `Qsys` innerhalb der noch festzulegenden Waagen-/Rundungstoleranz uebereinstimmen:

1. keine Bestandskorrektur;
2. vorhandene bestaetigte `LF`-/`LE`-Umbuchung auf den ausgewaehlten Lagerort/Lagerplatz;
3. zu transferierende Menge ist die physisch bestaetigte Menge nach der verbindlich festgelegten Rundungsregel.

### Fall B: weniger Pulver als im System (`Qphys < Qsys`)

Die Differenz

`Schwund = Qsys - Qphys`

muss zuerst als nachvollziehbare Bestandskorrektur **auf genau dem Tankbestand, Artikel und der aktuellen Mix-Charge** ausgebucht werden.

Erst nach eindeutig erfolgreicher Korrektur wird der Tankbestand erneut gelesen. Er muss danach der physisch gewogenen Menge entsprechen. Erst dann wird diese Menge mit `LF`/`LE` auf den ausgewaehlten Lagerort/Lagerplatz umgebucht.

Der FAM-STAGING-JET-Mitschnitt vom 18.09.2026 bestaetigt `I2 = Bestandskorr. Abgang (Schwund)` als wirksamen Dialogschluessel inklusive realer Mengenreduktion derselben Mix-Charge. Details siehe Abschnitt 6.

### Fall C: mehr Pulver als im System (`Qphys > Qsys`)

Die Differenz

`Mehrbestand = Qphys - Qsys`

muss zuerst als nachvollziehbare positive Bestandskorrektur **auf genau dem Tankbestand, Artikel und der aktuellen Mix-Charge** gebucht werden.

Erst nach eindeutig erfolgreicher Korrektur wird der Tankbestand erneut gelesen. Er muss danach der physisch gewogenen Menge entsprechen. Erst dann wird diese Menge mit `LF`/`LE` auf den ausgewaehlten Lagerort/Lagerplatz umgebucht.

Der FAM-STAGING-JET-Mitschnitt vom 18.09.2026 bestaetigt `I1 = Bestandskorrektur Zugang` als wirksamen Dialogschluessel inklusive realer Mengenerhoehung derselben Mix-Charge. Details siehe Abschnitt 6.

### Warum Korrektur vor Transfer

Die Reihenfolge `Bestand korrigieren -> Tank erneut lesen -> physische Menge transferieren` ist fachlich bevorzugt, weil damit Oxaion zuerst auf den tatsaechlich festgestellten Tankbestand gebracht wird. Erst danach wird genau der physisch vorhandene Bestand aus dem Tank ausgelagert.

Insbesondere bei `Qphys > Qsys` koennte die physische Menge andernfalls groesser als der in Oxaion vorhandene Bestand sein. Eine Transferbuchung darf dies nicht durch negative Bestaende oder andere implizite Annahmen umgehen.

Ob Bestandskorrektur und anschliessender `LF`-Transfer in **einem einzigen Oxaion-Lagerbeleg** sicher abgebildet werden koennen, ist noch zu testen. Falls Oxaion dies im Dialog zulaesst, soll dazu ein eigener JET-Mitschnitt erstellt werden. Bis zur Bestaetigung wird keine gemeinsame Positionsfolge erfunden.

### Transaktionsgrenzen bei der Wiegung

Bei Abweichung ist Tank-Auslagern ebenfalls ein Mehrschrittvorgang:

1. Bestandskorrektur;
2. Re-Read des Tankbestands;
3. `LF`-/`LE`-Transfer der gewogenen Menge;
4. finale Verifikation des Lagerbelegs und Tankzustands.

Verbindlich:

- Bei unklarem Ausgang der Bestandskorrektur wird kein Transfer gestartet.
- Bei erfolgreicher Korrektur und unklarem Transfer wird die Korrektur nicht automatisch rueckgaengig gemacht.
- Alle Teilschritte gehoeren zu einer gemeinsamen WebApp-Gesamttransaktion und muessen separat nachvollziehbar sein.
- Kein Blind-Retry.

## 3. Noch erforderliche STAGING-Mitschnitte

### Mitschnitt A - Storno einer bereits gebuchten MK-Rueckmeldung

Vorher dokumentieren:

- Fertigungsauftrag;
- Materialposition;
- Pulverartikel;
- `AMMATV` und `AMMPST`;
- Tanklagerort;
- Mix-Charge;
- Tankbestand.

Dann JET starten **bevor** die zu stornierende Rueckmeldung ausgewaehlt wird und in `PW22000`/`PW22031` genau eine bereits erfolgte MK-Materialrueckmeldung stornieren.

JET erst nach erfolgreicher Rueckkehr aus dem Stornodialog beenden.

Danach zusaetzlich pruefen bzw. protokollieren:

- Materialposition mit `PW20201J *READ` bzw. entsprechender Oxaion-Anzeige;
- Tankbestand ueber `Chargen pro Lagerort`;
- wiederhergestellte Mix-Charge und Menge.

### Mitschnitt B - korrigierte MK nach dem Storno

Nach demselben Storno auf derselben Materialposition eine kleinere, bewusst bekannte tatsaechliche Verbrauchsmenge neu rueckmelden.

Damit wird bestaetigt:

- welcher Materialstatus nach Storno entsteht;
- ob die bereits bestaetigte normale MK-Sequenz danach wieder zulaessig ist;
- welcher finale `AMMATV`/`AMMPST` entsteht;
- wie sich der Tankbestand final veraendert.

### Mitschnitt C - negative Tankkorrektur / Schwund

In STAGING mit einer kleinen kontrollierten Menge:

1. Tankartikel, Mix-Charge und Ausgangsbestand dokumentieren.
2. In `US50000` pruefen, welcher FAM-Buchungsschluessel dem Standardzweck `Bestandskorr. Abgang (Schwund)` entspricht und ob er im Dialog zugelassen ist.
3. JET starten.
4. Im normalen Oxaion-Lagerbelegdialog eine kleine Korrektur auf genau diesen Tank, Artikel und diese Mix-Charge buchen.
5. Vollstaendigen Ablauf bis zur Belegschliessung mitscheiden.
6. Tankbestand danach erneut lesen und exakte Reduktion bestaetigen.

### Mitschnitt D - positive Tankkorrektur

Analog zu C, jedoch fuer den FAM-Buchungsschluessel, der dem Standardzweck `Bestandskorrektur Zugang` entspricht. Danach muss der Bestand derselben Mix-Charge exakt um die gebuchte Menge erhoeht sein.

### Optionaler Mitschnitt E - Korrektur und LF in einem Lagerbeleg

Falls die Oxaion-Oberflaeche es zulaesst:

1. Position 1: kleine positive oder negative Bestandskorrektur auf dem Tank;
2. Position 2: `LF` der danach korrekten physischen Gesamtmenge auf einen Lagerort/Lagerplatz;
3. Beleg normal abschliessen.

Dieser Mitschnitt entscheidet, ob die WebApp beide Schritte spaeter in einem Materialbeleg verifizieren kann oder bewusst zwei korrelierte Oxaion-Vorgaenge verwenden muss.

## 4. Was im Mitschnitt benoetigt wird

Fuer die technische Rekonstruktion sollen die Mitschnitte enthalten:

- Programmname;
- Command (`*LOAD`, `*NEW`, `*CHK`, `*PUTNEW`, `*UPD`, `*END`, ...);
- DTA-Felder;
- `SSID` und relevante Zugriffsschluessel;
- `TCODE`/`FCOD` und Meldungen;
- zurueckgelieferte Bewegungszeilen;
- finalen gelesenen FA-/Tankzustand.

Passwoerter, Cookies, Tokens und andere Secrets gehoeren nicht in die Projektdateien.

## 5. Noch offene fachliche Entscheidung

Die zulaessige Wiege-/Rundungstoleranz ist noch festzulegen. Sie darf nicht geraten werden.

Dafuer benoetigt die Implementierung mindestens die Aufloesung bzw. kleinste Anzeigestufe der eingesetzten Waage. Danach wird entschieden, ob beispielsweise auf die Waagenaufloesung gerundet wird und ab welcher Differenz eine Bestandskorrektur erforderlich ist.


## 6. Technisch bestaetigt durch FAM-STAGING-JET vom 18.09.2026

Der Mitschnitt `FA Mat Storno - Schwund und Plus LB buchungen` bestaetigt die zuvor offenen Schreibsequenzen.

### 6.1 FA-Materialrueckmeldung stornieren

Bestaetigte Sequenz fuer den Referenzfall `FA24FK00126 / Pos. 10 / RP.00010`:

1. `PW22000J *LOADNEW` mit `STORNO=J`, `ARAKKZ=MK`, FA, Materialposition und Artikel.
2. `PW22000J *STON` mit `ARAKKZ=M*`.
3. `PW22021R *GETHDR` und `*FIRSTLIST`.
4. Die zu stornierende Rueckmeldung wird **nicht nach "letzter Buchung"**, sondern eindeutig ueber den Listeneintrag bestimmt. Im Referenzfall:
   - `ARFIRM=103`
   - `ARRMNR=33806`
   - `ARRMZT=11.59.48`
   - `ARFAUN=FA24FK00126`
   - `ARYRML=2026-09-18`
   - Materialposition 10
   - Beschreibung `RP.00010 20.16 kg`
   - Quelle `RP.00010 EOS1 RP00010MIX_20260909_140218`.
5. `PW22021R *STORNO` mit dem exakten Rueckmeldeschluessel.
6. Die erfolgreiche HTTP-Antwort kann nur aus der XML-Deklaration bestehen. Dies gilt **nicht** als Erfolgsbeweis.
7. Danach wird die Rueckmeldeliste erneut gelesen; der exakt stornierte Schluessel darf nicht mehr vorhanden sein.
8. Im Referenzfall fiel die FA-Materialposition von `AMMATV=20,160 / AMMPST=9` auf `AMMATV=0 / AMMPST=0`.
9. Der Tank `EOS1` mit derselben Mix-Charge stieg von 119,209 kg auf 139,369 kg, also exakt um 20,160 kg.
10. Anschliessend war die normale bereits bestaetigte MK-Sequenz wieder zulaessig. Eine neue MK mit 10,500 kg reduzierte den Tank auf 128,869 kg.

Verbindliche Implementierungsregel:

- Die Oxaion-Standard-Stornoliste `PW22021R` liefert bereits nur gueltige/stornierbare Rueckmeldungen. Fuer die automatische Jobabbruch-Korrektur muessen jedoch FA, Materialposition, Artikel, urspruengliche Menge, **Tanklager und Mix-Charge** der Originalrueckmeldung mit dem aktuell gescannten Tank uebereinstimmen.
- Eine Rueckbuchung einer alten FA-Rueckmeldung auf einen Tank, dessen Mix-Charge sich inzwischen geaendert hat, ist fachlich nicht zulaessig und wird technisch gesperrt.
- Die Pruefung erfolgt bereits unmittelbar nach dem FA-Scan ueber einen rein lesenden Aufbau der Oxaion-Stornoliste. Bei Charge-/Tankabweichung wird die Mengeneingabe nicht freigegeben; Meldung an den Bediener: Fertigungsauftrag kann nicht automatisch auf diesen Tank rueckgebucht werden, da sich die Tankcharge seit der urspruenglichen FA-Buchung geaendert hat. Fall in Oxaion pruefen und gegebenenfalls manuell ueber Lagerbelege korrigieren.
- Dieselbe Quellpruefung wird unmittelbar vor dem echten `PW22021R *STORNO` nochmals serverseitig ausgefuehrt.
- Nach dem Storno bleiben FA-Zustand und die exakte Rueckbuchung auf denselben Tank/dieselbe Mix-Charge zwingende Erfolgsnachweise.
- Nach `*STORNO` muessen Rueckmeldeliste, FA-Materialzustand und Tank/Mix **alle** den erwarteten Zustand beweisen, bevor eine neue MK gesendet wird.
- Bei unklarem Stornoausgang kein Blind-Retry und keine neue MK.

### 6.2 I2 - Schwund

Bestaetigt:

- Buchungsschluessel `I2`
- Oxaion-Bezeichnung `Bestandskorr. Abgang (Schwund)`
- Lagerbeleg ueber `LB20100J` / Position ueber `LB20115J`
- Persistierung ueber `LB20110R *UPD`
- Fensterzustand `TCODE=WIN2` / `LB20115J *LOADWIN2`
- Referenzbuchung: EOS1 / RP.00010 / Mix `RP00010MIX_20260909_140218` / 0,001 kg
- Tankbestand reduzierte sich exakt von 128,869 kg auf 128,868 kg
- im STAGING-Mitschnitt verwendete Kostenstelle: `6000`;
- `LBKLAS=J` bleibt die notwendige Freigabe des Buchungsschluessels als Lagerbuchung. Die zwischenzeitlich im Test gesetzte Vorbelegung `LBSKSB=0050000` war **keine fachliche Voraussetzung** fuer FAM und wurde spaeter als falsche Vorbelegung erkannt. `LBSKSB` darf fuer I2 leer sein und wird von der WebApp nicht mehr als Freigabekriterium verwendet.
- Die Kostenstelle bleibt fuer den FAM-Ablauf festgelegt (I2 `PSKSTL=6000`, I1 `PSKSTL=5100`). Der Geschaeftsbereich darf aber nicht vor dem ersten `PUTNEW` pauschal mit `21` belegt werden. Im erfolgreichen Mitschnitt wird nach `KST0001`/`KST1260` die Kostenstellen-F4-Liste `LB20115J *F4 -> US11001R` geoeffnet und der eindeutige Paarwert uebernommen: I2/6000 -> `PSWERK=03` (`Allgemeine Produktionskosten`), I1/5100 -> `PSWERK=21` (`3D-Druck`).
- Android-Live-Tests vom 29.09.2026: Sowohl vor als auch nach dem zwischenzeitlichen expliziten Setzen von `TX_B1SB01` brach der erste I2-`LB20115J *PUTNEW` in `entryChkIDNR04` mit NullPointerException ab. Der erneute Feld-fuer-Feld-Abgleich mit dem erfolgreichen 18.09.-JET zeigt den eigentlichen Ablaufunterschied: Der erste I2-Request hat `PSWERK`, `PSKSTL` und `TX_B1SB01` leer. Oxaion antwortet mit `KST0001`; danach liefert die F4-Kostenstellenliste fuer 6000 den Geschaeftsbereich 03. Erst der folgende erfolgreiche `PUTNEW` liefert `TCODE=WIN2` und `TX_B1SB01=2`. Die WebApp spielt nun genau diese Reihenfolge nach und validiert die von Oxaion abgeleitete Richtung vor dem Persistieren.

### 6.3 I1 - positiver Mehrbestand

Bestaetigt:

- Buchungsschluessel `I1`
- Oxaion-Bezeichnung `Bestandskorrektur Zugang`
- dieselbe Lagerbeleglogik mit `TCODE=WIN2` / `LOADWIN2`
- Referenzbuchung: EOS1 / RP.00010 / dieselbe Mix-Charge / 0,002 kg
- Tankbestand erhoehte sich exakt von 128,868 kg auf 128,870 kg
- im STAGING-Mitschnitt verwendete Kostenstelle: `5100`;
- waehrend des Mitschnitts wurde fuer I1 `LBKLAS=J` gesetzt. Die WebApp prueft diese Freigabe vor der Buchung;
- nach Kostenstelle 5100 liefert der Referenzfall `VEP1804` (`Preis fehlt`). Der Mitschnitt zeigt anschliessend `US11600J *READ` fuer den Artikel und die Uebernahme von `TLDNPR` als `TX_BRPR` (Referenzwert 29,29030). Die WebApp liest diesen Preis deshalb bei `VEP1804` dynamisch aus Oxaion; der Referenzwert wird nicht hart codiert.

Die Kostenstellen 6000/5100 sind durch diesen **FAM-STAGING-Ablauf** bestaetigt. Auch die im Referenzmitschitt aufgeloesten Paare 03/6000 (I2) und 21/5100 (I1) sind STAGING-spezifisch. Die WebApp liest deshalb den Geschaeftsbereich ueber die Oxaion-F4-Liste statt ihn hart zu codieren. Die Kostenstellen selbst duerfen bei spaeterem Firmen-/Produktivwechsel nicht ungeprueft uebernommen werden.

### 6.4 Implementierter Tank-Auslagerungsablauf

Ab dieser Umsetzung gilt:

1. Tank scannen und Systembestand `Qsys` lesen.
2. Physisch ausgelagerte Netto-Pulvermenge `Qphys` wiegen.
3. Beide Werte werden fuer die Oxaion-Mengenlogik auf 0,001 kg normalisiert. Es gibt damit noch **keine zusaetzliche physikalische Waagentoleranz**.
4. `Qphys = Qsys`: keine Korrektur.
5. `Qphys < Qsys`: Differenz als I2 buchen.
6. `Qphys > Qsys`: Differenz als I1 buchen.
7. Nach I1/I2 Tank/Mix erneut lesen. Nur bei exakt `Qphys` wird fortgesetzt.
8. Danach die bereits bestaetigte LF/LE-Umlagerung ueber genau `Qphys`.
9. I1/I2 und LF/LE sind getrennte, aber korrelierte Teiltransaktionen. Der Mitschnitt beweist **nicht**, dass Korrektur und LF sicher in demselben Lagerbeleg automatisiert werden koennen.

### 6.5 Offener Punkt Waage

Weiter offen bleibt ausschliesslich eine moegliche fachliche Toleranz oberhalb der technischen 0,001-kg-Normalisierung. Solange keine Waagenaufloesung/Toleranz festgelegt ist, fuehrt jede Abweichung ab 0,001 kg zu I1 oder I2.
