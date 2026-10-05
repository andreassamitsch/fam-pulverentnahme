# Manueller Uebergangsprozess bis zur produktiven Pulver-App

## Zweck

Bis die produktive App alle vorgesehenen Pulvervorgaenge direkt in Oxaion abbildet, werden die tatsaechlichen Bewegungen in der Produktion schriftlich erfasst und anschliessend von der Produktionsleitung manuell in Oxaion gebucht.

Grundsatz:

```text
physische Bewegung
-> sofort auf Zettel dokumentieren
-> Zettel an Produktionsleitung
-> in Oxaion in richtiger Reihenfolge buchen
-> Oxaion-Belegnummer und Buchungsstatus am Zettel vermerken
```

Die Produktion dokumentiert nur Informationen, die sie beim realen Vorgang kennt beziehungsweise physisch erfasst. Systemdaten werden nicht zusaetzlich von der Produktion verlangt.

## Rollen

### Produktion

Die Produktion dokumentiert die tatsaechliche Bewegung unmittelbar beim Vorgang.

Sie muss insbesondere keine Oxaion-Systemdaten nachschlagen und keine Mix-Chargen rekonstruieren.

### Produktionsleitung

Die Produktionsleitung:

1. prueft den Zettel auf Vollstaendigkeit;
2. ermittelt die fuer die Buchung benoetigten Oxaion-Systemdaten;
3. bucht die Vorgaenge pro Maschine in der tatsaechlichen zeitlichen Reihenfolge;
4. kontrolliert den Oxaion-Bestand beziehungsweise Beleg;
5. vermerkt Belegnummer, Buchungsdatum und Bearbeiter am Zettel;
6. kennzeichnet den Zettel eindeutig als gebucht.

Ein bereits als gebucht gekennzeichneter Vorgang darf nicht nochmals gebucht werden.

## Wichtige Reihenfolgeregel

Die alte Mix-Charge und der Maschinenbestand werden von der Produktion nicht aufgeschrieben, sondern von der Produktionsleitung aus Oxaion ermittelt.

Deshalb gilt bei mehreren noch offenen Vorgaengen auf derselben Maschine zwingend:

- immer den aeltesten Vorgang zuerst buchen;
- erst danach den naechsten Vorgang derselben Maschine buchen;
- nie einen spaeteren Nachfuellvorgang vor einem aelteren buchen.

Die Oxaion-Buchung des ersten Zettels erzeugt den Systemzustand, der fuer den naechsten Zettel die neue Ausgangsbasis bildet.

Wenn mehrere Vorgaenge derselben Maschine am selben Tag stattfinden, muss ihre Reihenfolge eindeutig nachvollziehbar sein. Dafuer kann auf dem Zettel zusaetzlich die Uhrzeit oder eine laufende Reihenfolge vermerkt werden.

# 1. Pulver nachfuellen

## Physischer Ablauf in der Produktion

1. Fertigungsauftrag bestimmen.
2. Tatsaechlich verwendete Maschine festlegen.
3. Pulver-/Rohmaterialcharge bereitstellen.
4. Pulver in die Maschine einfuellen.
5. Tatsaechlich eingefuellte Menge feststellen.
6. Einfuelldatum dokumentieren.
7. Bei mehreren Chargen jede Charge mit eigener Menge dokumentieren.
8. Zettel unmittelbar an die Produktionsleitung weitergeben beziehungsweise in die definierte Ablage fuer offene Buchungen legen.

## Was die Produktion aufschreibt

Pflichtangaben:

- Personalnummer beziehungsweise Mitarbeiter
- Fertigungsauftrag
- tatsaechlich verwendete Maschine
- tatsaechlich neu eingefuellte Rohmaterial-/Herstellercharge
- eingefuellte Menge in kg
- tatsaechliches Datum des Einfuellens

Bei mehreren eingefuellten Chargen wird jede Charge mit ihrer eigenen Menge erfasst.

Falls die Entnahmestelle beziehungsweise eine Behälter-/Lagerkennzeichnung in der Produktion eindeutig bekannt und fuer die Zuordnung hilfreich ist, kann sie mit angegeben werden. Ein interner Oxaion-Lagerplatzschluessel muss von der Produktion nicht nachgebildet werden.

## Was die Produktion nicht aufschreiben muss

- alte Mix-Charge auf der Maschine
- bisheriger Oxaion-Tankbestand
- Artikelbezeichnung
- neu zu erzeugende Mix-Charge
- Oxaion-Buchungstext
- Oxaion-Belegnummer

Diese Angaben ermittelt beziehungsweise ergaenzt die Produktionsleitung beim Buchen.

## Manuelle Oxaion-Buchung durch die Produktionsleitung

Der bestaetigte Nachfuell-/Mix-Vorgang besteht fachlich aus:

