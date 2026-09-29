# Separate Tankvorgaenge

Stand: 16.09.2026

Diese Datei dokumentiert die verbindliche Aufteilung der Pulververwaltung in separate Bedien- und Transaktionsvorgaenge. Die allgemeinen Regeln zu Personal-Session, Oxaion-Revalidierung, eindeutiger `clientOperationId`, Idempotenz, Fehlerbehandlung und Kein-Blind-Retry bleiben unveraendert.

## Sichtbare Vorgaenge

Nach erfolgreicher Mitarbeiter-Anmeldung waehlt der Bediener einen eigenstaendigen Vorgang:

- `Pulver nachfuellen`
- `Pulver aus Tank auslagern`
- `Neues Pulver in Tank fuellen`
- `Pulver auf Fertigungsauftrag buchen`
- `Korrekturbuchung Fertigungsauftrag (Jobabbruch)`
- rein lesend: `Lagerbestand ansehen`

Ein Pulverwechsel kann organisatorisch aus Auslagern und anschliessender Neubefuellung bestehen. Die WebApp darf diese beiden Vorgaenge nicht als eine atomare Oxaion-Transaktion vortaeuschen.

Die uebergeordnete Navigation und die aktuelle Android-Kopfzeile sind in `docs/AUTHENTICATED_HEADER_UI.md` und `docs/UX_OPTIMIZATIONS_2026-09-09.md` beschrieben.

## Pulver nachfuellen

Der bestehende, bereits live bestaetigte Nachfuellprozess bleibt fachlich unveraendert:

- angemeldeter Mitarbeiter;
- Maschinentank per QR;
- eindeutiger positiver Tankbestand bestimmt Artikel und aktuelle Mix-Charge;
- Nachfuellcharge `Artikel+++Charge` scannen;
- Oxaion-Quelle/Lagerplatz ermitteln;
- Menge bewusst eingeben;
- neue Mix-Charge nach bestehender Nachfuelllogik erzeugen;
- vor dem Schreiben Mitarbeiter, Tank und Quellen erneut validieren;
- Ergebnis exakt verifizieren und bei unklarem Ausgang nicht blind wiederholen.

Details stehen in den bestehenden Nachfuell-/Mix-Dokumenten.

## Pulver aus Tank auslagern

Bedienablauf:

1. Mitarbeiter ist angemeldet.
2. Maschinentank per Tank-QR scannen.
3. Aktuellen Tankbestand vollstaendig und eindeutig aus Oxaion lesen.
4. Artikel, aktuelle Charge und komplette Systemmenge `Qsys` anzeigen.
5. Pulver aus dem Tank entnehmen und die tatsaechlich ausgelagerte Netto-Pulvermenge `Qphys` wiegen.
6. Systemmenge, gewogene Menge und Differenz sichtbar gegenueberstellen.
7. Ziel-Lagerort und internen Ziel-Lagerplatz ueber die AJAX-Auswahl waehlen.
8. Vor einer schreibenden Buchung Tankbestand, Mitarbeiter, Lagerort und Lagerplatz erneut serverseitig validieren.
9. Falls `Qphys` und `Qsys` nach der noch festzulegenden Waagen-/Rundungsregel abweichen, muss zuerst der Tankbestand auf den physisch festgestellten Bestand korrigiert und danach erneut gelesen werden.
10. Erst wenn der Oxaion-Tankbestand der physisch bestaetigten Menge entspricht, wird die gewogene Menge mit der bestaetigten First-LF-/LF-LE-Logik ausgelagert.
11. Erst nach eindeutigem `SUCCESS` und final verifizierter LF/LE-Auslagerung fragt die App: `Etiketten drucken?`.
12. Bei `Ja` wird die positive ganze Stückzahl abgefragt und nochmals bestätigt; danach wird der am 29.09.2026 aufgezeichnete Oxaion-Lageretikettendruck auf der eindeutigen LE-Zielbewegung gestartet.
13. Etikettendruck und Materialbuchung sind getrennte korrelierte Operationen. Ein Druckfehler ändert den erfolgreichen Tank-Out nicht.

