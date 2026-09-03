# Oxaion-Personalpruefung fuer Pulverentnahme

## Status

Der lesende Ablauf wurde am 02.09.2026 aus einem realen JET-Datenstrom rekonstruiert. Die Feldzuordnung fuer die Bedieneranzeige wurde am 03.09.2026 anhand des STAGING-Tests korrigiert: `PEPENA` ist fuer diesen Ablauf das zu verwendende Feld fuer den vollstaendigen Namen. `PESAKZ` wird nicht verwendet, da es nicht fuer jeden Mitarbeiter gepflegt ist.

Ein weiterer realer JET-Mitschnitt vom 03.09.2026 bestaetigt die feldbezogene Oxaion-Filterung fuer eine exakte Personalnummer. Damit wird die freie Suche nicht mehr als ausreichende fachliche Personalpruefung behandelt.

## Warum die freie Suche allein nicht ausreicht

`US14090J *SEARCH` ist eine freie Suche ueber die dargestellten Listenspalten. Dadurch kann eine Eingabe wie `450` auch einen Mitarbeiter liefern, bei dem `450` beispielsweise nur in der Kostenstelle vorkommt.

Verbindliche Regel fuer die WebApp:

- Die AJAX-Suche darf `US14090J *SEARCH` weiterhin als schnelle Kandidatensuche verwenden.
- Ein Kandidat darf aber nur angezeigt werden, wenn seine normalisierte `PEPENU` mit der eingegebenen Ziffernfolge beginnt.
- Eingabe `45` darf somit `450`, `451`, `452`, `453` usw. liefern, aber niemals `245`, `345` oder einen Treffer, bei dem `45` nur in einem anderen Feld vorkommt.
- Diese Praefixpruefung erfolgt im Backend und nicht nur im Browser.
- Das Ergebnislimit wird erst nach der `PEPENU`-Praefixpruefung angewendet.

Die technische Oxaion-Syntax fuer einen nativen `PEPENU beginnt mit ...`-Filter wurde bisher nicht in einem JET-Mitschnitt bestaetigt. Insbesondere werden keine Wildcards oder Vergleichsoperatoren erfunden.

## Bestaetigte Basis-Aufrufreihenfolge

1. `MN10209J *CHKCMD` mit `CHKCMD=MA` und `_father_=CMDLINE`
2. `US14090J *LOAD`
3. `US14090J *GETCFTIT`
4. `US14090J *FIRSTLIST`
5. fuer die AJAX-Kandidatensuche: `US14090J *SEARCH`
6. `US14000J *LOAD`
7. `US14000J *READ` fuer die nach `PEPENU` zugelassenen Personalsaetze

Fuer die WebApp werden aus dem gelesenen Personalsatz nur folgende Felder verwendet:

```text
PEPENU: Oxaion-Personalnummer
PEPENA: vollstaendiger Name
```

## Bestaetigter exakter Oxaion-Filter nach PEPENU

Der Mitschnitt vom 03.09.2026 bestaetigt fuer die exakte Personalnummer `450` folgende Folge nach dem Aufbau der Personalliste:

1. `US14001R *GETFILTER` mit `SSID`
2. `US14001 *SAVCURSET` mit `SSID`
3. `US14001R *GETSLTV` mit `SSID`
4. `US14001R *GETSLTATR` mit den von `GETSLTV` gelieferten Selektionsfeldern und `mode=merge`
5. `US14001R *CHKSLTV` mit den Selektionsfeldern und `IPENU=0000000450`
6. `US14090J *FIRSTLIST` mit `SSID`, `FROM_PGMN=MAINFILTER` und `mode=replace-children`
7. `US14000J *LOAD`
8. `US14000J *READ` fuer den eindeutig gefilterten Personalsatz

Der bestaetigte Filter lieferte fuer `IPENU=0000000450` genau `PEPENU=0000000450`. Dieser feldbezogene Ablauf wird fuer die erneute exakte Personalpruefung unmittelbar vor einer Materialbuchung verwendet.

## Feldbedeutung fuer die WebApp

- `PEPENU`: Oxaion-Personalnummer; Oxaion liefert sie intern mit fuehrenden Nullen. Die WebApp zeigt und speichert die Personalnummer ohne fuehrende Nullen.
- `PEPENA`: vollstaendiger Name des Mitarbeiters; wird als kanonischer Name in die Buchungsdokumentation uebernommen.
- `IPENU`: Selektionsfeld des bestaetigten Oxaion-Filters fuer eine exakte Personalnummer; die Nummer wird dabei zehnstellig mit fuehrenden Nullen uebergeben.
- `PESAKZ`: wird bewusst weder fuer die Identifikation noch fuer die Anzeige benoetigt, weil das Feld nicht fuer jeden Mitarbeiter gepflegt ist.
- `PENLAE`: wird fuer den vollstaendigen Mitarbeiternamen in diesem Ablauf nicht verwendet.

## UI-Regel

Der Bediener gibt nur Ziffern ein. Die Suche bleibt als AJAX-Suche mit kurzer Verzoegerung erhalten. Angezeigt werden ausschliesslich Treffer nach dem Muster

```text
446 - Andreas Samitsch
```

also fachlich:

```text
PEPENU - PEPENA
```

Der Treffer muss bewusst angeklickt beziehungsweise bestaetigt werden. Eine freie Namenseingabe gibt es nicht.

## Sicherheitsregel vor Buchung

Die Auswahl im Browser ist keine dauerhafte Wahrheit. Direkt vor dem ersten schreibenden Materialbuchungsaufruf wird die gewaehlte Personalnummer erneut ueber den bestaetigten feldbezogenen Oxaion-Filter `IPENU` gelesen. Stimmen `PEPENU` und `PEPENA` nicht mehr eindeutig mit dem ausgewaehlten Mitarbeiter ueberein, wird keine Materialbuchung gestartet.
