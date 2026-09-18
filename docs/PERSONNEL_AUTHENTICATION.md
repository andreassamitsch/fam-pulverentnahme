# Mitarbeiter-Anmeldung: NFC-Zielbild und aktueller Passwort-Entwicklungsstand

## Verbindliche Zielentscheidung vom 18.09.2026

Fuer die finale Pulverentnahme-PWA ist die Mitarbeiter-Anmeldung per NFC-Chip beschlossen.

Verbindlich gilt:

- Die eingesetzten Mitarbeiterchips funktionieren mit dem vorgesehenen NFC-Ablauf.
- Wird ein Chip erfolgreich erkannt und serverseitig eindeutig einem gueltigen Mitarbeiter zugeordnet, gilt der Mitarbeiter als angemeldet.
- Bei erfolgreicher NFC-Anmeldung ist kein zusaetzliches Passwort erforderlich.
- Die Zuordnung erfolgt serverseitig auf Basis der vorhandenen SYNCOS-RFID-Zuordnung; Chip-/RFID-Werte werden nicht als frei vertrauenswuerdige Browserangabe behandelt.
- Nach erfolgreicher Zuordnung wird wie bisher eine serverseitige Session verwendet.
- Die erneute Oxaion-Personalpruefung unmittelbar vor dem ersten schreibenden Materialbuchungsaufruf bleibt als getrennte fachliche Sicherheitspruefung bestehen.
- Es gibt keinen Offline-Bypass fuer eine abgelaufene oder fehlende Anmeldung.
- Fuer den Produktivbetrieb bleibt HTTPS verbindlich.
- Ob das ASP.NET-Core-Backend spaeter hinter IIS oder direkt als Windows-Dienst mit Kestrel betrieben wird, ist fuer diese Authentifizierungsentscheidung unerheblich und derzeit noch offen.

Der aktuell vorhandene Login ueber Personalnummer und SYNCOS-Passwort bleibt waehrend der Entwicklung am Client-Rechner vorerst als technischer Zwischenstand bestehen. Er ist seit dem 18.09.2026 nicht mehr das finale Produktivkonzept.

## Aktueller Passwort-Entwicklungsstand

## Status

Am 03.09.2026 wurde fuer den damaligen STAGING-/Entwicklungsstand folgende Passwortanmeldung umgesetzt:

- Die Auswahl eines Mitarbeiters ueber die Oxaion-Personalnummer allein reicht nicht mehr fuer eine produktive Buchung.
- Nach der bewussten Auswahl des Oxaion-Mitarbeiters muss der Bediener sein Passwort eingeben.
- Die Passwortpruefung erfolgt ausschliesslich im ASP.NET-Core-Backend.
- Das Frontend kennt weder den Legacy-Schluessel noch den gespeicherten `PASSWORD`-Wert.
- Passwort, transformierter Passwortwert und Datenbank-Credential duerfen weder in `IndexedDB`, im `clientOperationId`-Vorgang, im Transaktionslog noch in Git gespeichert werden.
- Eine neue Anmeldung ist ein Online-Schritt. Bei abgelaufener Session gibt es keinen Offline-Bypass; der Bediener muss sich nach Wiederherstellung der Backend-Verbindung erneut anmelden.
- Fuer die aktuelle STAGING-/Testphase muss die WebApp inklusive Mitarbeiter-Login auch ueber normales HTTP im internen Netz testbar sein. HTTP wird deshalb im Backend nicht blockiert. Fuer den Produktivbetrieb bleibt HTTPS verbindlich.

Die bestehende Oxaion-Personalpruefung ueber `PEPENU` und `PEPENA` bleibt unveraendert bestehen. Der nachfolgend dokumentierte Passwortweg beschreibt den aktuellen Implementierungsstand bis zur NFC-Umstellung. Fuer das finale Zielbild wird die bereits vorhandene SYNCOS-RFID-Zuordnung fuer die serverseitige Chipzuordnung verwendet.

## Aktueller Ablauf des Passwort-Zwischenstands

