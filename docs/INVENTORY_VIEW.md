# Lagerbestandsansicht fuer RP.* Pulverartikel

Stand: 07.09.2026

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

## Verbindlicher Ablauf: `Chargen je Firma` als gefilterter Index

### 1. Gefilterter Einstieg ueber `Chargen je Firma`

Als Einstieg dient wieder:

```text
MN10209J *CHKCMD
CHKCMD = CF
-> LB30210R - Chargen je Firma
```

Die Liste soll in Oxaion auf folgende Bedingungen eingeschraenkt werden:

```text
Artikelnummer: RP.*
Lagerbestand:  <> 0
```

Der vorhandene Referenzmitschnitt einer bereits so eingeschraenkten `LB30210R *FIRSTLIST` lieferte nur eine kleine RP-Pulverliste und endete mit `<STOP/>`.

Aus dieser Liste werden fuer die weitere Verarbeitung ausschliesslich verwendet:

```text
KEY/POIDNR bzw. IDNR.TLIDNR   konkrete Artikelnummer
IDNR.TLBEZG                   Artikelbezeichnung
UPOWEP.POLABE                 Lagerbestand
```

Mehrere Chargenzeilen desselben Artikels werden zu genau einer konkreten Artikelnummer zusammengefasst.

Das Backend prueft zusaetzlich defensiv:

- Artikel beginnt mit `RP.`;
- Bestand ist `<> 0`;
- negative Bestaende bleiben enthalten und werden spaeter als Klaerungsfall sichtbar gemacht.

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
KEY/LPLAGO   Lagerort
KEY/LPIDNR   Artikel
KEY/LPLAPL   interner Lagerplatzschluessel
KEY/LPPONR   Charge
LLPWEP.LPLABE Bestand
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
- `Chargen je Firma` dient nur als Artikelindex, nicht als Lagerortquelle;
- `_CALC.W_LAGO` wird ignoriert;
- alle Unterprogramme werden je konkreter `RP.xxxxx`-Artikelnummer aufgerufen;
- Nullbestaende werden nicht angezeigt;
- negative Bestaende werden nicht herausgefiltert, sondern auffaellig markiert;
- `LB30340R`, `LB30430R` und `LB30230R` werden auf den bereits bestaetigten Wegen bis `<STOP/>` gelesen;
- eine unvollstaendige `LB30210R`-Indexliste wird nicht als vollstaendige Lagerbestandsansicht akzeptiert.

## Noch technisch offen: Setzen des `RP.*`-Listenfilters per HTTP/JET

Der vorhandene Mitschnitt beweist die bereits gefilterte `LB30210R *FIRSTLIST`. Der exakte HTTP/JET-Datenstrom, mit dem der Benutzer in `Chargen je Firma` den alphanumerischen Artikelnummernfilter `RP.*` setzt und anwendet, ist jedoch noch nicht aufgezeichnet.

Die generische Oxaion-Listenfiltermechanik und der Bestandfilter `<>` sind aus anderen Listendialogen bekannt. Die genaue Kodierung des `RP.*`-Werts fuer `LB30210` wird trotzdem nicht geraten.

Bis dieser kurze Mitschnitt vorliegt, blockiert das Backend eine `LB30210R`-Antwort ohne `<STOP/>`, statt eine unvollstaendige RP-Liste als vollstaendig auszugeben oder eine nicht bestaetigte Pagination beziehungsweise Filterfolge zu erfinden.

## Sicherheitsregel

Die Lagerbestandsansicht ist rein lesend. Spaetere Umlagerungs- oder andere Buchungsfunktionen duerfen ihre Quell-/Ziellagerplaetze nur aus bestaetigten Oxaion-Schluesseln ableiten und muessen unmittelbar vor einer schreibenden Aktion erneut serverseitig validieren.
