# Mehrere Nachfuellchargen in einem Pulver-Nachfuellvorgang

## Fachliche Entscheidung

Beim Prozess **Pulver nachfuellen** koennen in einem Vorgang eine oder mehrere neue Pulverchargen in den Maschinentank eingefuellt werden.

Alle beteiligten Pulverquellen werden auf **eine gemeinsame neue Mix-Charge** gebucht. Der bereits vorhandene Maschinenbestand wird weiterhin vollstaendig aus Oxaion ermittelt und ist die erste Quelle des neuen Mixes.

Der Bediener darf die vorhandene Mix-Charge und deren Restmenge nicht manuell veraendern. Fuer jede zusaetzliche Nachfuellcharge wird die konkrete Quell-Bestandsposition aus Oxaion ausgewaehlt. Damit sind nicht frei editierbar:

- Quell-Lagerort
- interner Lagerplatzschluessel, soweit der Lagerort eine Lagerplatzorganisation besitzt
- Charge

Manuell eingegeben wird die Einfuellmenge. Sie darf den aktuell verfuegbaren Bestand der ausgewaehlten Oxaion-Position nicht ueberschreiten.

Mindestens eine Nachfuellcharge ist fuer den derzeitigen Nachfuell-Prototyp erforderlich. Details zur Auswahl siehe `docs/OXAION_SOURCE_STOCK_LOOKUP.md`.

## Oxaion-Positionsmodell

Die bestehende, praktisch getestete Buchungslogik wird positionsweise erweitert:

```text
Position 1
  vorhandene Mix-Charge / kompletter Maschinenbestand
  -> neue Mix-Charge

Position 2
  Nachfuellcharge 1
  -> dieselbe neue Mix-Charge

Position 3
  Nachfuellcharge 2
  -> dieselbe neue Mix-Charge

Position 4
  Nachfuellcharge 3
  -> dieselbe neue Mix-Charge

usw.
```

Jede Position erzeugt das bereits bekannte LM/LN-Paar. Bei `N` zusaetzlichen Nachfuellchargen werden deshalb insgesamt

```text
1 + N Positionen
2 * (1 + N) LM/LN-Bewegungen
```

erwartet.

## Technisch nachgewiesen vs. verallgemeinert

Praktisch in Oxaion STAGING bestaetigt ist bisher:

- Position 1: alte Mix-Charge -> neue Mix-Charge
- Position 2: eine neue Pulvercharge -> dieselbe neue Mix-Charge
- eine weitere reale Nachfuellposition mit `H04HRL / RE1F3 / 84671` wurde im Datenstrom als erfolgreiche Position 3 mit LM/LN sichtbar; damit ist insbesondere der interne Lagerplatzschluessel `RE1F3` fuer diesen Referenzfall bestaetigt
- nach Position 1 wird die persistierte LN-Zielzeile neu gelesen und deren echter `PSBGZT` als Fortsetzungszustand fuer Position 2 verwendet
- fuer Fortsetzungspositionen gilt die bereits dokumentierte Unterscheidung zwischen `TCODE=WIN3` und dem direkt final validierten `PUTNEW`-Zustand

Der STAGING-Prototyp verwendet fuer Position 3 und weitere Positionen dieselbe bestaetigte Fortsetzungssequenz mit fortlaufender `PSPOSI`/`SNR`. Nach jeder erfolgreich bestaetigten Position wird die zu genau dieser Position gehoerende persistierte LN-Zielzeile neu gelesen und als Zustand fuer die naechste Position verwendet.

Die allgemeine Verallgemeinerung fuer beliebig viele Positionen bleibt automatisiert getestet; ein vollstaendiger WebApp-Durchlauf mit mindestens zwei ueber die neue Oxaion-Auswahl gewaehlten Nachfuellchargen soll noch als Gesamtintegration in STAGING bestaetigt werden.

## Quellbestandsauswahl und Revalidierung

Vor dem Erzeugen des Buchungsrequests werden Nachfuellquellen aus aktuellen positiven Oxaion-Bestaenden gewaehlt:

1. `LB30340R` liefert Lagerorte/Chargen mit Artikelbestand.
2. `LB30430R` liefert fuer den gewaehlten Lagerort den exakten internen Lagerplatzschluessel, Charge und Lagerplatzbestand.
3. Bei eindeutigem `LAG1626` besitzt der Lagerort keine Lagerplatzorganisation; dann bleibt der Lagerplatz leer und der bestaetigte `LB30230R`-Lagerortbestand wird verwendet.