1. Bediener gibt die Personalnummer ein.
2. Backend sucht den Mitarbeiter wie bisher ueber die bestaetigte Oxaion-Personallogik.
3. Bediener waehlt bewusst den Treffer `PEPENU - PEPENA`.
4. PWA zeigt das Passwortfeld.
5. `POST /api/personnel/login` sendet Personalnummer und Passwort an das Backend. In STAGING darf dies fuer Tests auch ueber HTTP erfolgen; produktiv muss die Verbindung ueber HTTPS laufen.
6. Backend liest den Mitarbeiter erneut exakt aus Oxaion.
7. Backend liest den aktiven und sichtbaren SYNCOS-Benutzer zur Personalnummer aus `syncos_stg_102.ITSDEV.ITSUSER`.
8. Der Lookup verwendet die bekannte Zuordnung ueber den Suffix des `OBJECTKEY`, z. B. Personalnummer `446` -> `OBJECTKEY LIKE '%446'`.
9. Backend transformiert das eingegebene Passwort mit der am 03.09.2026 rekonstruierten SYNCOS-Legacy-Logik und vergleicht die Bytes zeitkonstant mit `ITSUSER.PASSWORD`.
10. Bei Erfolg wird eine serverseitige Session angelegt. Im Browser liegt nur die HTTP-Session-Cookie-Referenz; das Passwort wird verworfen.
11. `POST /api/mix` akzeptiert neue Buchungen nur, wenn die Session vorhanden ist und Personalnummer sowie Name exakt mit dem Buchungsvorgang uebereinstimmen.
12. Direkt vor der Materialbuchung bleibt zusaetzlich die bereits bestehende erneute Oxaion-Pruefung von `PEPENU` und `PEPENA` aktiv.

Im aktuellen Passwort-Zwischenstand bestehen zwei voneinander getrennte Sicherheitspruefungen:

- Anmeldung: Personalnummer + Passwort
- fachliche Buchungspruefung: Mitarbeiter unmittelbar vor dem ersten schreibenden Oxaion-Aufruf erneut eindeutig in Oxaion bestaetigen

Im finalen NFC-Zielbild wird die erste Stufe durch die eindeutige NFC-/RFID-Zuordnung ersetzt; die zweite Stufe bleibt bestehen.

## Rekonstruierte SYNCOS-Passworttransformation

Der Datenbankwert ist kein MD5-, SHA-1-, SHA-256- oder SHA-512-Hash. Die kontrollierten Testdaten zeigen eine positionsabhaengige XOR-Transformation mit anschliessender Legacy-Zeichenbehandlung.

Bisher bestaetigter Positionsschluessel fuer 18 Zeichen:

```text
Pos: 01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18
Key: 49 FC 1F D1 49 7B FB 2E 63 1B 56 81 27 3D 00 0C 0C 86
```

Grundoperation je Zeichen:

```text
transformierter Wert = Zeichenwert XOR Positionsschluessel
```

Fallen die resultierenden Werte in den C1-Steuerzeichenbereich `0x80` bis `0x9F`, wurde in den kontrollierten Tests im gespeicherten Wert `0x3F` (`?`) beobachtet. Werte ab `0xA0` blieben byteidentisch erhalten.

### Bestaetigte Testvektoren

```text
1                  -> 78
2                  -> 7B
12                 -> 78CE
731486             -> 7ECF2EE5714D
123456789987654321 -> 78CE2CE57C4DCC165A226EB61108343F3EB7
abcdefggfedcba     -> 283F7CB52C1D3F49057E32E2455C
aBcdefGgfedcba     -> 28BE7CB52C1DBC49057E32E2455C
```

Der letzte Test wurde vorab aus der rekonstruierten Logik mit `28BE7CB52C1DBC49057E32E2455C` vorhergesagt und anschliessend exakt so von SYNCOS gespeichert. Damit ist die Transformation fuer die getesteten ASCII-Buchstaben/Ziffern eindeutig bestaetigt.

### Aktuelle Implementierungsgrenze

Die WebApp akzeptiert fuer diese Legacy-Pruefung derzeit nur:

- `0-9`
- `A-Z`
- `a-z`
- maximal 18 Zeichen

Diese Grenze ist absichtlich enger als eine erfundene Verallgemeinerung. Sonderzeichen und Positionen ab 19 sind noch nicht durch kontrollierte Testvektoren bestaetigt.

## Bestaetigte Credential-Abfrage

Die bereits fuer die RFID-Zuordnung verwendete SYNCOS-Abfrage ist bestaetigt und wird nicht als frei konfigurierbare SQL-Anweisung behandelt:

```sql
SELECT t0.RFID,
       t0.ObjectKey,
       t0.Name,
       t0.Description,
       t0.PASSWORD,
       t0.IsEnabled,
       t0.IsVisible
  FROM syncos_stg_102.ITSDEV.ITSUSER t0
 WHERE t0.ClassID = 47
   AND t0.IsEnabled = -1
   AND t0.IsVisible = -1
   AND t0.OBJECTKEY LIKE '%446'
```

Die Backend-Implementierung verwendet dieselben bestaetigten Bedingungen, liest fuer die Passwortpruefung aber nur die benoetigte Spalte `PASSWORD` und setzt die Personalnummer als SQL-Parameter ein:

```sql
SELECT t0.PASSWORD
  FROM syncos_stg_102.ITSDEV.ITSUSER t0
 WHERE t0.ClassID = 47
   AND t0.IsEnabled = -1
   AND t0.IsVisible = -1
   AND t0.OBJECTKEY LIKE '%' + @PersonnelNo
```

Damit ist keine `PasswordLookupSql`-Laufzeitkonfiguration mehr vorgesehen.

