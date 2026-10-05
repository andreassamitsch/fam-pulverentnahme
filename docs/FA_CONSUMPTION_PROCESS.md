# Pulververbrauch auf Fertigungsauftrag

Stand: 08.09.2026

Diese Datei dokumentiert die aktuelle fachliche und technisch nachgewiesene Grundlage fuer den Bedienvorgang `Pulver auf Fertigungsauftrag buchen`.

Der Vorgang ist eigenstaendig und wird nicht mit Nachfuellen, Tankauslagerung oder Neubefuellung verkettet. Die allgemeinen Regeln aus `AGENTS.md`, `docs/ERROR_HANDLING.md`, `docs/QR_CODE_WORKFLOW.md` und der Mitarbeiter-Authentifizierung gelten unveraendert.

## Fachlicher Zweck

Nach dem realen Pulververbrauch fuer einen Fertigungsauftrag erfasst der Bediener den **aktuellen tatsaechlichen Gesamtverbrauch (Ist-Verbrauch)** des Pulvers. Die App bucht diesen Wert ueber die bestaetigte Oxaion-PPS-Materialrueckmeldung auf die richtige Materialposition des Fertigungsauftrags.

Wichtige Korrektur vom 08.09.2026:

- Das Eingabefeld ist **kein Feld fuer einen zusaetzlichen Verbrauch**.
- Der Bediener gibt den aktuellen **Ist-Verbrauch gesamt** ein.
- `PWAMAP.AMMATV` bleibt die Quelle fuer den bereits in Oxaion gebuchten Ist-Verbrauch.
- Die neu zu buchende Differenz ergibt sich aus `eingegebener Ist-Verbrauch - bereits gebuchter AMMATV`.
- Der eingegebene Ist-Verbrauch wird nicht nochmals zu `AMMATV` addiert.

Beispiel:

```text
bereits gebucht AMMATV: 1,000 kg
Bedienereingabe Ist:     2,700 kg
neu zu buchende Differenz: 1,700 kg
Ziel-AMMATV nach MK:      2,700 kg
```

## Bedienablauf

1. Mitarbeiter anmelden.
2. Maschinentank scannen.
3. Aktuellen Tankbestand eindeutig aus Oxaion lesen.
4. Fertigungsauftrag scannen.
5. Rohmaterialartikel aus dem FA-QR gegen den aktuell im Tank vorhandenen Artikel pruefen.
6. Materialpositionen des Fertigungsauftrags aus Oxaion lesen und die exakt zum Pulverartikel passende Position ermitteln.
7. Soll-Materialbedarf, bereits gebuchten Ist-Verbrauch und Materialpositionsstatus anzeigen.
8. Nur wenn genau eine passende und fuer die normale Materialrueckmeldung zulaessige Position vorhanden ist, den aktuellen **Pulver Ist-Verbrauch in kg** eingeben lassen.
9. Die App zeigt daraus den neu zu buchenden Differenzbetrag an.
10. Unmittelbar vor einer produktiven Buchung Tankbestand, Mitarbeiter, Materialposition, bereits gebuchten Ist-Verbrauch und Status erneut serverseitig validieren.
11. Die bestaetigte Oxaion-PPS-Materialrueckmeldung ausloesen.
12. Nach der Oxaion-Antwort die Materialposition ausschliesslich lesend erneut pruefen. Die Schreiboperation wird niemals automatisch wiederholt.

Die Mitarbeiterbezeichnung fuer Schritt 8 lautet `Pulver Verbrauch eingeben`. In der Anzeige wird der eingegebene Wert als `Ist-Verbrauch` bezeichnet.

## Maschinentank

Der Tank wird physisch per Tank-QR identifiziert. Der QR enthaelt nur den Oxaion-Tanklagerort, beispielsweise:

```text
EOS1
```

Der gescannte Wert muss gegen die gepflegte Tankliste validiert werden. Danach wird der bestaetigte `LB30230R`-Leseablauf verwendet.

Fuer diesen Vorgang ist nur ein eindeutig positiver Tankbestand zulaessig:

- genau eine positive Position;
- Mengeneinheit `KGM`;
- kein negativer Bestand;
- eindeutiger Artikel;
- eindeutige aktuelle Mix-Charge.