Verbindlich fuer die Mengenlogik:

- `Delta = Qphys - Qsys`.
- Fuer den aktuellen STAGING-Stand werden Oxaion-Mengen auf 0,001 kg normalisiert. `Delta = 0` nach dieser Normalisierung: keine Bestandskorrektur, anschliessend `LF`/`LE` der gewogenen Menge. Eine zusaetzliche physikalische Waagentoleranz bleibt offen.
- `Delta < 0`: Differenz ist Schwund. Die Differenz muss vor dem Transfer auf genau dem Tankartikel und der aktuellen Mix-Charge als negativer Bestandskorrekturvorgang gebucht werden.
- `Delta > 0`: positiver Mehrbestand. Die Differenz muss vor dem Transfer auf genau dem Tankartikel und der aktuellen Mix-Charge als positiver Bestandskorrekturvorgang gebucht werden.
- Nach jeder Korrektur wird der Tankbestand erneut aus Oxaion gelesen und muss der physisch gewogenen Menge entsprechen, bevor `LF` gestartet wird.
- Ist der Ausgang einer Korrektur unklar, darf kein `LF` gestartet werden.
- Ist die Korrektur erfolgreich, aber der anschliessende Transfer unklar, wird die Korrektur nicht automatisch rueckgaengig gemacht; der Gesamtvorgang geht in Klaerung.

Seit dem FAM-STAGING-JET-Mitschnitt vom 18.09.2026 sind `I1 = Bestandskorrektur Zugang` und `I2 = Bestandskorr. Abgang (Schwund)` technisch bestaetigt. Verbindlicher aktueller FAM-Stand seit 29.09.2026: **I1 und I2 verwenden beide `PSWERK=21` und `PSKSTL=5100`**. Die historische I2-Kombination 03/6000 ist damit für die WebApp abgelöst. I1/I2 werden als eigene korrelierte Teiltransaktion gebucht; erst nach exakt bestaetigtem Tankbestand wird die vorhandene LF/LE-Umlagerung gestartet.

Verbindlich fuer die Zielauswahl:

- Die Trefferliste kommt aus der freigegebenen rein lesenden Oxaion-SQL-Suche.
- Fuer die Lagerplatzsuche werden Lagerplaetze des gewaehlten Lagerorts unabhaengig von RP.*-Bestand oder aktuellem Bestand angeboten.
- Frei eingetippter Text ist kein kanonischer Buchungsschluessel. Erst die Auswahl eines Backend-Treffers setzt Lagerort/Lagerplatz.
- Vor der schreibenden Buchung bleiben die bestaetigten Oxaion-F4-Pruefungen `US16601R` beziehungsweise `LB13210R` verbindlich.
- Auf Android startet die Auswahl ohne geoeffnete Bildschirmtastatur. Die Tastatur wird nur ueber das sichtbare Tastatur-Symbol bewusst aktiviert.

Der First-LF-Fix und die noch notwendige Live-STAGING-Bestaetigung sind in `docs/STAGING_TEST_FIXES_2026-09-08.md` und `docs/OPEN_POINTS.md` festgehalten. Die neue Wiegungs-/Korrekturlogik sowie die dafuer noch erforderlichen Mitschnitte stehen in `docs/JOB_ABORT_CORRECTION_AND_TANK_WEIGHING_2026-09-16.md`.

Der optionale Etikettendruck nach erfolgreicher Auslagerung ist in `docs/TANK_OUT_LABEL_PRINT_2026-09-29.md` dokumentiert. Die Bediener-Stückzahl wird als `EK99103R.MENGE` übergeben; die Druckwarteschlange wird aus der aktuellen Oxaion-`MN50100J *GETTABLE`-Konfiguration übernommen und nicht hart codiert.

## Neues Pulver in Tank fuellen

Dieser Vorgang ist nur fuer einen von Oxaion eindeutig als leer bestaetigten Maschinentank vorgesehen.

Bedienablauf:

