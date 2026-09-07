# Pulververbrauch auf Fertigungsauftrag

Stand: 07.09.2026

Diese Datei dokumentiert die aktuelle spezifische fachliche und technisch nachgewiesene Grundlage fuer den Bedienvorgang `Pulver auf Fertigungsauftrag buchen`.

Der Vorgang ist bewusst eigenstaendig und wird nicht mit Nachfuellen, Tankauslagerung oder Neubefuellung verkettet. Die allgemeinen Regeln aus `AGENTS.md`, `docs/ERROR_HANDLING.md`, `docs/QR_CODE_WORKFLOW.md` und der Mitarbeiter-Authentifizierung gelten unveraendert.

## Fachlicher Zweck

Nach dem realen Pulververbrauch fuer einen Fertigungsauftrag soll der Bediener die zusaetzlich verbrauchte Menge aus dem aktuell verwendeten Maschinentank erfassen und ueber die bestaetigte Oxaion-PPS-Materialrueckmeldung auf die richtige Materialposition des Fertigungsauftrags buchen.

Der Bedienablauf ist:

1. Mitarbeiter anmelden.
2. Maschinentank scannen.
3. Aktuellen Tankbestand eindeutig aus Oxaion lesen.
4. Fertigungsauftrag scannen.
5. Rohmaterialartikel aus dem FA-QR gegen den aktuell im Tank vorhandenen Artikel pruefen.
6. Materialpositionen des Fertigungsauftrags aus Oxaion lesen und die exakt zum Pulverartikel passende Position ermitteln.
7. Soll-Materialbedarf, bereits gebuchten Ist-Verbrauch und Materialpositionsstatus anzeigen.
8. Nur wenn genau eine passende und fuer die normale Materialrueckmeldung zulaessige Position vorhanden ist, den zusaetzlichen tatsaechlichen Pulververbrauch in kg eingeben lassen.
9. Unmittelbar vor einer produktiven Buchung Tankbestand, Mitarbeiter, Materialposition, bereits gebuchten Ist-Verbrauch und Status erneut serverseitig validieren.
10. Die bestaetigte Oxaion-PPS-Materialrueckmeldung ausloesen.

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

Im Materialpositionsbestand sind fuer die WebApp insbesondere relevant:

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

Fuer die WebApp gilt deshalb:

- Materialposition nie fest codieren.
- Nach dem FA-Scan die Oxaion-Materialpositionen lesen.
- Nur Positionen beruecksichtigen, deren Artikel exakt dem gescannten Tank-/FA-Pulverartikel entspricht.
- Genau eine passende Position ist Voraussetzung fuer eine automatische Zuordnung.
- Keine passende Position oder mehrere passende Positionen blockieren die automatische Buchung und fuehren zur manuellen Klaerung.
- Nach Ermittlung der Position wird diese mit `PW20201J *READ` nochmals exakt gelesen.

## Soll und bereits gebuchter Verbrauch

Wichtige Korrektur vom 07.09.2026:

`ARVBME` aus dem Rueckmeldedialog ist **nicht** die verlaessliche Quelle fuer die bereits gebuchte Menge. Im Dialog kann dieses Feld mit einem kalkulierten beziehungsweise aus der Stueckliste abgeleiteten Wert vorbelegt sein.

Fuer die Anzeige und Doppelbuchungspruefung sind die Materialpositionsfelder massgeblich:

```text
AMMATB = Soll-/Materialbedarf
AMMATV = tatsaechlich bereits gebuchter Materialverbrauch
```

Die Mitarbeiteransicht soll daher vor jeder Eingabe deutlich zeigen, zum Beispiel:

```text
Materialposition:       10
Soll laut FA/Stueckliste: 15,410 kg
Bereits gebucht:          15,420 kg
Status:                    9 - Komplett abgebucht
```

Der Bediener gibt nicht einen neuen Gesamtwert ein, sondern den **zusaetzlichen tatsaechlichen Verbrauch**, der jetzt noch gebucht werden soll.

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
8. Der aktuelle Tankbestand reicht fuer die zusaetzliche Verbrauchsmenge aus.

Hat sich `AMMATV`, `AMMPST`, Tankartikel, Mix-Charge oder Tankbestand zwischen Anzeige und Buchung geaendert, wird **keine** neue Materialbuchung gestartet. Der Vorgang geht in `CONFLICT` beziehungsweise manuelle Klaerung.

Jeder produktive Vorgang besitzt eine eindeutige `clientOperationId` und eine serverseitige Transaktions-ID. Ein unklarer Ausgang darf nicht blind erneut gebucht werden.

## Normale Materialrueckmeldung `MK`

Der vorherige Mitschnitt `pulver auf FA Rueckmelden` bestaetigt fuer eine normale Materialkomplettentnahme den Oxaion-Weg ueber:

```text
PW22000J
PW22031J
```

mit Rueckmeldetransaktion:

```text
ARAKKZ = MK
```

Der erfolgreiche Referenzfall fuer Materialposition `10` verwendete den Pulverartikel `RP.00010`, Lagerort `EOS1` und die aktuelle Mix-Charge. Nach der erfolgreichen Rueckmeldung wurde in `PW20201J *READ` der Materialverbrauch `AMMATV=15,420` und Status `AMMPST=9` bestaetigt.

Der neue Mitschnitt zeigt gleichzeitig die wichtige Statusgrenze: Ein erneuter `MK`-Versuch auf derselben Materialposition bei `AMMPST=9` wird von Oxaion eindeutig abgelehnt:

```text
FCOD = AKK2638
Rückmeldetransaktion "MK" bei Materialstatus "9" nicht möglich
```

Die Fehlermeldung nennt als fuer `MK` zulaessige Materialstatuswerte `0`, `1` und `8`.

Damit gilt fuer die App:

- `AMMPST=9` ist kein Fall fuer eine normale erneute MK-Buchung.
- Bei Status 9 muss die normale Schaltflaeche gesperrt bleiben.
- Der bereits gebuchte Wert `AMMATV` bleibt sichtbar, damit eine versehentliche Doppelbuchung erkannt wird.

## Nachtraeglicher zusaetzlicher Verbrauch mit `MU`

Der neue Mitschnitt zeigt den begonnenen Oxaion-Weg fuer:

```text
ARAKKZ = MU
TX_AKKZ = Material ungeplant
```

Dabei wird fuer den Fertigungsauftrag eine ungeplante Materialrueckmeldung vorbereitet; im Referenzfluss wird nach der Auswahl von `MU` eine neue Position vorbereitet und der Artikel `RP.00010` gesetzt.

Der Mitschnitt wurde bewusst **vor Abschluss der MU-Buchung beendet**. Deshalb sind derzeit nicht als produktiv bestaetigt:

- finaler `PW22031J *PUTNEW` fuer MU;
- konkrete vollstaendige Pflichtfeldkombination fuer eine erfolgreiche MU-Buchung;
- Ergebnis-/Verifikationsfolge nach erfolgreichem MU;
- belastbare Recovery-Logik fuer einen unklaren MU-Ausgang.

Folge: MU darf noch nicht automatisch von der WebApp gebucht werden. Bei einer bereits abgeschlossenen Materialposition kann die App aber darauf hinweisen, dass eine nachtraegliche Korrektur fachlich ueber `Material ungeplant (MU)` moeglich ist und dafuer noch ein vollstaendig erfolgreicher STAGING-Mitschnitt benoetigt wird.

## Verbrauchseingabe

Der zusaetzliche Verbrauch ist eine bewusste Bedienereingabe in kg.

Verbindlich:

- keine Vorbelegung;
- Wert muss groesser als `0` sein;
- Wert darf den aktuell gelesenen Tankbestand nicht ueberschreiten;
- bereits gebuchter Verbrauch `AMMATV` wird separat angezeigt und niemals als Eingabewert missverstanden;
- rechnerischer Restbestand des Tanks ist nur Bedienhilfe und wird vor der Buchung erneut gegen Oxaion validiert.

## Aktueller Implementierungs-/Teststatus

Technisch nachgewiesen sind jetzt:

- Tankartikel und Mix-Charge aus dem gescannten Maschinentank lesen;
- FA-QR und Artikelabgleich;
- Materialposition mit Artikelbezug aus Oxaion lesen;
- Sollbedarf `AMMATB` lesen;
- tatsaechlich gebuchten Materialverbrauch `AMMATV` lesen;
- Materialstatus `AMMPST` und Statustext `TX_MPST` lesen;
- normale Rueckmeldetransaktion `MK` aus einem erfolgreichen Mitschnitt;
- eindeutige Oxaion-Ablehnung `AKK2638` fuer `MK` bei Materialstatus `9`;
- Beginn des alternativen ungeplanten Materialwegs `MU`.

Noch offen fuer eine vollstaendig produktionsreife FA-Rueckmeldung sind:

- Backend-Start der Materialpositionsauskunft aus einer screenlosen HTTP-Session im STAGING-End-to-End-Test bestaetigen;
- MK-Buchungsservice mit Pre-Write-Revalidierung von `AMMATV`/`AMMPST`, Idempotenz und sicherer Ergebnispruefung in der WebApp implementieren und live testen;
- vollstaendig erfolgreichen MU-Mitschnitt aufnehmen, bevor MU schreibend implementiert wird;
- Recovery-/Statuspruefung der FA-Rueckmeldung bei Verbindungsabbruch so absichern, dass kein blinder Retry moeglich ist.
