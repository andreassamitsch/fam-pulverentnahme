# Lagerbestandsansicht fuer RP.* Pulverartikel

Stand: 08.09.2026

## Ziel

Die PWA besitzt eine rein lesende Funktion `Lagerbestand`. Sie zeigt aktuelle Oxaion-Bestaende fuer Pulverartikel `RP.*` hierarchisch nach Lagerort und - soweit der Lagerort lagerplatzgefuehrt ist - nach internem Lagerplatz.

Die Funktion ist reine Auskunft. Sie erzeugt keine Lager- oder Materialbuchung.

## Verbindliche Korrektur vom 07.09.2026

Der Versuch, den Artikelbereich `RP.*` direkt als `TIDF/I_TIDF` an die Unterprogramme `LB30340R` beziehungsweise `LB30430R` zu uebergeben, ist im STAGING-Livetest fehlgeschlagen.

Oxaion meldete:

```text
IDN1823 Artikel mit Identnummer "RP.*" nicht gefunden.
Field: TIDF
```

Damit ist technisch nachgewiesen:

- `RP.*` ist fuer diesen Ablauf ein **Listenfilter**, keine gueltige Oxaion-Identnummer;
- `TIDF/I_TIDF` der Unterprogramme erhalten nur konkrete Artikelnummern wie `RP.00010`;
- der direkte Wildcard-Abstieg wird nicht weiter verwendet.

## Bestaetigter Filterablauf vom 08.09.2026

Der Mitschnitt `chargen pro firma RP und ungleich null filter.7z` bestaetigt jetzt auch den zuvor offenen Filterweg fuer `Chargen je Firma`.

### Listenstart

```text
MN10209J *CHKCMD
CHKCMD = CF
-> LB30210R
```

Danach:

```text
LB30210R *GETHDR
SSID      = <von CHKCMD>
NOHWPgm   = LB30210
```

Im Referenzmitschnitt liefert der Header:

```text
_FILTERTITLE_ = mit Bestand
```

Die anschliessende ungefilterte beziehungsweise nur mit dem aktiven Bestandsfilter versehene

```text
LB30210R *FIRSTLIST
mode = reset
```

lieferte 50 Zeilen verschiedener Artikel und noch kein `<STOP/>`. Diese erste Liste wird von der WebApp **nicht** als vollstaendiger RP-Bestand verwendet; sie dient nur zum Aufbau des bestaetigten Listenkontexts.

### Artikelbereich `RP.*` setzen

Der entscheidende, im Mitschnitt bestaetigte Aufruf ist:

```text
LB30210 *SAVALLSLT

SSID       = <Listen-SSID>
NAME       = IDNR.TLIDNR
V_TLIDNR   = RP.*
B_TLIDNR   = <leer>
```

Damit wird `RP.*` als Listen-Selektion auf die Artikelnummer gesetzt. Es wird **nicht** als `TIDF/I_TIDF` an ein Artikelprogramm uebergeben.

Der JET-Dialog ruft beim manuellen Oeffnen der Selektionsmaske zusaetzlich `*CRTSLTUID` und `*GETSLT` auf. Diese Aufrufe dienen der UI-/Selektionsdialogdarstellung. Fuer die Backend-Logik ist der fachlich relevante, vollstaendig parametrisierte Speicherschritt `*SAVALLSLT`; dessen Wirkung wird anschliessend durch die neu geladene Liste streng verifiziert.

Besonderheit des Referenzmitschnitts:

- `LB30210 *SAVALLSLT` wird HTTP-/JET-seitig erfolgreich ausgefuehrt,
- die Antwort enthaelt jedoch nur die XML-Deklaration und kein parsebares Dokumentelement.

Analog zum bereits bestaetigten Personal-Sonderfall wird deshalb **nur** der spezifische XML-Parsefehler dieses einen Schritts toleriert. Transport-/HTTP-Fehler werden nicht toleriert. Danach muessen `GETU01`, die neue `FIRSTLIST` und die Ergebnispruefung erfolgreich sein.