```text
Position 1
vorhandene Mix-Charge / kompletter Maschinenbestand
-> neue Mix-Charge auf derselben Maschine

Position 2
neu eingefuellte Rohmaterialcharge
-> dieselbe neue Mix-Charge auf der Maschine

Position 3 und weitere
weitere Rohmaterialchargen
-> dieselbe neue Mix-Charge auf der Maschine
```

Vorgehen:

1. Maschine aus dem Zettel bestimmen.
2. Aktuellen, fuer diesen Zettel massgeblichen Oxaion-Maschinenbestand pruefen.
3. Daraus Artikel, alte Mix-Charge und kompletten Maschinenbestand ermitteln.
4. Die auf dem Zettel dokumentierte Rohmaterialcharge in Oxaion eindeutig als Quellbestand bestimmen.
5. Bei Lagerplatzorganisation den echten internen Oxaion-Lagerplatzschluessel verwenden; keine optisch formatierte Lagerplatzdarstellung nachbauen.
6. Neue Mix-Charge nach dem verbindlichen Schema erzeugen:

```text
<Artikel ohne Punkt>MIX_<yyyyMMdd>_<HHmmss>
```

7. Zuerst den kompletten alten Maschinenbestand auf die neue Mix-Charge umbuchen.
8. Danach jede auf dem Zettel angegebene neue Rohmaterialcharge mit der dokumentierten Menge auf dieselbe neue Mix-Charge buchen.
9. Oxaion-Beleg beziehungsweise resultierenden Maschinenbestand kontrollieren.
10. Oxaion-Belegnummer und Buchungsstatus auf dem Zettel vermerken.

Der technisch bestaetigte Mix-Ablauf verwendet einen Lagerbeleg mit Buchungskennzeichen `MB`; die Positionen erzeugen die bestaetigten `LM`-/`LN`-Bewegungen. Die genaue technische HTTP-Sequenz steht in `docs/STAGING_REAL_MIX_PROTOTYPE.md`.

## Papier-Vorlage: Pulver nachfuellen

```text
FAM - PULVER NACHFUELLEN

Produktion

Personalnummer / Mitarbeiter: ______________________________

Fertigungsauftrag: _________________________________________

Tatsaechliche Maschine: ____________________________________

Datum des Einfuellens: _____________________________________

Bei mehreren Nachfuellungen am selben Tag ggf. Uhrzeit/Reihenfolge:
____________________________________________________________

Eingefuellte Pulverchargen:

1. Rohmaterial-/Herstellercharge: __________________________
   Menge: __________________ kg
   Entnahmeort/Behaelter, falls bekannt: ____________________

2. Rohmaterial-/Herstellercharge: __________________________
   Menge: __________________ kg
   Entnahmeort/Behaelter, falls bekannt: ____________________

3. Rohmaterial-/Herstellercharge: __________________________
   Menge: __________________ kg
   Entnahmeort/Behaelter, falls bekannt: ____________________

Unterschrift/Kuerzel Produktion: ____________________________

------------------------------------------------------------
NUR PRODUKTIONSLEITUNG / OXAION

Artikel: ___________________________________________________

Alte Mix-Charge: ___________________________________________

Alter Maschinenbestand: ________________________________ kg

Neue Mix-Charge: ___________________________________________

Oxaion-Belegnummer: ________________________________________

Gebucht am: __________________  von: ________________________

[ ] Oxaion-Bestand kontrolliert
[ ] Vollstaendig gebucht
```

# 2. Pulververbrauch zum Fertigungsauftrag

## Physischer Ablauf in der Produktion

Nach dem Druck wird der tatsaechliche Pulververbrauch nach dem vorgesehenen Wiege-/Erfassungsverfahren festgestellt.

Die Produktion dokumentiert:

- Personalnummer beziehungsweise Mitarbeiter
- Fertigungsauftrag
- tatsaechlich verwendete Maschine
- Verbrauchsmenge in kg
- Datum der Ermittlung beziehungsweise des Verbrauchsvorgangs

Die aktuelle Mix-Charge muss von der Produktion nicht aufgeschrieben werden, sofern sie aus dem Maschinenzustand eindeutig durch die Produktionsleitung bestimmt werden kann.

## Manuelle Oxaion-Buchung durch die Produktionsleitung

Der konkrete Oxaion-Programmablauf beziehungsweise Buchungsschluessel fuer die FA-Materialrueckmeldung ist im Projekt noch nicht technisch bestaetigt.

Bis zur Bestaetigung verwendet die Produktionsleitung den bereits betrieblich freigegebenen manuellen Oxaion-Prozess fuer den Materialverbrauch am Fertigungsauftrag. Es werden keine neuen Oxaion-Programme oder Buchungsschluessel aus dieser Dokumentation abgeleitet.

Vor der Buchung prueft die Produktionsleitung:

- richtigen Fertigungsauftrag;
- richtige Ist-Maschine;
- Pulverartikel und fuer den Zeitpunkt relevante Mix-Charge;
- dokumentierte Verbrauchsmenge.

## Papier-Vorlage: Pulververbrauch zum FA