Ein leerer, mehrdeutiger oder fachlich unplausibler Tank blockiert den Vorgang.

Mindestens angezeigt werden:

- tatsaechlicher Tanklagerort;
- Pulverartikel und Artikelbezeichnung;
- aktuelle Mix-Charge;
- aktueller Tankbestand;
- EFA01/EFA02-Erkennungsfarben, soweit aus dem bestaetigten Leseweg verfuegbar.

## Fertigungsauftrag-QR

Das Format bleibt:

```text
Rohmaterialartikel+++Fertigungsauftrag+++Maschinen-ID
```

Beispiel:

```text
RP.00010+++FA25FK00001+++EP-M650-1
```

Der Rohmaterialartikel aus dem FA-QR muss exakt zum aus Oxaion gelesenen Tankartikel passen. Ein nicht passender Artikel wird abgelehnt und nicht in den Vorgang uebernommen.

Die Maschinen-ID im FA-QR ist weiterhin Plan-/Auftragsinformation und nicht der Oxaion-Tanklagerort. Eine kurzfristig abweichende tatsaechliche Maschine bleibt fachlich moeglich; die noch offene Maschinen-ID-zu-Tank-Referenz wird nicht erfunden.

## Materialposition technisch ermitteln

Der Mitschnitt `fa materialpos finden und materialverbrauch.7z` vom 07.09.2026 bestaetigt, dass die Materialpositionen eines Fertigungsauftrags in Oxaion mit Artikelbezug gelesen werden koennen.

Fuer die WebApp sind insbesondere relevant:

```text
PWAMAP.AMPOSN   Materialposition
PWAMAP.AMIDNK   Komponenten-/Pulverartikel
PWAMAP.AMMATB   Materialbedarf (Soll)
PWAMAP.AMMATV   Materialverbrauch (tatsaechlich bereits gebucht)
PWAMAP.AMMPST   Materialpositionsstatus
```

Im Referenzfall liefert die Liste fuer `FA25FK00001` genau eine Position zum Pulverartikel `RP.00010`:

```text
AMPOSN = 10
AMIDNK = RP.00010
AMMATB = 15,410 kg
AMMATV = 15,420 kg
AMMPST = 9
```

`PW20201J *READ` auf Firma + Fertigungsauftrag + Materialposition + Artikel bestaetigt dieselben Werte und liefert zusaetzlich:

```text
TX_MPST = Komplett abgebucht
AMMEKZ  = KGM
```

Verbindlich:

- Materialposition nie fest codieren.
- Nach dem FA-Scan die Oxaion-Materialpositionen lesen.
- Nur Positionen beruecksichtigen, deren Artikel exakt dem gescannten Tank-/FA-Pulverartikel entspricht.
- Genau eine passende Position ist Voraussetzung fuer eine automatische Zuordnung.
- Keine passende Position oder mehrere passende Positionen blockieren die automatische Buchung und fuehren zur manuellen Klaerung.
- Nach Ermittlung der Position wird diese mit `PW20201J *READ` nochmals exakt gelesen.

## Soll und bereits gebuchter Ist-Verbrauch

`ARVBME` aus dem Rueckmeldedialog ist **nicht** die verlaessliche Quelle fuer die bereits gebuchte Menge. Im Dialog kann dieses Feld mit einem kalkulierten beziehungsweise aus der Stueckliste abgeleiteten Wert vorbelegt sein.

Fuer Anzeige und Doppelbuchungspruefung sind die Materialpositionsfelder massgeblich:

```text
AMMATB = Soll-/Materialbedarf
AMMATV = tatsaechlich bereits gebuchter Materialverbrauch
```

Die Mitarbeiteransicht zeigt daher vor der Eingabe beispielsweise:

```text
Materialposition:          10
Soll laut FA/Stueckliste:  15,410 kg
Bereits gebucht:           10,000 kg
Status:                     0 - Eingeplant / Reserviert

Pulver Verbrauch eingeben
Ist-Verbrauch:             15,420 kg
Neu zu buchen:              5,420 kg
```

## Verbrauchseingabe