Anschliessend:

```text
LB30210R *GETU01
SSID = <Listen-SSID>

LB30210R *FIRSTLIST
SSID = <Listen-SSID>
mode = replace
```

Der Mitschnitt vom 08.09.2026 liefert danach:

- 21 Zeilen,
- ausschliesslich konkrete Artikel `RP.xxxxx`,
- ausschliesslich Lagerbestand `<> 0`,
- abschliessendes `<STOP/>`.

Damit ist der zuvor offene alphanumerische `RP.*`-Filterweg fuer diesen Index technisch bestaetigt.

### Bestand `<> 0`

Der Mitschnitt bestaetigt zusaetzlich die interne Oxaion-Listenfilterdarstellung fuer `UPOWEP.POLABE`:

```text
LB30210 *SAVLSTA
COLUMN = UPOWEP.POLABE
LFNU   = 0
OPER   = =
V_     = ,000
```

Die `A`-Selektion schliesst damit Bestand `= 0` aus und bildet fachlich `<> 0` ab.

Der aktuelle generische WebApp-HTTP-Client uebertraegt einfache DTA-Felder, aber keine verschachtelten JET-`TABLE`-Strukturen. Fuer die Lagerbestandsansicht ist kein erfundener TABLE-Transport erforderlich: Im bestaetigten Listenkontext ist bereits der Filter `mit Bestand` aktiv, und direkt nach `SAVALLSLT RP.*` liefert Oxaion die kleine vollstaendige RP-Liste mit Bestand `<> 0` und `<STOP/>`.

Das Backend prueft die Antwort trotzdem defensiv und bricht ab, wenn:

- `<STOP/>` fehlt,
- ein Artikel ausserhalb `RP.*` geliefert wird,
- ein Nullbestand geliefert wird.

Negative Bestaende sind `<> 0`, bleiben deshalb bewusst enthalten und werden spaeter als Klaerungsfall angezeigt.

## Verbindlicher Ablauf: `Chargen je Firma` als gefilterter Index

### 1. Konkrete RP-Artikel aus `Chargen je Firma`

Aus der bestaetigten gefilterten `LB30210R *FIRSTLIST` werden fuer die weitere Verarbeitung ausschliesslich verwendet:

```text
KEY/POIDNR bzw. IDNR.TLIDNR   konkrete Artikelnummer
IDNR.TLBEZG                   Artikelbezeichnung
UPOWEP.POLABE                 Lagerbestand
```

Mehrere Chargenzeilen desselben Artikels werden zu genau einer konkreten Artikelnummer zusammengefasst.

Das Backend prueft zusaetzlich defensiv:

- Artikel beginnt mit `RP.`;
- kein Wildcardwert wird als Artikel weitergegeben;
- Bestand ist `<> 0`;
- negative Bestaende bleiben enthalten.

### 2. `_CALC.W_LAGO` wird nicht verwendet

Die sichtbare Lagerortspalte `_CALC.W_LAGO` in `Chargen je Firma` ist eine kundenspezifisch kalkulierte/aggregierte Anzeige und kann mehrere Lagerorte in einem Wert zusammenfassen.

Der Mitschnitt vom 07.09.2026 belegt das konkret fuer:

```text
Artikel: RP.00002
Charge:  72911
_CALC.W_LAGO = FAMLAB, H04KDX
```

Dieser Wert ist kein eindeutiger Lagerortschluessel. Er wird weder fuer die hierarchische Bestandsanzeige noch fuer Buchungen ausgewertet.

`Chargen je Firma` dient in der Lagerbestandsansicht damit **nur zur Ermittlung der konkreten RP-Artikelnummern**.

### 3. Mit konkretem Artikel in `Chargen und Lagerorte pro Artikel`

Fuer jede aus `LB30210R` gelesene konkrete Artikelnummer, z. B.

```text
RP.00010
```

wird die bestaetigte Auskunft aufgerufen:

```text
LB30340R - Chargen und Lagerorte pro Artikel
```

