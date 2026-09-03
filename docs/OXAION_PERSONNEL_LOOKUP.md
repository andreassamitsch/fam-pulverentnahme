# Oxaion-Personalpruefung fuer Pulverentnahme

## Status

Der lesende Ablauf wurde am 02.09.2026 aus einem realen JET-Datenstrom rekonstruiert. Er dient dazu, die Personalnummer des Bedieners in Oxaion zu bestaetigen und Name/Kuerzel nicht frei erfassen zu muessen.

## Bestaetigte Aufrufreihenfolge

1. `MN10209J *CHKCMD` mit `CHKCMD=MA` und `_father_=CMDLINE`
2. `US14090J *LOAD`
3. `US14090J *GETCFTIT`
4. `US14090J *FIRSTLIST`
5. `US14090J *SEARCH` mit der vom Bediener eingegebenen Personalnummer ohne fuehrende Nullen, z. B. `SEARCH=446`
6. `US14000J *LOAD`
7. `US14000J *READ` fuer den ausgewaehlten Personalsatz

Der Referenzfall ergab:

```text
Eingabe: 446
PEPENU:  0000000446
PESAKZ:  ANSA
PENLAE:  Andreas Samitsch
```

## Feldbedeutung fuer die WebApp

- `PEPENU`: Oxaion-Personalnummer; Oxaion liefert sie intern mit fuehrenden Nullen. Die WebApp zeigt/speichert die Bedienereingabe ohne fuehrende Nullen.
- `PESAKZ`: Personal-Kuerzel fuer die Trefferanzeige.
- `PENLAE`: vollstaendiger Name; wird als kanonischer Name in die Buchungsdokumentation uebernommen.

## UI-Regel

Der Bediener gibt nur Ziffern ein. Die WebApp sucht nach kurzer Verzoegerung in Oxaion und zeigt Treffer nach dem Muster

```text
446 · ANSA · Andreas Samitsch
```

Der Treffer muss bewusst angeklickt/bestaetigt werden. Eine freie Namenseingabe gibt es nicht.

## Sicherheitsregel vor Buchung

Die Auswahl im Browser ist keine dauerhafte Wahrheit. Direkt vor dem ersten schreibenden Materialbuchungsaufruf liest das Backend die Personalnummer erneut ueber denselben bestaetigten Oxaion-Ablauf. Stimmen Personalnummer und kanonischer Name nicht mehr eindeutig, wird keine Materialbuchung gestartet.

## STAGING-Befund vom 03.09.2026

Beim ersten realen Test der erneuten serverseitigen Personalpruefung vor der Buchung wurde der Vorgang mit `PERSONNEL_UNAVAILABLE` in `PERSONNEL_VALIDATION` gestoppt. Der Oxaion-Client konnte eine Antwort innerhalb des lesenden Personalablaufs nicht als XML parsen. Zu diesem Zeitpunkt war noch keine schreibende Materialbuchung gestartet; es liegt daher kein unklarer Buchungsausgang vor.

Die konkrete fehlerhafte Teilantwort war mit der bisherigen allgemeinen Meldung `Oxaion response was not valid XML.` nicht identifizierbar. Der STAGING-Branch ergaenzt deshalb die XML-Diagnose um den konkreten Kontext `CONNECT` beziehungsweise `Programm + Aktion`, HTTP-Status, Content-Type, Antwortklassifikation und XML-Parserposition. Der Antwortinhalt selbst wird dabei nicht in die Fehlermeldung uebernommen, damit keine Personal- oder Sitzungsdaten offengelegt werden.

Bis ein erneuter Live-Test die konkrete Teilantwort und den vollstaendigen Ablauf bestaetigt, bleibt die HTTP-Personalpruefung als noch nicht live bestaetigt markiert. Die bestaetigte JET-Aufrufreihenfolge wird nicht auf Verdacht geaendert.