Der eingegebene Wert ist eine bewusste Bedienereingabe in kg und stellt den aktuellen **Ist-Verbrauch gesamt** dar.

Verbindlich:

- keine Vorbelegung;
- Ist-Verbrauch muss groesser als `0` sein;
- Ist-Verbrauch muss groesser als der unmittelbar zuvor gelesene bereits gebuchte Wert `AMMATV` sein, solange die normale MK-Buchung eine positive neue Verbrauchsdifferenz erzeugen soll;
- die neu zu buchende Differenz `Ist - AMMATV` darf den aktuell gelesenen Tankbestand nicht ueberschreiten;
- bereits gebuchter Verbrauch `AMMATV` wird separat angezeigt und niemals automatisch zur Bedienereingabe addiert;
- der rechnerische Differenzbetrag ist Bedienhilfe und wird vor der Buchung erneut gegen den Oxaion-Zustand abgesichert.

Die aktuelle STAGING-API verwendet aus Kompatibilitaetsgruenden im Request noch den technischen Property-Namen `additionalConsumptionKg`. Dieser Name ist **nicht mehr die fachliche Bedeutung des Feldes**. Der Wert wird ab 08.09.2026 als eingegebener Ist-Gesamtverbrauch behandelt. Eine spaetere API-Bereinigung darf nur mit kontrollierter Frontend-/Backend-Versionierung erfolgen.

## Schutz vor Doppelbuchung

Die WebApp darf sich nicht auf einen einmal gelesenen Wert verlassen.

Vor dem ersten schreibenden Oxaion-Aufruf muss das Backend mindestens erneut pruefen:

1. Personal-Session passt zum Vorgang.
2. Mitarbeiter ist weiterhin exakt in Oxaion bestaetigt.
3. Tanklagerort, Tankartikel, aktuelle Mix-Charge und Bestand entsprechen weiterhin dem vorbereiteten Zustand.
4. Fertigungsauftrag und Materialposition werden erneut gelesen.
5. `AMIDNK` entspricht weiterhin dem Pulverartikel.
6. `AMMATV` entspricht exakt dem Wert, der dem Bediener vor seiner Eingabe angezeigt wurde.
7. `AMMPST` entspricht weiterhin dem erwarteten Materialstatus.
8. Die neu zu buchende Differenz zwischen eingegebenem Ist-Verbrauch und bereits gebuchtem `AMMATV` ist positiv.
9. Der aktuelle Tankbestand reicht fuer genau diese Differenz aus.

Hat sich `AMMATV`, `AMMPST`, Tankartikel, Mix-Charge oder Tankbestand zwischen Anzeige und Buchung geaendert, wird **keine** neue Materialbuchung gestartet. Der Vorgang geht in `CONFLICT` beziehungsweise manuelle Klaerung.

Jeder produktive Vorgang besitzt eine eindeutige `clientOperationId` und eine serverseitige Transaktions-ID. Ein unklarer Ausgang darf nicht blind erneut gebucht werden.

## Normale Materialrueckmeldung `MK`

Der erfolgreiche Mitschnitt `pulver auf FA Rueckmelden` bestaetigt fuer eine normale Materialkomplettentnahme den Oxaion-Weg ueber:

```text
PW22000J
PW22031J
```

mit Rueckmeldetransaktion:

```text
ARAKKZ = MK
```

Die aktuelle Implementierung verwendet den eingegebenen Ist-Verbrauch als Zielwert der bestaetigten Rueckmeldemenge (`ARVBME2`). Sie addiert den Wert nicht nochmals auf `AMMATV`.

Der erfolgreiche Referenzfall fuer Materialposition `10` verwendete den Pulverartikel `RP.00010`, Lagerort `EOS1` und die aktuelle Mix-Charge. Nach der erfolgreichen Rueckmeldung wurde in `PW20201J *READ` der Materialverbrauch `AMMATV=15,420` und Status `AMMPST=9` bestaetigt.

Ein erneuter `MK`-Versuch bei `AMMPST=9` wird von Oxaion eindeutig abgelehnt:

```text
FCOD = AKK2638
Rückmeldetransaktion "MK" bei Materialstatus "9" nicht möglich
```

