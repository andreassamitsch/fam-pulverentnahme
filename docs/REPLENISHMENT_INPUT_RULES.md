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

Die Personalnummer wird ohne fuehrende Nullen eingegeben. Die Suche bleibt eine AJAX-Suche. Die eingegebene Ziffernfolge wird verbindlich als Praefix der normalisierten Oxaion-Personalnummer `PEPENU` behandelt: Eingabe `45` darf beispielsweise `450`, `451`, `452`, `453` usw. anzeigen, aber nicht `245`, `345` oder Treffer, bei denen `45` nur in Kostenstelle, Name oder einem anderen Listenfeld vorkommt.

Die WebApp liest Personalnummer und vollstaendigen Namen aus Oxaion und zeigt ausschliesslich `PEPENU - PEPENA`, zum Beispiel `446 - Andreas Samitsch`. Eine freie Namenseingabe gibt es nicht. `PESAKZ` wird nicht verwendet, da es nicht fuer jeden Mitarbeiter gepflegt ist. Details siehe `docs/OXAION_PERSONNEL_LOOKUP.md`.

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

## Manueller Uebergangsprozess bis zur produktiven App

Bis der produktive App-Ablauf die Oxaion-Buchung uebernimmt, soll die Produktion den Nachfuellvorgang schriftlich mit moeglichst denselben Bedienerangaben erfassen, die spaeter auch in der App bewusst ausgewaehlt oder eingegeben werden. Systemdaten werden nicht zusaetzlich von der Produktion verlangt.

Verbindlich gilt fuer die Produktion beim manuellen Nachfuellen:

- Die Produktion muss die auf der Maschine vorhandene alte Mix-Charge nicht kennen oder aufschreiben.
- Die Produktion muss den aktuellen Oxaion-Tankbestand nicht ermitteln oder aufschreiben.
- Artikelbezeichnung, neu zu erzeugende Mix-Charge und Buchungstext sind ebenfalls keine zusaetzlichen manuellen Produktionsangaben.
- Die Produktion dokumentiert zwingend die tatsaechlich neu eingefuellte Rohmaterial-/Herstellercharge. Diese ist von der spaeter neu erzeugten Mix-Charge zu unterscheiden.
- Die Produktion dokumentiert fuer jede eingefuellte Charge die tatsaechlich eingefuellte Menge.
- Die Produktion dokumentiert das tatsaechliche Datum des Einfuellens. Dieses Einfuelldatum ist der physische Vorgangszeitpunkt und kann vom spaeteren manuellen Buchungstag der Produktionsleitung abweichen.
- Die Produktionsleitung ermittelt beim spaeteren manuellen Buchen zuerst den fuer den Vorgang massgeblichen Oxaion-Maschinenbestand und daraus insbesondere alte Mix-Charge und Tankbestand.
- Die neue Mix-Charge wird bei der manuellen Buchung nach dem verbindlichen Mix-Chargenschema erzeugt.
- Die schriftliche Uebergabe der Produktion soll die fachlich notwendigen Bediener-/Bewegungsdaten enthalten: Personalnummer, Fertigungsauftrag beziehungsweise die im FA-Kontext benoetigten Identifikationsdaten, tatsaechlich verwendete Maschine sowie je verwendeter Nachfuellquelle Lagerort/Lagerplatz soweit fuer die Produktion eindeutig erkennbar, Rohmaterial-/Herstellercharge, eingefuellte Menge und Einfuelldatum.
- Werden mehrere Nachfuellchargen verwendet, wird jede tatsaechlich verwendete Charge mit ihrer eigenen Menge dokumentiert. Das Einfuelldatum wird je Vorgang beziehungsweise, falls die Chargen an unterschiedlichen Tagen eingefuellt wurden, je Charge festgehalten.
- Die Produktionsleitung ergaenzt die fuer die Oxaion-Buchung erforderlichen Systemdaten und kennzeichnet den Vorgang nach erfolgreicher Buchung eindeutig als gebucht, damit keine Doppelbuchung entsteht.

Ziel dieses Uebergangsprozesses ist ausdruecklich, keine zusaetzliche Parallel-Datenerfassung fuer die Produktion aufzubauen, die spaeter mit Einfuehrung der App wieder entfaellt.

## Backend-Sicherheitsregeln

Vor einer neuen Materialbuchung bestaetigt das Backend weiterhin Maschinenbestand, Mitarbeiter und jede Nachfuellquelle erneut. Bei der Mitarbeiterpruefung wird die ausgewaehlte Personalnummer ueber den bestaetigten feldbezogenen Oxaion-Filter `IPENU` exakt gelesen; danach werden `PEPENU` und `PEPENA` gegen die Browserauswahl geprueft. Die freie `US14090J *SEARCH`-Suche ist fuer diese Sicherheitspruefung nicht ausreichend. Zusaetzlich werden Ziel=Maschine, Ausschluss des Maschinenlagers als Quelle, dynamischer Buchungstext, aktuelles Buchungs-/Erstellungsdatum und Mix-Chargenschema serverseitig geprueft. Bei Abweichungen wird keine schreibende Oxaion-Materialbuchung gestartet.

Ein bewusster neuer Versuch nach einem historisch eindeutig `REJECTED` Vorgang behaelt dagegen gemaess bestehender Idempotenzentscheidung exakt die alten Buchungsdaten; er wird nicht stillschweigend auf das neue Namens-/Datumsformat umgeschrieben.