Relevante Felder:

```text
KEY/LALAGO                 Lagerort
KEY/LAIDNR                 Artikel
KEY/LAPONR                 Charge
LLAWEL01PONR.LALABE        Lagerbestand
```

Nur Bestaende `<> 0` werden fuer die Gesamtansicht weiterverarbeitet.

### 4. Pro Lagerort exakte Lagerplatz-/Chargenpositionen lesen

Fuer jeden so ermittelten Lagerort wird mit derselben **konkreten** Artikelnummer aufgerufen:

```text
LB30430R - Lagerplaetze pro Artikel und -ort
```

Relevante Schluessel/Felder:

```text
KEY/LPLAGO     Lagerort
KEY/LPIDNR     Artikel
KEY/LPLAPL     interner Lagerplatzschluessel
KEY/LPPONR     Charge
LLPWEP.LPLABE  Bestand
```

Der interne Lagerplatzschluessel wird ausschliesslich aus Oxaion uebernommen und niemals aus einer optisch formatierten Anzeige nachgebildet.

### 5. Lagerort ohne Lagerplatzorganisation

Meldet Oxaion bei `LB30430R` eindeutig

```text
LAG1626 - Lagerort hat keine Lagerplatzorganisation
```

wird fuer genau diesen Lagerort auf den bestaetigten Ablauf

```text
LB30230R - Chargen pro Lagerort
```

zurueckgegriffen, ebenfalls mit der **konkreten** Artikelnummer.

Der Lagerplatz bleibt leer. Andere Fehlercodes werden nicht als `kein Lagerplatz` interpretiert.

## Ergebnisstruktur

Die PWA zeigt mindestens:

```text
Artikel RP.xxxxx - Bezeichnung
  Lagerort ABC - Lagerortbezeichnung
    Lagerplatz XYZ
      Charge 12345      42,500 kg
    Lagerplatz ZZZ
      Charge MIX...     10,000 kg
  Lagerort DEF
    Charge 67890        -0,250 kg   [Klaerung erforderlich]
```

Sichtbare Felder:

- Artikelnummer
- Artikelbezeichnung
- Lagerortschluessel
- Lagerortbezeichnung
- interner Lagerplatzschluessel, falls vorhanden
- Charge
- Bestand
- Mengeneinheit
- Kennzeichnung negativer Bestaende

## Filter und Vollstaendigkeit

Verbindlich:

- `RP.*` wird ausschliesslich als Filter der `Chargen je Firma`-Liste verwendet, niemals als konkrete Identnummer in `TIDF/I_TIDF` der Unterprogramme;
- der bestaetigte Filteraufruf ist `LB30210 *SAVALLSLT` mit `NAME=IDNR.TLIDNR`, `V_TLIDNR=RP.*`, leerem `B_TLIDNR` und derselben Listen-SSID;
- `Chargen je Firma` dient nur als Artikelindex, nicht als Lagerortquelle;
- `_CALC.W_LAGO` wird ignoriert;
- alle Unterprogramme werden je konkreter `RP.xxxxx`-Artikelnummer aufgerufen;
- Nullbestaende werden nicht angezeigt;
- negative Bestaende werden nicht herausgefiltert, sondern auffaellig markiert;
- `LB30340R`, `LB30430R` und `LB30230R` werden auf den bereits bestaetigten Wegen bis `<STOP/>` gelesen;
- die nach `SAVALLSLT` neu geladene `LB30210R`-Indexliste muss selbst `<STOP/>` enthalten und darf weder Fremdartikel noch Nullbestaende enthalten; andernfalls wird keine scheinbar vollstaendige Bestandsansicht erzeugt.

## Sicherheitsregel

Die Lagerbestandsansicht ist rein lesend. Spaetere Umlagerungs- oder andere Buchungsfunktionen duerfen ihre Quell-/Ziellagerplaetze nur aus bestaetigten Oxaion-Schluesseln ableiten und muessen unmittelbar vor einer schreibenden Aktion erneut serverseitig validieren.
