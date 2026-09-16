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

Vor einer produktiven Implementierung muss ein vollstaendiger JET-/HTTP-Mitschnitt eines realen MK-Stornos in STAGING vorliegen.

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

Die oxaion-Standarddokumentation nennt fuer diesen Zweck `I2 = Bestandskorr. Abgang (Schwund)`. Ob `I2` in der FAM-STAGING-Firma unveraendert vorhanden, als Dialogschluessel zugelassen und mit der gewuenschten Kontierung versehen ist, muss vor Implementierung in `US50000` und durch einen realen JET-Mitschnitt bestaetigt werden.

### Fall C: mehr Pulver als im System (`Qphys > Qsys`)

Die Differenz

`Mehrbestand = Qphys - Qsys`

muss zuerst als nachvollziehbare positive Bestandskorrektur **auf genau dem Tankbestand, Artikel und der aktuellen Mix-Charge** gebucht werden.

Erst nach eindeutig erfolgreicher Korrektur wird der Tankbestand erneut gelesen. Er muss danach der physisch gewogenen Menge entsprechen. Erst dann wird diese Menge mit `LF`/`LE` auf den ausgewaehlten Lagerort/Lagerplatz umgebucht.

Die oxaion-Standarddokumentation nennt fuer diesen Zweck `I1 = Bestandskorrektur Zugang`. Auch `I1` muss in der FAM-STAGING-Konfiguration und durch JET bestaetigt werden, bevor der Buchungsschluessel produktiv verwendet wird.

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