```text
FAM - PULVERVERBRAUCH FERTIGUNGSAUFTRAG

Produktion

Personalnummer / Mitarbeiter: ______________________________

Fertigungsauftrag: _________________________________________

Tatsaechliche Maschine: ____________________________________

Verbrauchte Menge: _____________________________________ kg

Datum: _____________________________________________________

Unterschrift/Kuerzel Produktion: ____________________________

------------------------------------------------------------
NUR PRODUKTIONSLEITUNG / OXAION

Artikel: ___________________________________________________

Massgebliche Mix-Charge: ___________________________________

Oxaion-Buchung / Belegreferenz: _____________________________

Gebucht am: __________________  von: ________________________

[ ] Oxaion-Bestand / FA-Buchung kontrolliert
[ ] Vollstaendig gebucht
```

# 3. Pulverwechsel / vollstaendige Entleerung

## Physischer Ablauf in der Produktion

Beim Pulverwechsel wird die Maschine vollstaendig entleert.

Ablauf:

1. Maschine eindeutig bestimmen.
2. Pulver vollstaendig aus der Maschine entnehmen.
3. Entnommenes Pulver gemaess Produktionsprozess sieben.
4. Pulver eindeutig fuer die Rueckstellung kennzeichnen beziehungsweise im vorgesehenen Rueckstellprozess ablegen.
5. Datum und ausfuehrenden Mitarbeiter dokumentieren.
6. Zettel an die Produktionsleitung uebergeben.

Die Produktion muss die alte Mix-Charge und den Oxaion-Systembestand nicht kennen. Die vollstaendige Entnahmemenge soll im spaeteren App-Prozess aus dem Oxaion-Systembestand uebernommen werden.

## Manuelle Oxaion-Buchung durch die Produktionsleitung

Die Produktionsleitung ermittelt vor der Buchung in Oxaion:

- Pulverartikel auf der Maschine;
- aktuelle Mix-Charge;
- kompletten Systembestand der Maschine.

Der konkrete Oxaion-Buchungsschluessel fuer `Maschinenlager -> Pulverlager` ist im Projekt noch offen. Deshalb darf diese Anleitung dafuer keinen neuen Buchungsschluessel vorgeben.

Nach der Ruecklagerung und vor einer Neubefuellung muss der Maschinenbestand in Oxaion mit dem tatsaechlichen Zustand uebereinstimmen.

Eine anschliessende Neubefuellung mit neuem Pulver wird als eigener Nachfuell-/Befuellvorgang dokumentiert. Dafuer wird der Zettel `Pulver nachfuellen` verwendet.

## Papier-Vorlage: Pulverwechsel / Entleerung

```text
FAM - PULVERWECHSEL / MASCHINE ENTLEERT

Produktion

Personalnummer / Mitarbeiter: ______________________________

Maschine: __________________________________________________

Datum der Entleerung: ______________________________________

[ ] Maschine vollstaendig entleert
[ ] Entnommenes Pulver gesiebt

Rueckstellort / Behaelterkennzeichnung, falls bekannt:
____________________________________________________________

Bemerkung, nur falls erforderlich:
____________________________________________________________
____________________________________________________________

Unterschrift/Kuerzel Produktion: ____________________________

------------------------------------------------------------
NUR PRODUKTIONSLEITUNG / OXAION

Artikel: ___________________________________________________

Alte Mix-Charge: ___________________________________________

Systembestand / Ruecklagerungsmenge: ___________________ kg

Ziellager / Lagerplatz: ____________________________________

Oxaion-Belegnummer: ________________________________________

Gebucht am: __________________  von: ________________________

[ ] Maschinenbestand nach Buchung kontrolliert
[ ] Vollstaendig gebucht
```

# 4. Fehler- und Doppelbuchungsregel fuer Papierzettel

- Ein Zettel wird nach erfolgreicher Oxaion-Buchung eindeutig als `GEBUCHT` gekennzeichnet.
- Oxaion-Belegnummer und Bearbeiter werden dokumentiert.
- Ein als gebucht gekennzeichneter Zettel darf nicht nochmals gebucht werden.
- Ist nach einer Buchung unklar, ob sie erfolgreich war, wird nicht vorsorglich ein zweites Mal gebucht.
- Zuerst werden Oxaion-Beleg und Bestand geprueft.
- Bei widerspruechlichen oder fehlenden Angaben wird nicht geraten; der Vorgang wird vor der Buchung geklaert.

# 5. Abgrenzung zum spaeteren App-Prozess

Die Papierzettel sind nur der manuelle Uebergangsprozess.

Sie sollen keine zusaetzlichen dauerhaften Produktionsangaben einfuehren, die spaeter in der App nicht benoetigt werden.

Insbesondere werden alte Mix-Charge und Tankbestand spaeter weiterhin automatisch aus Oxaion gelesen. Die neue Mix-Charge wird automatisch erzeugt. Die Produktion liefert beziehungsweise bestaetigt nur die tatsaechlichen physischen Vorgangsdaten.
