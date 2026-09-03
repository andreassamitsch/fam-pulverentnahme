# Oxaion-Sachmerkmale fuer Artikel-Erkennungsfarben

Stand: 03.09.2026

## Zweck

Nach dem Scan des Maschinentanks wird der aktuelle Pulverartikel aus dem eindeutigen positiven Oxaion-Tankbestand abgeleitet. Zu diesem Artikel sollen die beiden Sachmerkmale `EFA01` und `EFA02` gelesen und als geteiltes Farbfeld in der PWA dargestellt werden.

Die Farbdarstellung dient der schnellen visuellen Gegenkontrolle durch das Produktionspersonal. Sie ist eine Bedienhilfe und kein Buchungsschluessel. Ein Fehler beim Lesen der Farben darf deshalb nicht als erfolgreiche oder fehlgeschlagene Materialbuchung interpretiert werden.

## Fachliche Bedeutung

Verbindlich:

- `EFA01` = Erkennungs Farbe 1 = linke Haelfte des Farbfelds
- `EFA02` = Erkennungs Farbe 2 = rechte Haelfte des Farbfelds
- Die Sachmerkmalsauspraegung enthaelt einen sechsstelligen RGB-HEX-Wert ohne notwendiges `#`.
- In der PWA werden neben der Farbe auch Merkmalsname, Farbbezeichnung und HEX-Wert als Text angezeigt. Farbe ist damit nicht die einzige Information.

Bestaetigter Referenzartikel `RP.00010`:

```text
EFA01 = 0D0D0D = Schwarz
EFA02 = 7030A0 = Violett
```

Dies entspricht dem bestehenden Chargenetikett: linke Farbhaelfte Schwarz, rechte Farbhaelfte Violett.

## Bestaetigter Oxaion-Datenstrom

Der am 03.09.2026 bereitgestellte JET-/HTTP-Mitschnitt fuer `RP.00010` bestaetigt folgenden rein lesenden Ablauf.

### 1. Sachmerkmalskontext speichern

```text
US17000J *SAVKEY
  TLIDNR = RP.00010
  PGMN   = US21000
  KEYTYPE = UTLSM
  state  = ANZEIGEN
```

Die Antwort liefert eine neue `SSID`. Diese wird im folgenden Aufruf als `CPY-FRSSID` verwendet.

### 2. Eigenschaften / Listenkontext anfordern

```text
US17000J *PROPERTY
  TLIDNR     = RP.00010
  CPY-FRSSID = <SSID aus *SAVKEY>
  SSID       = <laufende App-Tunnel-Session-ID>
  PGMN       = US21000
  KEYTYPE    = UTLSM
  state      = ANZEIGEN
```

Im interaktiven Mitschnitt hatte die laufende Session-ID das Format `ANSA...`. Im Backend entspricht dies der von `/app-tunnel/connect` gelieferten Session-ID des `OxaionSession`-Objekts. Die Antwort von `*PROPERTY` liefert die fuer `US21000R` verwendete Listen-`SSID`.

### 3. Sachmerkmalmaske laden

```text
US21001J *LOAD
  NOHWPgm = US210002
  SSID    = <Listen-SSID aus *PROPERTY>
  state   = ANZEIGEN
  mode    = merge
```

### 4. Header lesen

```text
US21000R *GETHDR
  FLD        =
  CPY-FRSSID = <SSID aus *SAVKEY>
  SSID       = <Listen-SSID aus *PROPERTY>
  PFLD       =
```

### 5. Sachmerkmale lesen

```text
US21000R *FIRSTLIST
  SSID = <Listen-SSID aus *PROPERTY>
  mode = replace
```

Der bestaetigte Referenzfall lieferte beide benoetigten Zeilen und `<STOP/>` bereits mit `*FIRSTLIST`.

Es wird derzeit bewusst **kein** `US21000R *NEXTLIST` erfunden. Falls ein Artikel bei `*FIRSTLIST` kein `<STOP/>` liefert, behandelt das Backend die Farbabfrage als nicht vollstaendig bestaetigt und zeigt die Farben nicht als verlaessliche Bedienhilfe an. Ein realer Mitschnitt eines paginierten Sachmerkmalfalls waere dann erforderlich.

## Relevante Rueckgabefelder

Merkmalsname:

```text
UYASMP.ASSMMN
```

beziehungsweise im Schluessel:

```text
KEY/ASSMMN
```

HEX-Ausprägung:

```text
UYASMP.ASSMMA
```

Farbenbezeichnung:

```text
_INTERN.SMMABZ
```

Referenzzeilen:

```text
ASSMMN = EFA01
ASSMMA = 0D0D0D
SMMABZ = Schwarz

ASSMMN = EFA02
ASSMMA = 7030A0
SMMABZ = Violett
```

## Validierung

Die PWA darf nur Werte als CSS-Farbe verwenden, die nach Bereinigung eines optionalen fuehrenden `#` exakt dem Muster entsprechen:

```text
[0-9A-Fa-f]{6}
```

Andere Inhalte werden nicht in einen CSS-Style eingesetzt.

Der Backend-Parser normalisiert gueltige Werte auf Grossbuchstaben und liefert zum Beispiel:

```text
0d0d0d  -> 0D0D0D
#7030A0 -> 7030A0
```

## Darstellung in der PWA

Nach einem eindeutigen Maschinentankbestand:

1. Artikel wird aus Oxaion abgeleitet.
2. `EFA01`/`EFA02` werden ueber den oben bestaetigten Sachmerkmalsablauf gelesen.
3. Die PWA zeigt ein quadratisches Feld.
4. Linke Haelfte = `EFA01`.
5. Rechte Haelfte = `EFA02`.
6. Daneben werden beide Merkmale mit Farbbezeichnung und HEX-Wert ausgeschrieben.

Beispiel:

```text
[ Schwarz | Violett ]
EFA01 Schwarz (#0D0D0D) · EFA02 Violett (#7030A0)
```

## Fehlerverhalten

- Fehlt `EFA01` oder `EFA02`, wird der fehlende Teil nicht als frei angenommene Farbe dargestellt.
- Ist ein HEX-Wert ungueltig, wird er nicht als CSS-Farbe verwendet.
- Scheitert der rein lesende Oxaion-Sachmerkmalsabruf, bleibt der eindeutige Maschinenbestand davon fachlich getrennt.
- Die PWA zeigt die fehlende Farbinformation sichtbar an, darf daraus aber weder eine Materialfreigabe noch eine Buchungsablehnung ableiten.
- Vor der Materialbuchung gelten unveraendert die bestehenden serverseitigen Maschinen-, Personal- und Quellenbestandspruefungen.