Direkt vor dem ersten schreibenden Oxaion-Aufruf validiert das Backend jede Quelle erneut. Stimmen Artikel, Lagerort, Lagerplatz, Charge oder verfuegbare Menge nicht mehr, wird der gesamte Vorgang vor der Materialbuchung gestoppt.

Dieselbe exakte Bestandsposition darf innerhalb eines Multi-Batch-Vorgangs nicht zweimal vorkommen; die gewuenschte Menge ist in einer Quelle zusammenzufassen.

## Transaktion und Idempotenz

Alle Nachfuellchargen gehoeren zu derselben `clientOperationId` und derselben Backend-Transaktion.

Ein Vorgang darf nicht in einzelne unabhaengige Buchungsrequests pro Charge zerlegt werden, weil dadurch bei Netzwerkfehlern die Korrelation und Duplicate Prevention geschwaecht wuerden.

Der komplette Request speichert die Nachfuellquellen als geordnete Liste `additionalSources`. Fuer Rueckwaertskompatibilitaet mit bereits gespeicherten STAGING-Transaktionen bleiben die bisherigen Felder der ersten Nachfuellcharge vorerst erhalten und spiegeln den ersten Listeneintrag.

## Recovery bei mehreren Positionen

Die Ergebnisanalyse erwartet fuer jede Position exakt ein LM- und ein LN-Paar mit:

- Positionsnummer
- Buchungskennzeichen
- Artikel
- Charge
- Lagerort/Lagerplatz
- Menge

Recovery bestimmt den laengsten vollstaendig vorhandenen Positions-Prefix.

Beispiele:

```text
keine Bewegung vorhanden
-> Position 1 aufbauen, danach alle Nachfuellpositionen

Position 1 vorhanden
-> ab Position 2 fortsetzen

Position 1 und 2 vorhanden
-> ab Position 3 fortsetzen

alle erwarteten Positionen vorhanden
-> keine weitere Position buchen; nur Abschluss/Verifikation

Luecke, Duplikat oder unerwartete Zusatzbewegung
-> MANUAL_REVIEW_REQUIRED
```

Der fuer die Fortsetzung benoetigte validierte Oxaion-Zustand wird je Position persistent in der Backend-Transaktion gespeichert. Dadurch muss Recovery nach einem Antwortverlust nicht einen Zustand einer anderen Position erraten.

Ein Transportfehler waehrend oder nach `LB20110R *UPD` bleibt `UNCERTAIN`, bis der Oxaion-Beleg erneut gelesen und der tatsaechliche Positionsstand festgestellt wurde. Es gibt keinen blinden Retry einer bereits moeglicherweise gebuchten Position.

## Abschlusspruefung

`SUCCESS` wird erst gesetzt, wenn nach erneutem Oeffnen des Oxaion-Belegs **alle erwarteten LM/LN-Bewegungen exakt einmal** vorhanden sind.

Die fruehere feste Erwartung von vier Bewegungen gilt damit nur noch fuer den Sonderfall mit genau einer Nachfuellcharge.

## Bewusster neuer Versuch nach REJECTED

Bei einem eindeutig `REJECTED` abgelehnten Vorgang darf nach Behebung der Ursache weiterhin ein neuer verknuepfter Versuch erzeugt werden.

Dabei muessen neben den bisherigen Buchungsdaten auch:

- Anzahl der Nachfuellchargen
- Reihenfolge der Nachfuellchargen
- Lagerorte/Lagerplaetze
- Chargen
- Mengen

identisch bleiben. Der neue Versuch erhaelt eine neue `clientOperationId` und verweist ueber `retryOfClientOperationId` auf den abgelehnten Vorgang.

Wurde ein alter Vorgang wegen eines falschen frei eingegebenen Lagerplatzschluessels abgelehnt, darf der Same-Data-Retry diesen Wert nicht stillschweigend korrigieren. Die korrekte Oxaion-Bestandsposition ist neu auszuwaehlen und als neuer normaler Buchungsvorgang zu starten.

## Bedienoberflaeche

Die STAGING-Oberflaeche zeigt mindestens eine Nachfuellcharge und bietet die Aktion **Weitere Nachfuellcharge**.

Jede weitere Charge wird als eigener Block angezeigt. Lagerort und konkrete Bestandsposition werden ueber Dropdowns aus Oxaion geladen; interner Lagerplatzschluessel, Charge und verfuegbarer Bestand werden nur angezeigt. Die Zusammenfassung vor der echten STAGING-Buchung zeigt alle Quellen, ihre Mengen, die resultierende Anzahl Oxaion-Positionen und die erwartete Anzahl LM/LN-Bewegungen.

Solange ein Vorgang offen oder unklar ist, koennen keine Nachfuellquellen hinzugefuegt oder entfernt werden.
