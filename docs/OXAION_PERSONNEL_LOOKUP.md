# Oxaion-Personalpruefung fuer Pulverentnahme

## Status

Der lesende Ablauf wurde am 02.09.2026 aus einem realen JET-Datenstrom rekonstruiert. Die Feldzuordnung fuer die Bedieneranzeige wurde am 03.09.2026 anhand des STAGING-Tests korrigiert: `PEPENA` ist fuer diesen Ablauf das zu verwendende Feld fuer den vollstaendigen Namen. `PESAKZ` wird nicht verwendet, da es nicht fuer jeden Mitarbeiter gepflegt ist.

## Bestaetigte Aufrufreihenfolge

1. `MN10209J *CHKCMD` mit `CHKCMD=MA` und `_father_=CMDLINE`
2. `US14090J *LOAD`
3. `US14090J *GETCFTIT`
4. `US14090J *FIRSTLIST`
5. `US14090J *SEARCH` mit der vom Bediener eingegebenen Personalnummer ohne fuehrende Nullen, z. B. `SEARCH=446`
6. `US14000J *LOAD`
7. `US14000J *READ` fuer den ausgewaehlten Personalsatz

Fuer die WebApp werden aus dem gelesenen Personalsatz nur folgende Felder verwendet:

```text
PEPENU: Oxaion-Personalnummer
PEPENA: vollstaendiger Name
```

## Feldbedeutung fuer die WebApp

- `PEPENU`: Oxaion-Personalnummer; Oxaion liefert sie intern mit fuehrenden Nullen. Die WebApp zeigt und speichert die Personalnummer ohne fuehrende Nullen.
- `PEPENA`: vollstaendiger Name des Mitarbeiters; wird als kanonischer Name in die Buchungsdokumentation uebernommen.
- `PESAKZ`: wird bewusst weder fuer die Identifikation noch fuer die Anzeige benoetigt, weil das Feld nicht fuer jeden Mitarbeiter gepflegt ist.
- `PENLAE`: wird fuer den vollstaendigen Mitarbeiternamen in diesem Ablauf nicht verwendet.

## UI-Regel

Der Bediener gibt nur Ziffern ein. Die WebApp sucht nach kurzer Verzoegerung in Oxaion und zeigt Treffer ausschliesslich nach dem Muster

```text
446 - Andreas Samitsch
```

also fachlich:

```text
PEPENU - PEPENA
```

Der Treffer muss bewusst angeklickt beziehungsweise bestaetigt werden. Eine freie Namenseingabe gibt es nicht.

## Sicherheitsregel vor Buchung

Die Auswahl im Browser ist keine dauerhafte Wahrheit. Direkt vor dem ersten schreibenden Materialbuchungsaufruf liest das Backend die Personalnummer erneut ueber denselben bestaetigten Oxaion-Ablauf. Stimmen `PEPENU` und `PEPENA` nicht mehr eindeutig mit dem ausgewaehlten Mitarbeiter ueberein, wird keine Materialbuchung gestartet.
