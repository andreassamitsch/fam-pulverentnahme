# Bedien- und Eingaberegeln: Pulver nachfuellen

## Verbindliche Entscheidungen vom 02.09.2026

Der Nachfuellprozess wird von der ausgewaehlten Maschine und den aktuellen Oxaion-Bestaenden gefuehrt. Freie Eingaben werden auf die wirklich notwendigen Bedienerentscheidungen reduziert.

### Maschine / Tank-Lagerort

- Der Maschinen-Lagerort wird aus einer gepflegten Whitelist ausgewaehlt; im aktuellen STAGING-Stand sind `EOS1` und `EOS2` konfiguriert.
- Dieselbe Whitelist ist spaeter die Referenz fuer den Maschinen-QR-Code: der Scan ersetzt die manuelle Auswahl, muss aber gegen die Liste validiert werden.
- Der gewaehlte Maschinen-Tanklagerort darf nicht als Quelllager einer Nachfuellcharge angeboten oder vom Backend akzeptiert werden.

### Artikel aus Maschinenbestand

Nach Wahl des Maschinen-Lagerorts liest das Backend die komplette bestaetigte `LB30230R`-Lagerortliste und wertet Bestand ungleich 0 aus.

Fuer den Nachfuellprozess gilt:

- genau ein positiver `KGM`-Bestand: Artikel, Artikelbezeichnung, aktuelle Mix-Charge und Tankmenge werden daraus abgeleitet;
- kein positiver Bestand: der Artikel kann in diesem Prozess noch nicht aus der Maschine abgeleitet werden; Nachfuellen wird bis zum spaeteren FA-/Leerbefuellungsablauf nicht freigegeben;
- mehrere positive Bestaende: nicht eindeutig, keine Buchung;
- negative oder unerwartete Mengeneinheiten: keine Buchung.

Artikel und Artikelbezeichnung sind reine Systeminformationen und nicht editierbar.

### Mitarbeiter

Die Personalnummer wird ohne fuehrende Nullen eingegeben. Name und Kuerzel werden aus Oxaion gelesen; eine freie Namenseingabe gibt es nicht. Details siehe `docs/OXAION_PERSONNEL_LOOKUP.md`.

### Nachfuellquellen

- Quell-Lagerort nur aus positivem Oxaion-Bestand zum abgeleiteten Artikel.
- Der aktuelle Maschinen-Tanklagerort wird server- und clientseitig ausgeschlossen.
- Erst nach Wahl des Quell-Lagerorts wird das Dropdown fuer die konkrete Lagerplatz-/Chargenposition eingeblendet.
- Erst nach Wahl der Bestandsposition werden Lagerplatz, Charge, verfuegbarer Bestand und Mengeneingabe angezeigt.
- Mehrere Nachfuellchargen bleiben zulaessig.

### Neue Mix-Charge

Der Ziel-Lagerort ist immer die ausgewaehlte Maschine und wird nicht erneut eingegeben.

Die Mix-Chargennummer wird nicht frei editiert. Sie wird bei Erstellung beziehungsweise ueber `Mix-Charge neu erzeugen` nach folgendem Muster erzeugt:

```text
<Artikel ohne Punkt>MIX_<yyyyMMdd>_<HHmmss>
```

Beispiel fuer `RP.00010`:

```text
RP00010MIX_20260902_162312
```

Die Anzeige `Mix Charge erstellt am` zeigt den Erzeugungszeitpunkt. Fuer das bisherige Oxaion-Feld `PSPRDT` wird dabei das aktuelle Datum verwendet.

### Buchungsdatum und Buchungstext

- Das Buchungsdatum ist keine Bedienereingabe und ist bei einem neuen Nachfuellvorgang immer das aktuelle Datum.
- Der Buchungstext ist keine freie Eingabe. Er lautet dynamisch `Pulver nachfuellen <Maschinen-Lagerort>`, z. B. `Pulver nachfuellen EOS2`.

### Darstellung

Editierbare Pflichtfelder werden optisch deutlich von automatisch aus Oxaion beziehungsweise aus Prozessregeln abgeleiteten Informationsfeldern getrennt. Nicht nutzbare Kind-Dropdowns werden bis zur Wahl des Elternfelds ausgeblendet.

## Backend-Sicherheitsregeln

Vor einer neuen Materialbuchung bestaetigt das Backend weiterhin Maschinenbestand, Mitarbeiter und jede Nachfuellquelle erneut. Zusaetzlich werden Ziel=Maschine, Ausschluss des Maschinenlagers als Quelle, dynamischer Buchungstext, aktuelles Buchungs-/Erstellungsdatum und Mix-Chargenschema serverseitig geprueft. Bei Abweichungen wird keine schreibende Oxaion-Materialbuchung gestartet.

Ein bewusster neuer Versuch nach einem historisch eindeutig `REJECTED` Vorgang behaelt dagegen gemaess bestehender Idempotenzentscheidung exakt die alten Buchungsdaten; er wird nicht stillschweigend auf das neue Namens-/Datumsformat umgeschrieben.
