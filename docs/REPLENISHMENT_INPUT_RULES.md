# Bedien- und Eingaberegeln: Pulver nachfuellen

## Verbindliche Entscheidungen, Stand 03.09.2026

Der Nachfuellprozess wird vom physisch gescannten Maschinentank und den aktuellen Oxaion-Bestaenden gefuehrt. Freie Eingaben werden auf die wirklich notwendigen Bedienerentscheidungen reduziert.

### Maschinentank

- Der Maschinentank wird im normalen STAGING-Nachfuellablauf per QR gescannt; eine sichtbare manuelle Tankauswahl ist nicht mehr vorgesehen.
- Der Tank-QR enthaelt ausschliesslich den Oxaion-Tanklagerort, zum Beispiel `EOS1`.
- Der gescannte Wert muss gegen eine gepflegte Whitelist validiert werden; im aktuellen STAGING-Stand sind `EOS1` und `EOS2` konfiguriert.
- Ein mehrteiliger Code mit `+++` wird im Maschinentank-Schritt nicht akzeptiert.
- Der gescannte Maschinentank darf nicht als Quelllager einer Nachfuellcharge angeboten oder vom Backend akzeptiert werden.

Die Produktionsmaschinen-ID aus einem Fertigungsauftrag, zum Beispiel `EP-M650-1`, ist nicht identisch mit dem Tanklagerort. Die verbindliche Zuordnung zwischen Produktionsmaschine und Maschinentank ist noch offen und wird nicht erfunden.

### Artikel aus Maschinentankbestand

Nach Scan des Maschinentanks liest das Backend die komplette bestaetigte `LB30230R`-Lagerortliste und wertet Bestand ungleich 0 aus.

Fuer den Nachfuellprozess gilt:

- genau ein positiver `KGM`-Bestand: Artikel, Artikelbezeichnung, aktuelle Mix-Charge und Tankmenge werden daraus abgeleitet;
- kein positiver Bestand: der Artikel kann in diesem Prozess noch nicht aus dem Tank abgeleitet werden; Nachfuellen wird bis zum spaeteren FA-/Leerbefuellungsablauf nicht freigegeben;
- mehrere positive Bestaende: nicht eindeutig, keine Buchung;
- negative oder unerwartete Mengeneinheiten: keine Buchung.

Artikel und Artikelbezeichnung sind reine Systeminformationen und nicht editierbar.

### Mitarbeiter

Die Personalnummer kann weiterhin ohne fuehrende Nullen manuell eingegeben werden. Die Suche bleibt eine AJAX-Suche. Die eingegebene Ziffernfolge wird verbindlich als Praefix der normalisierten Oxaion-Personalnummer `PEPENU` behandelt: Eingabe `45` darf beispielsweise `450`, `451`, `452`, `453` usw. anzeigen, aber nicht `245`, `345` oder Treffer, bei denen `45` nur in Kostenstelle, Name oder einem anderen Listenfeld vorkommt.

Die WebApp liest Personalnummer und vollstaendigen Namen aus Oxaion und zeigt ausschliesslich `PEPENU - PEPENA`, zum Beispiel `446 - Andreas Samitsch`. Eine freie Namenseingabe gibt es nicht. `PESAKZ` wird nicht verwendet, da es nicht fuer jeden Mitarbeiter gepflegt ist. Details siehe `docs/OXAION_PERSONNEL_LOOKUP.md`.

Zusaetzlich kann der Mitarbeiter per NFC-Chip identifiziert werden. Nach erfolgreicher Syncos-RFID-Aufloesung wird dieselbe Personalnummer ueber den bestaetigten Oxaion-`IPENU`-Ablauf exakt bestaetigt. Erst danach wird der Mitarbeiter automatisch ausgewaehlt. Die manuelle Personalsuche bleibt als Fallback bestehen. Bei physischer Chiperkennung erzeugt die PWA einen kurzen browsergenerierten Ton; dieser bestaetigt nur die Erkennung des Chips, nicht bereits die erfolgreiche Mitarbeiterpruefung.

