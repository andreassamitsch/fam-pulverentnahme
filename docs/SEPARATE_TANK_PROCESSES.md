# Separate Tankvorgaenge

Stand: 09.09.2026

Diese Datei dokumentiert die verbindliche Aufteilung der Pulververwaltung in separate Bedien- und Transaktionsvorgaenge. Die allgemeinen Regeln zu Personal-Session, Oxaion-Revalidierung, eindeutiger `clientOperationId`, Idempotenz, Fehlerbehandlung und Kein-Blind-Retry bleiben unveraendert.

## Sichtbare Vorgaenge

Nach erfolgreicher Mitarbeiter-Anmeldung waehlt der Bediener einen eigenstaendigen Vorgang:

- `Pulver nachfuellen`
- `Pulver aus Tank auslagern`
- `Neues Pulver in Tank fuellen`
- `Pulver auf Fertigungsauftrag buchen`
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
4. Artikel, aktuelle Charge und komplette Systemmenge anzeigen.
5. Ziel-Lagerort und internen Ziel-Lagerplatz ueber die AJAX-Auswahl waehlen.
6. Vor der Buchung Tankbestand, Mitarbeiter, Lagerort und Lagerplatz erneut serverseitig validieren.
7. Die gesamte bestaetigte Tankmenge wird mit der bestaetigten First-LF-/LF-LE-Logik ausgelagert.

Verbindlich fuer die Zielauswahl:

- Die Trefferliste kommt aus der freigegebenen rein lesenden Oxaion-SQL-Suche.
- Fuer die Lagerplatzsuche werden Lagerplaetze des gewaehlten Lagerorts unabhaengig von RP.*-Bestand oder aktuellem Bestand angeboten.
- Frei eingetippter Text ist kein kanonischer Buchungsschluessel. Erst die Auswahl eines Backend-Treffers setzt Lagerort/Lagerplatz.
- Vor der schreibenden Buchung bleiben die bestaetigten Oxaion-F4-Pruefungen `US16601R` beziehungsweise `LB13210R` verbindlich.
- Auf Android startet die Auswahl ohne geoeffnete Bildschirmtastatur. Die Tastatur wird nur ueber das sichtbare Tastatur-Symbol bewusst aktiviert.

Der First-LF-Fix und die noch notwendige Live-STAGING-Bestaetigung sind in `docs/STAGING_TEST_FIXES_2026-09-08.md` und `docs/OPEN_POINTS.md` festgehalten.

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
