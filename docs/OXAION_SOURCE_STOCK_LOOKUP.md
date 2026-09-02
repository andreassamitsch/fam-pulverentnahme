# Oxaion-Nachfüllquelle: Lagerort, Lagerplatz, Charge und Bestand

## Zweck

Für eine Nachfüllcharge dürfen Lagerort, Lagerplatz und Charge nicht frei als Text eingegeben werden. Die gültige Buchungsquelle wird aus dem aktuellen positiven Oxaion-Bestand gewählt und unmittelbar vor der Materialbuchung erneut validiert.

Grundlage sind die am 02.09.2026 aufgezeichneten JET-Datenströme:

- angeforderte Buchungs-/F4-Folge mit erfolgreicher Quelle `H04HRL / RE1F3 / 84671`
- Programm `Chargen je Firma`
- Auskunft `Chargen und Lagerorte pro Artikel` sowie `Lagerplätze pro Artikel und -ort`

## Nachgewiesene Ursache des Fehlers LAP1258

Beim abgelehnten Vorgang wurde der Lagerplatz visuell als `RE1  F 3` wahrgenommen beziehungsweise entsprechend frei eingegeben. Oxaion erwartet für `PSLAPL` jedoch den internen Lagerplatzschlüssel.

Im erfolgreichen Datenstrom ist eindeutig belegt:

```text
PSLAGO = H04HRL
PSLAPL = RE1F3
PSPONR = 84671
```

Die anschließende Position wurde von `LB20110R *UPD` erfolgreich als LM/LN verarbeitet. Damit ist `RE1F3` der für die Buchung relevante interne Schlüssel. Eine vom Benutzer nachgebildete Anzeigeformatierung darf nicht mehr als Buchungsschlüssel verwendet werden.

## 1. Lagerorte mit positivem Bestand zum Artikel

Bestätigter Aufruf:

```text
US30600J
  FFMT = CL
  FFMS = CL
  PGMN = LB30340R
  I_TIDF = <Artikel>
  NEXTPGM = LB30340R
```

Danach:

```text
LB30340R *GETHDR
LB30340R *FIRSTLIST
LB30340R *NEXTLIST ... bis <STOP/>
```

Bezeichnung des Dialogs:

```text
Chargen und Lagerorte pro Artikel
```

Relevante Rückgabefelder:

```text
KEY/LALAGO                 Lagerort
KEY/LAIDNR                 Artikel
KEY/LAPONR                 Charge
LLAWEL01PONR.LALABE        Lagerbestand
```

Für `RP.00010` waren im Referenzdatenstrom unter anderem folgende positive Bestände vorhanden:

```text
EOS1    / RP10WEB_20260901_163210 / 164,344
FAMLAB  / 87911                    / 246,972
H04HRL  / 84671                    / 1024,713
H04HRL  / 88673                    / 600,000
```

Die WebApp liest die vollständige Liste und wertet `Bestand > 0` selbst aus. Es wird kein gespeicherter Oxaion-Filter benötigt.

## 2. Lagerort-Bezeichnung

Bestätigter rein lesender Aufruf:

```text
US00006J *GETPLAIN
MFLD   = LAGO
PGMN   = US30600J
LAGO   = <Lagerort>
PFIELD = TX_LAGO
FIELD  = LAGO
```

Beispiel:

```text
H04HRL -> Halle 04 Hochregallager
```

Die Bezeichnung ist Anzeigeinformation. Für die Buchung bleibt der Oxaion-Schlüssel maßgeblich.

## 3. Exakter Lagerplatz + Charge + Bestand

Für einen gewählten Lagerort mit Lagerplatzorganisation:

```text
US30600J
  TIDF = <Artikel>
  STARTUP = <DUFIRM>...</DUFIRM><DUIDNV>...</DUIDNV><DULAGV>...</DULAGV>
  TX_FFMT = Lagerplätze pro Artikel und -ort
  FFMT = PT
  FFMS = PT
  PGMN = LB30430R
  LAGO = <Lagerort>
  I_TIDF = <Artikel>
  NEXTPGM = LB30430R
```

Danach:

```text
LB30430R *GETHDR
LB30430R *FIRSTLIST
LB30430R *NEXTLIST ... bis <STOP/>
```

Relevante Schlüssel/Felder:

```text
KEY/LPLAGO          Lagerort
KEY/LPIDNR          Artikel
KEY/LPLAPL          interner Lagerplatzschlüssel
KEY/LPPONR          Charge
LLPWEP.LPLAPL       Lagerplatz
LLPWEP.LPPONR       Charge
LLPWEP.LPLABE       Lagerplatzbestand
```

Referenz für `H04HRL / RP.00010`:

```text
RE1F2 / 87911 /    0,000 kg
RE1F2 / 88673 /  600,000 kg
RE1F3 / 84671 / 1024,713 kg
```

Für die Auswahl werden nur Bestände `> 0` angeboten.

## 4. Lagerort ohne Lagerplatzorganisation

Im Datenstrom ist der Fall ebenfalls bestätigt:

```text
FCOD = LAG1626
Lagerort EOS1 hat keine Lagerplatzorganisation
```

Das ist kein Grund, einen Lagerplatz zu erfinden. In genau diesem bestätigten Fall wird auf den bereits implementierten lesenden Ablauf `Chargen pro Lagerort` (`LB30230R`) zurückgegriffen. Dieser liefert Charge, Lagerortbestand und Mengeneinheit. Der Lagerplatz bleibt für die Quelle leer.

Andere Fehlercodes werden nicht als `kein Lagerplatz` interpretiert.

## 5. `Chargen je Firma` als Kontrollnachweis

Der Mitschnitt `Chargen je Firma` verwendet:

```text
MN10209J *CHKCMD
CHKCMD = CF
-> LB30210R
```

Für `RP.00010` bestätigte die Ansicht die gleichen positiven Chargen und die Mengeneinheit KGM, unter anderem:

```text
84671                    1024,713 KGM   H04HRL
87911                     246,972 KGM   FAMLAB
88673                     600,000 KGM   H04HRL
RP10WEB_20260901_163210    164,344 KGM   EOS1
```

Diese Ansicht bestätigt die fachlichen Daten, enthält aber keinen exakten Lagerplatz. Sie ist deshalb nicht die alleinige Quelle für die Buchungsauswahl.

## Verbindliche WebApp-Logik

Für jede Nachfüllcharge gilt:

1. Artikel ist bekannt.
2. Backend liest über `LB30340R` die Lagerorte mit positivem Artikelbestand.
3. Bediener wählt einen dieser Lagerorte.
4. Backend liest über `LB30430R` die positiven Lagerplatz-/Chargenpositionen.
5. Bei `LAG1626` wird `LB30230R` verwendet und der Lagerplatz bleibt leer.
6. Bediener wählt eine konkrete Oxaion-Bestandsposition; Lagerort, interner Lagerplatzschlüssel und Charge sind danach nicht frei editierbar.
7. Bediener gibt nur noch die gewünschte Einfüllmenge ein; sie darf den angezeigten Bestand nicht überschreiten.
8. Direkt vor der ersten schreibenden Materialbuchung liest das Backend alle ausgewählten Quellen erneut und verlangt exakte Übereinstimmung von Artikel, Lagerort, Lagerplatz und Charge sowie ausreichenden Bestand.
9. Bei Abweichung oder nicht lesbarem Bestand wird keine Materialbuchung gestartet.

Mehrfachauswahl derselben exakten Bestandsposition innerhalb eines Vorgangs ist nicht zulässig; die Menge ist in einer Quelle zusammenzufassen.