### Nachfuellquellen

Der Bediener waehlt Lagerort und Charge nicht mehr vorab aus Dropdowns. Er geht zur physisch verwendeten Charge und scannt deren QR-Code.

Chargen-QR:

```text
Artikel+++Charge
```

Beispiele:

```text
RP.00006+++88688
RP.00010+++RP00010MIX_20260903_132212
```

Verbindliche Bedienlogik:

- Die Artikelnummer aus dem QR muss zum aus dem Maschinentank abgeleiteten Artikel passen.
- Die PWA liest die positiven Oxaion-Quell-Lagerorte des Artikels; der Maschinentank selbst wird ausgeschlossen.
- Fuer diese Lagerorte werden die positiven Lagerplatz-/Chargenpositionen gelesen und auf die exakt gescannte Charge eingeschraenkt.
- Genau eine eindeutige Bestandsposition: Lagerort und gegebenenfalls interner Lagerplatz werden automatisch uebernommen.
- Mehrere Bestandspositionen fuer dieselbe Charge: der Bediener muss den tatsaechlich verwendeten Entnahmeort beziehungsweise Lagerplatz bewusst bestaetigen.
- Kein eindeutiger Treffer oder ein nicht sicher lesbarer Oxaion-Bestand: keine Buchungsfreigabe.
- Erst nach eindeutiger Zuordnung werden Lagerort/Lagerplatz, Charge, verfuegbarer Bestand und die Mengeneingabe angezeigt.
- Die Einfuellmenge bleibt eine Bedienereingabe und darf den verfuegbaren Bestand nicht ueberschreiten.
- Mehrere Nachfuellchargen bleiben zulaessig; jede weitere Charge wird erneut physisch gescannt.
- Dieselbe exakte Oxaion-Bestandsposition darf innerhalb eines Vorgangs nicht doppelt verwendet werden.

Details siehe `docs/OXAION_SOURCE_STOCK_LOOKUP.md` und `docs/QR_CODE_WORKFLOW.md`.

### Neue Mix-Charge

Der Ziel-Lagerort ist immer der gescannte Maschinentank und wird nicht erneut eingegeben.

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

Editierbare Pflichtfelder werden optisch deutlich von automatisch aus QR-Code, Oxaion beziehungsweise Prozessregeln abgeleiteten Informationsfeldern getrennt. Eine Auswahl des Entnahmeorts wird nur eingeblendet, wenn die gescannte Charge in Oxaion an mehreren positiven Bestandspositionen gefunden wurde.

## Backend-Sicherheitsregeln

Vor einer neuen Materialbuchung bestaetigt das Backend weiterhin Maschinentankbestand, Mitarbeiter und jede Nachfuellquelle erneut. Bei der Mitarbeiterpruefung wird die ausgewaehlte Personalnummer ueber den bestaetigten feldbezogenen Oxaion-Filter `IPENU` exakt gelesen; danach werden `PEPENU` und `PEPENA` gegen die Browserauswahl geprueft. Die freie `US14090J *SEARCH`-Suche ist fuer diese Sicherheitspruefung nicht ausreichend. Zusaetzlich werden Ziel=Maschinentank, Ausschluss des Maschinentanks als Quelle, dynamischer Buchungstext, aktuelles Buchungs-/Erstellungsdatum und Mix-Chargenschema serverseitig geprueft. Jede gescannte Nachfuellquelle wird vor dem ersten schreibenden Aufruf erneut exakt anhand von Artikel, Lagerort, internem Lagerplatz, Charge und verfuegbarer Menge validiert. Bei Abweichungen wird keine schreibende Oxaion-Materialbuchung gestartet.

Ein bewusster neuer Versuch nach einem historisch eindeutig `REJECTED` Vorgang behaelt dagegen gemaess bestehender Idempotenzentscheidung exakt die alten Buchungsdaten; er wird nicht stillschweigend auf das neue Namens-/Datumsformat umgeschrieben.
