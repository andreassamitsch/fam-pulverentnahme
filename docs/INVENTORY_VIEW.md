# Lagerbestandsansicht fuer RP.* Pulverartikel

Stand: 07.09.2026

## Ziel

Die PWA soll eine rein lesende Funktion `Lagerbestand` erhalten. Sie zeigt aktuelle Oxaion-Bestaende fuer Pulverartikel `RP.*` hierarchisch nach Lagerort und - soweit der Lagerort lagerplatzgefuehrt ist - nach internem Lagerplatz.

Die Funktion ist reine Auskunft. Sie erzeugt keine Lager- oder Materialbuchung.

## Wichtige Abgrenzung: `Chargen je Firma`

Der Oxaion-Dialog `Chargen je Firma` beziehungsweise `LB30210R` darf fuer die Lagerortdarstellung nicht als alleinige fachliche Quelle verwendet werden.

Grund: In der aktuell verwendeten Sicht kann die sichtbare Lagerortspalte eine kundenspezifisch kalkulierte/aggregierte Spalte sein und mehrere Lagerorte beispielsweise per `STRING_AGG` in einem Anzeigewert zusammenfassen. Dieser Anzeigewert ist kein eindeutiger Lagerortschluessel und darf weder fuer Buchungen noch fuer eine exakte hierarchische Bestandszuordnung verwendet werden.

`Chargen je Firma` kann spaeter hoechstens als Einstieg zum Ermitteln vorhandener RP-Artikel/Chargen dienen, sofern die dafuer verwendeten Artikel-/Chargenfelder technisch eindeutig bestaetigt sind. Seine aggregierte Lagerortanzeige wird dabei ignoriert.

## Verbindlicher Abstiegsweg fuer exakte Lagerorte

Fuer einen bekannten Pulverartikel gilt der bereits technisch bestaetigte Auskunftsweg:

1. `LB30340R` - `Chargen und Lagerorte pro Artikel`
   - liefert den echten Oxaion-Lagerortschluessel zum Artikel/Charge-Bestand;
   - fuer die Lagerbestandsansicht werden Bestaende `<> 0` beruecksichtigt, nicht nur positive Bestaende;
   - negative Bestaende werden sichtbar als Klaerungsfall dargestellt und nicht ausgeblendet.
2. Fuer jeden ermittelten Lagerort wird `LB30430R` - `Lagerplaetze pro Artikel und -ort` gelesen.
   - liefert den internen Lagerplatzschluessel, Charge und Lagerplatzbestand;
   - Lagerplatzwerte werden ausschliesslich aus Oxaion uebernommen und nicht aus einer optisch formatierten Darstellung nachgebildet.
3. Meldet Oxaion fuer einen Lagerort eindeutig `LAG1626` (`Lagerort hat keine Lagerplatzorganisation`), bleibt die Lagerplatzebene leer und der bestaetigte `LB30230R`-Lagerort-/Chargenweg wird verwendet.

Diese Hierarchie entspricht den bereits fuer Nachfuellquellen bestaetigten Lesewegen und vermeidet die Verwendung einer aggregierten Lagerort-Anzeigespalte.

## Gewuenschte Darstellung

Die Mitarbeiteransicht soll mindestens anzeigen:

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

Sinnvolle Felder:

- Artikelnummer
- Artikelbezeichnung
- Lagerortschluessel
- Lagerortbezeichnung
- interner Lagerplatzschluessel, falls vorhanden
- Charge
- Bestand
- Mengeneinheit
- Kennzeichnung negativer Bestaende

Optional koennen Summen je Artikel und Lagerort berechnet werden. Diese Summen sind Anzeigehilfen; die Einzelpositionen bleiben sichtbar.

## Filter

Fuer die geplante Gesamtansicht gilt:

- nur Artikel, deren Artikelnummer mit `RP.` beginnt;
- nur Bestandspositionen mit Bestand `<> 0`;
- Nullbestaende werden nicht angezeigt;
- negative Bestaende werden **nicht** herausgefiltert, sondern auffaellig markiert.

## Noch offener technischer Einstieg fuer `alle RP.*`

Die bestaetigten `LB30340R`-/`LB30430R`-Wege starten mit einem bekannten Artikel. Fuer die Gesamtansicht `alle RP.*` wird noch ein sauberer rein lesender Einstieg benoetigt, der die vorhandenen RP-Artikel ermittelt.

Der bereits bekannte Dialog `Chargen je Firma` ist als moeglicher Artikel-/Chargenindex zu pruefen, jedoch ohne seine kundenspezifische aggregierte Lagerortspalte zu uebernehmen. Falls ein besser geeigneter Oxaion-Standarddialog beziehungsweise ein bestaetigter HTTP-Auskunftsweg existiert, ist dieser vorzuziehen.

Bis dieser Einstieg technisch bestaetigt ist, darf die Gesamtansicht nicht so implementiert werden, als waere die aggregierte Lagerortspalte aus `Chargen je Firma` eine eindeutige Bestandsquelle.

## Sicherheitsregel

Die Lagerbestandsansicht ist rein lesend. Spaetere Umlagerungs- oder andere Buchungsfunktionen duerfen ihre Quell-/Ziellagerplaetze nur aus bestaetigten Oxaion-Schluesseln ableiten und muessen unmittelbar vor einer schreibenden Aktion erneut serverseitig validieren.