Die Oxaion-Meldung nennt fuer `MK` die Materialstatuswerte `0`, `1` und `8` als zulaessig.

Damit gilt:

- `AMMPST=9` ist kein Fall fuer eine normale erneute MK-Buchung.
- Bei Status 9 bleibt die normale Schaltflaeche gesperrt.
- Der bereits gebuchte Wert `AMMATV` bleibt sichtbar, damit eine versehentliche Doppelbuchung erkannt wird.

## Ergebnisverifikation nach MK

Nach `PW22031J *PUTNEW` darf die App die MK-Schreiboperation **nicht** wiederholen.

Die Materialposition wird in frischen Oxaion-Sessions ausschliesslich lesend erneut gelesen. Ein `SUCCESS` ist nur zulaessig, wenn der exakte erwartete Endzustand bestaetigt ist:

- gleiche Materialposition;
- `AMMATV` entspricht exakt dem vom Bediener eingegebenen Ist-Verbrauch;
- der bestaetigte erfolgreiche Referenz-Endstatus ist erreicht.

Wird dieser Endzustand nicht eindeutig bestaetigt, ist der Buchungsausgang unklar. Dann gilt die Kein-Blind-Retry-Regel aus `docs/ERROR_HANDLING.md`.

Eine spaetere rein lesende Statuspruefung, bei der der aktuelle Wert zufaellig dem erwarteten Wert entspricht, beweist ohne eindeutige WebApp-/Oxaion-Transaktionsreferenz nicht sicher, dass genau dieser WebApp-Vorgang die Aenderung erzeugt hat. Ein solcher Fall bleibt `MANUAL_REVIEW_REQUIRED`.

## Nachtraeglicher ungeplanter Verbrauch `MU`

Der vorhandene Mitschnitt zeigt den begonnenen Oxaion-Weg fuer:

```text
ARAKKZ = MU
TX_AKKZ = Material ungeplant
```

Der Mitschnitt wurde bewusst **vor Abschluss der MU-Buchung beendet**. Deshalb sind derzeit nicht produktiv bestaetigt:

- finaler `PW22031J *PUTNEW` fuer MU;
- konkrete vollstaendige Pflichtfeldkombination fuer eine erfolgreiche MU-Buchung;
- Ergebnis-/Verifikationsfolge nach erfolgreichem MU;
- belastbare Recovery-Logik fuer einen unklaren MU-Ausgang.

MU darf deshalb weiterhin nicht automatisch von der WebApp gebucht werden.

## Aktueller Implementierungs-/Teststatus

Technisch nachgewiesen und im aktuellen STAGING-Code abgebildet sind:

- Tankartikel und Mix-Charge aus dem gescannten Maschinentank lesen;
- FA-QR und Artikelabgleich;
- Materialposition mit Artikelbezug aus Oxaion lesen;
- Sollbedarf `AMMATB` lesen;
- tatsaechlich gebuchten Materialverbrauch `AMMATV` lesen;
- Materialstatus `AMMPST` und Statustext `TX_MPST` lesen;
- normale Rueckmeldetransaktion `MK` aus einem erfolgreichen Mitschnitt;
- eindeutige Oxaion-Ablehnung `AKK2638` fuer `MK` bei Materialstatus `9`;
- Pre-Write-Revalidierung des bisherigen `AMMATV` und Status;
- eingegebenen Ist-Verbrauch als Zielwert behandeln und die Differenz nur fuer die Tankbestandspruefung verwenden;
- nach der MK-Antwort ausschliesslich lesend auf den exakten Endzustand pruefen;
- bei unklarem Ergebnis keinen blinden Retry ausfuehren.

Noch offen beziehungsweise weiter live zu bestaetigen sind:

- den aktuellen kombinierten WebApp-Stand mit Ist-Gesamtverbrauch im STAGING-End-to-End-Test bestaetigen;
- vollstaendig erfolgreichen MU-Mitschnitt aufnehmen, bevor MU schreibend implementiert wird;
- eine eindeutige Oxaion-Transaktionsreferenz fuer automatische Eigentumszuordnung eines spaeter gelesenen FA-Zustands bestaetigen, falls Oxaion dafuer einen geeigneten Weg bietet.
