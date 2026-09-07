# Lagerbestandsansicht fuer RP.* Pulverartikel

Stand: 07.09.2026

## Ziel

Die PWA besitzt eine rein lesende Funktion `Lagerbestand`. Sie zeigt aktuelle Oxaion-Bestaende fuer Pulverartikel `RP.*` hierarchisch nach Lagerort und - soweit der Lagerort lagerplatzgefuehrt ist - nach internem Lagerplatz.

Die Funktion ist reine Auskunft. Sie erzeugt keine Lager- oder Materialbuchung.

## Verbindliche Korrektur vom 07.09.2026

Fuer die Gesamtansicht wird `Chargen je Firma` (`LB30210R`) nicht als Artikelindex benoetigt und nicht mehr verwendet.

Grund: Die Bestandsansicht benoetigt weder eine vorgelagerte vollstaendige Artikelliste noch die kundenspezifische Lagerortanzeige aus `Chargen je Firma`. Der sauberere Einstieg ist direkt die Lager-/Chargenstruktur mit Artikelbereich `RP.*`.

Der Bediener hat bestaetigt, dass der Oxaion-Artikelbereich fuer diese Listen mit `RP.*` eingeschraenkt werden kann. Die Backend-Implementierung verwendet deshalb denselben bereits bestaetigten Artikelparameter `TIDF/I_TIDF`, der bei den Einzelartikel-Auskuenften verwendet wird, nun mit `RP.*` fuer die Gesamtansicht. Die Live-Bestaetigung dieses neuen Gesamtaufrufs erfolgt im STAGING-Test.

Nullbestaende werden im Backend zusaetzlich immer ausgefiltert. Negative Bestaende bleiben sichtbar.

## Warum `Chargen je Firma` nicht fuer Lagerorte verwendet wird

In der aktuell verwendeten Sicht von `Chargen je Firma` ist die sichtbare Lagerortspalte `_CALC.W_LAGO` eine kundenspezifisch kalkulierte/aggregierte Anzeige und kann mehrere Lagerorte in einem Wert zusammenfassen.

Der Mitschnitt vom 07.09.2026 belegt das konkret fuer:

```text
Artikel: RP.00002
Charge:  72911
_CALC.W_LAGO = FAMLAB, H04KDX
```

Dieser Wert ist kein eindeutiger Lagerortschluessel und darf weder fuer Buchungen noch fuer eine exakte hierarchische Bestandszuordnung verwendet werden.

## Verbindlicher Lagerort-zuerst-Ablauf

### 1. RP.* Lagerorte ermitteln

Als Einstieg wird die bereits bestaetigte Auskunft

```text
LB30340R - Chargen und Lagerorte pro Artikel
```

mit dem Artikelbereich

```text
RP.*
```

aufgerufen.

Aus den zurueckgegebenen Schluesselfeldern werden ausschliesslich echte Oxaion-Daten verwendet:

```text
KEY/LALAGO   Lagerort
KEY/LAIDNR   Artikel
KEY/LAPONR   Charge
```

Fuer die weitere Verarbeitung werden nur Zeilen mit Artikel `RP.*` und Bestand `<> 0` beruecksichtigt. Aus ihnen werden die unterschiedlichen Lagerorte bestimmt.

`LB30340R` wird dabei nicht als finale Lagerplatzansicht verwendet, sondern als sauberer Einstieg zu den Lagerorten, auf denen aktuell RP-Pulver vorhanden ist.

### 2. Pro Lagerort die exakten Lagerplatz-/Chargenpositionen lesen

Fuer jeden so ermittelten Lagerort wird

```text
LB30430R - Lagerplaetze pro Artikel und -ort
```

mit

```text
Lagerort = <ermittelter Lagerort>
Artikel  = RP.*
```

aufgerufen.

Damit werden fuer lagerplatzgefuehrte Lagerorte in einem Durchlauf alle RP-Pulverpositionen dieses Lagerorts gelesen. Verwendet werden insbesondere:

```text
KEY/LPLAGO   Lagerort
KEY/LPIDNR   Artikel
KEY/LPLAPL   interner Lagerplatzschluessel
KEY/LPPONR   Charge
LLPWEP.LPLABE Bestand
```

Der interne Lagerplatzschluessel wird ausschliesslich aus Oxaion uebernommen und niemals aus einer optisch formatierten Anzeige nachgebildet.

### 3. Lagerort ohne Lagerplatzorganisation

Meldet Oxaion bei `LB30430R` eindeutig

```text
LAG1626 - Lagerort hat keine Lagerplatzorganisation
```

wird fuer genau diesen Lagerort auf den bestaetigten Ablauf

```text
LB30230R - Chargen pro Lagerort
```

zurueckgegriffen, ebenfalls mit Artikelbereich `RP.*`.

Der Lagerplatz bleibt dann leer. Die einzelnen RP-Artikel, Chargen und Bestaende stammen aus der Oxaion-Lagerortliste.

Andere Fehlercodes werden nicht als `kein Lagerplatz` interpretiert.

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
- Artikelbezeichnung, soweit die verwendete Oxaion-Liste sie liefert
- Lagerortschluessel
- Lagerortbezeichnung
- interner Lagerplatzschluessel, falls vorhanden
- Charge
- Bestand
- Mengeneinheit
- Kennzeichnung negativer Bestaende

## Filter und Vollstaendigkeit

Verbindlich:

- nur Artikel, deren Artikelnummer mit `RP.` beginnt;
- Artikelbereich des Oxaion-Aufrufs fuer die Gesamtansicht: `RP.*`;
- nur Bestandspositionen mit Bestand `<> 0`;
- Nullbestaende werden nicht angezeigt;
- negative Bestaende werden nicht herausgefiltert, sondern auffaellig markiert;
- Listen, fuer die `*NEXTLIST` bereits bestaetigt ist (`LB30340R`, `LB30430R`, `LB30230R`), werden bis zum bestaetigten `<STOP/>` gelesen;
- keine Abhaengigkeit mehr von der nicht vollstaendigen ersten `LB30210R *FIRSTLIST`.

## Sicherheitsregel

Die Lagerbestandsansicht ist rein lesend. Spaetere Umlagerungs- oder andere Buchungsfunktionen duerfen ihre Quell-/Ziellagerplaetze nur aus bestaetigten Oxaion-Schluesseln ableiten und muessen unmittelbar vor einer schreibenden Aktion erneut serverseitig validieren.