1. leeren Tank scannen und online bestaetigen;
2. erste Pulvercharge `Artikel+++Charge` scannen;
3. Artikel der ersten gueltigen Charge wird Tankartikel;
4. weitere Chargen muessen denselben Artikel haben;
5. konkrete positive Oxaion-Bestandsposition waehlen beziehungsweise eindeutig ermitteln;
6. aktuell verfuegbare Menge dieser Position wird als editierbarer **Vorschlag** in das Mengenfeld uebernommen;
7. weitere Quellen koennen hinzugefuegt oder wieder entfernt werden;
8. vor dem Buchen werden leerer Tank, Mitarbeiter und alle finalen Quellen erneut validiert;
9. die Mix-Entscheidung wird aus der **finalen** Quellenliste neu berechnet.

### Dynamische Mix-Regel

Die fruehere Regel `immer neue Mix-Charge` ist ersetzt.

- Genau eine bereits eingelagerte Mix-Charge -> diese Charge bleibt beim `LF -> LE` in den leeren Tank erhalten; keine neue Mix-Charge.
- Genau eine Nicht-Mix-Charge -> neue Mix-Charge.
- Eine Mix-Charge plus mindestens eine weitere Charge -> neue Mix-Charge.
- Mehrere Quellen generell -> neue Mix-Charge.
- Wird eine zweite Quelle vor dem Buchen wieder entfernt und bleibt genau eine vorhandene Mix-Charge uebrig, gilt wieder `Mix-Charge bleibt`.

Die Entscheidung wird serverseitig erneut aus dem finalen Request berechnet; die UI-Anzeige allein ist nicht vertrauenswuerdig.

Die bestaetigten Oxaion-Bausteine und die genauen Transferketten stehen in `docs/FILL_NEW_OXAION_SEQUENCE.md`.

### Fehlscan

Eine Folgecharge mit falschem Artikel wird nicht in den Vorgang uebernommen. Die bereits fuer Nachfuellen verwendete bildfuellende blockierende Fehlscan-Meldung wird auch hier verwendet und muss bewusst bestaetigt werden.

## Pulver auf Fertigungsauftrag buchen

Dieser Vorgang bucht den kumulierten tatsaechlichen Pulververbrauch auf genau einen Fertigungsauftrag.

Bedienablauf:

1. Mitarbeiter anmelden und Maschinentank scannen.
2. Tank muss genau einen plausiblen positiven Pulverbestand liefern.
3. Fertigungsauftrag im Format `Rohmaterial+++Fertigungsauftrag+++Maschinen-ID` scannen.
4. Rohmaterial aus dem QR muss dem Tankartikel entsprechen.
5. Exakt eine Materialposition fuer Fertigungsauftrag und Artikel ermitteln.
6. `AMMATB` als Soll, `AMMATV` als bereits tatsaechlich gebucht und `AMMPST` als Status anzeigen.
7. Das Feld `Pulver Verbrauch eingeben` ist mit dem Sollwert vorbelegt, deutlich als Vorschlag gekennzeichnet und vom Bediener mit dem tatsaechlichen Ist-Verbrauch zu pruefen beziehungsweise zu korrigieren.
8. Die neu zu buchende Differenz ist `Ist-Verbrauch - AMMATV`.
9. Diese Differenz muss positiv sein und darf den aktuellen Tankbestand nicht ueberschreiten.
10. Direkt vor der MK-Buchung werden Tank, Mitarbeiter und unveraenderte Materialposition erneut validiert.
11. Nach `PW22031J *PUTNEW` wird nur lesend auf den exakten Endzustand geprueft; die Buchung wird niemals automatisch wiederholt.

Wichtige Bedienfelder und Buchungsbestaetigungen schreiben `Fertigungsauftrag` aus. Details stehen in `docs/FA_CONSUMPTION_PROCESS.md` und `docs/UX_OPTIMIZATIONS_2026-09-09.md`.

## Korrekturbuchung Fertigungsauftrag (Jobabbruch)

Dieser Vorgang ist fuer den Fall vorgesehen, dass bereits beim Druckstart Pulver auf den Fertigungsauftrag gebucht wurde, der Druckjob danach aber abbricht und der tatsaechliche Verbrauch kleiner als die urspruenglich gebuchte Menge ist.