Verbindliche Sicherheitsregeln:

- ausschliesslich `SELECT`;
- Personalnummer wird parametriert uebergeben;
- nur `ClassID = 47`;
- nur `IsEnabled = -1`;
- nur `IsVisible = -1`;
- kein Treffer -> Anmeldung abgelehnt;
- mehr als ein Treffer -> Anmeldung abgelehnt, da die Zuordnung nicht eindeutig ist;
- keine Schreiboperation an `ITSUSER`.

Nur der Datenbank-Connection-String bleibt Laufzeitkonfiguration:

```text
PersonnelAuthentication__ConnectionString
```

Zugangsdaten im Connection String sind Secrets und duerfen nicht in Git gespeichert werden.

## OBJECTKEY-Zuordnung

Im realen Referenzdatensatz wurde fuer Personalnummer `446` der Benutzer mit

```text
OBJECTKEY = 0000000446
NAME      = ANSA
PASSWORD  = 7ECF2EE5714D
```

beobachtet.

Die bereits vorhandene RFID-Abfrage verwendet die Personalnummer als `OBJECTKEY`-Suffix. Dieses Verhalten wird fuer die Passwortanmeldung uebernommen. Mehrdeutige Suffix-Treffer werden nicht toleriert.

## HTTP in der Testphase

Fuer den aktuellen STAGING-Betrieb darf die WebApp im internen Netz ueber HTTP aufgerufen und der Login getestet werden.

Technisch gilt:

- der Login-Endpunkt erzwingt aktuell in STAGING kein HTTPS;
- das Session-Cookie verwendet `CookieSecurePolicy.SameAsRequest`;
- bei HTTP funktioniert die Session ohne `Secure`-Flag;
- bei HTTPS wird das Cookie automatisch mit `Secure` ausgeliefert;
- die Bedienoberflaeche kennzeichnet eine erfolgreiche Anmeldung ueber HTTP als `HTTP-Testbetrieb`.

Wichtig fuer die Abgrenzung: Browser behandeln Service Worker und installierbare PWA-Funktionen als Secure-Context-Funktionen. Deshalb kann ueber eine normale HTTP-Adresse im LAN die WebApp und die Login-/Buchungslogik getestet werden, aber nicht zwingend der komplette installierte PWA-/Offline-Lebenszyklus. Fuer diesen Teil wird spaeter HTTPS benoetigt.

Fuer den Produktivbetrieb ist HTTP nicht freigegeben. Vor Produktivsetzung muss ein vertrauenswuerdiger HTTPS-Endpunkt aktiv sein. Dieser kann spaeter entweder ueber IIS oder direkt ueber Kestrel im Windows-Dienst bereitgestellt werden; die Hosting-Entscheidung ist noch offen.

## Session und Fehlverhalten

- Session-Idle-Timeout im aktuellen STAGING-Stand: 480 Minuten, konfigurierbar.
- Login-Endpunkt ist aktuell pro Client-IP auf 10 Versuche pro Minute begrenzt.
- Falsche Personalnummer und falsches Passwort liefern dieselbe Bedienermeldung.
- Ein fehlender oder nicht eindeutiger Credential-Datensatz fuehrt nicht zu einer Freigabe.
- Ist die SYNCOS-Datenbank nicht erreichbar oder der Connection String nicht konfiguriert, wird keine Anmeldung bestaetigt.
- Stimmt der angemeldete Mitarbeiter beim Buchungsaufruf nicht exakt mit `PersonnelNo` und `PersonnelName` des Vorgangs ueberein, wird vor jeder Materialbuchung mit `AUTH_CONFLICT` gestoppt.
- Nach App-/Server-Neustart kann eine erneute Anmeldung erforderlich sein. Das ist sicherer als eine lokal gespeicherte Passwort- oder Authentifizierungsumgehung.

## Sicherheitsbewertung

Die rekonstruierte SYNCOS-Transformation ist eine Legacy-Obfuskation und kein moderner Passwort-Hash. Die PWA verwendet sie nur, um einen bereits vorhandenen SYNCOS-Passwortwert kompatibel zu pruefen.

Daraus folgen verbindlich:

- Transformation nur serverseitig;
- kein Legacy-Schluessel im JavaScript;
- keine Ausgabe des gespeicherten Passwortwerts an das Frontend;
- keine Passwortprotokollierung;
- HTTP nur fuer den bewusst begrenzten STAGING-/Testbetrieb im internen Netz;
- HTTPS fuer den Produktivbetrieb;
- zeitkonstanter Vergleich der transformierten Bytes;
- keine Verwendung dieser Transformation fuer neue eigene Passwortspeicher der WebApp.

Wenn spaeter eine eigene WebApp-Benutzerverwaltung entsteht, muss sie moderne Passwort-Hashverfahren verwenden und darf diese Legacy-Logik nicht uebernehmen.