Fachlich verbindliche Reihenfolge:

1. Mitarbeiter anmelden.
2. Maschinentank scannen.
3. Fertigungsauftrag scannen und exakte Pulver-Materialposition ermitteln.
4. Die urspruengliche zu korrigierende Materialrueckmeldung eindeutig ermitteln.
5. FA-Zustand, aktuell gebuchten Verbrauch, Tanklager und Mix-Charge anzeigen.
6. Originalrueckmeldung als echten Oxaion-Storno stornieren.
7. Storno rein lesend verifizieren; insbesondere FA-Materialposition und Rueckbuchung auf Tank/Mix-Charge pruefen.
8. Erst nach eindeutig erfolgreichem Storno den tatsaechlichen Ist-Verbrauch erfassen.
9. Den korrekten Ist-Verbrauch mit der bestaetigten normalen FA-Materialrueckmeldung neu buchen, sofern der nach dem Storno gelesene Oxaion-Zustand diese Rueckmeldung zulaesst.
10. Final FA-Materialposition, Tankbestand und Mix-Charge erneut lesen und exakt verifizieren.

Der Vorgang ist eine gemeinsame WebApp-Gesamttransaktion mit mindestens zwei schreibenden Teilschritten: Storno und korrigierte neue Rueckmeldung. Ein unklarer Stornoausgang blockiert den zweiten Schreibschritt vollstaendig. Kein Teilschritt darf blind wiederholt oder automatisch durch eine erfundene Gegenbuchung kompensiert werden.

Seit dem FAM-STAGING-JET-Mitschnitt vom 18.09.2026 sind sowohl der konkrete Oxaion-Stornoablauf als auch die anschliessende korrigierte MK technisch bestaetigt und in der WebApp schreibend umgesetzt. Die App waehlt die Originalrueckmeldung nur bei genau einem Treffer anhand von FA, Materialposition, Artikel, urspruenglicher Menge, Tanklager und Mix-Charge aus. Nach `PW22021R *STORNO` muessen Rueckmeldeliste, FA-Materialzustand und dieselbe Tank-Mix-Charge den erwarteten Zustand beweisen, bevor die neue MK gestartet wird. Details stehen in `docs/JOB_ABORT_CORRECTION_AND_TANK_WEIGHING_2026-09-16.md`.

## Gemeinsame Buchungsdarstellung

- Sichtbare Schrittueberschriften sind nicht nummeriert.
- Nach der finalen Buchungsbestaetigung wird ein blockierender Lade-Spinner angezeigt, bis ein Ergebnis beziehungsweise eine Recovery-Meldung vorliegt.
- Erfolgsmeldungen sind auf Deutsch.
- Nach eindeutigem Erfolg werden die Eingaben des abgeschlossenen Vorgangs geleert; die Mitarbeiteranmeldung bleibt bestehen.
- Bei `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED` gilt weiterhin ausdruecklich: nicht erneut buchen, Status klaeren.

## Wiederverwendete bestaetigte Bausteine

Ohne fachliche Aenderung werden wiederverwendet:

- NFC-Login beziehungsweise Personalnummer + SYNCOS-Passwort;
- serverseitige Personal-Session und erneute Oxaion-Personalpruefung vor Writes;
- Maschinentank-QR und Tank-Whitelist;
- `LB30230R`-basierter Tankbestand;
- EFA01/EFA02 als visuelle Such-/Erkennungshilfe;
- Chargen-QR `Artikel+++Charge`;
- Fertigungsauftrag-QR `Rohmaterial+++Fertigungsauftrag+++Maschinen-ID`;
- Oxaion-geführte Quellauflösung `LB30340R`/`LB30430R` und `LAG1626`-Sonderfall;
- dieselbe Charge auf unterschiedlichen exakten Oxaion-Bestandspositionen;
- klare Fehler-/Recovery-Meldungen;
- Kein-Blind-Retry-Regeln aus `docs/ERROR_HANDLING.md`.
