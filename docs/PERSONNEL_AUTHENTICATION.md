# Mitarbeiter-Anmeldung mit SYNCOS-Passwort

## Status

Am 03.09.2026 wurde fuer die Pulverentnahme-PWA verbindlich entschieden:

- Die Auswahl eines Mitarbeiters ueber die Oxaion-Personalnummer allein reicht nicht mehr fuer eine produktive Buchung.
- Nach der bewussten Auswahl des Oxaion-Mitarbeiters muss der Bediener sein Passwort eingeben.
- Die Passwortpruefung erfolgt ausschliesslich im ASP.NET-Core-Backend.
- Das Frontend kennt weder den Legacy-Schluessel noch den gespeicherten `PASSWORD`-Wert.
- Passwort, transformierter Passwortwert und Datenbank-Credential duerfen weder in `IndexedDB`, im `clientOperationId`-Vorgang, im Transaktionslog noch in Git gespeichert werden.
- Eine neue Anmeldung ist ein Online-Schritt. Bei abgelaufener Session gibt es keinen Offline-Bypass; der Bediener muss sich nach Wiederherstellung der Backend-Verbindung erneut anmelden.

Die bestehende Oxaion-Personalpruefung ueber `PEPENU` und `PEPENA` bleibt unveraendert bestehen. Oxaion bleibt fuer die Identitaet des Mitarbeiters fuehrend; die separate Credential-Datenbank wird nur zur Passwortpruefung gelesen.

## Ablauf

1. Bediener gibt die Personalnummer ein.
2. Backend sucht den Mitarbeiter wie bisher ueber die bestaetigte Oxaion-Personallogik.
3. Bediener waehlt bewusst den Treffer `PEPENU - PEPENA`.
4. PWA zeigt das Passwortfeld.
5. `POST /api/personnel/login` sendet Personalnummer und Passwort ueber HTTPS an das Backend.
6. Backend liest den Mitarbeiter erneut exakt aus Oxaion.
7. Backend bildet aus der normalisierten Personalnummer den aktuell verwendeten Credential-Schluessel zehnstellig mit fuehrenden Nullen, z. B. `446 -> 0000000446`.
8. Backend liest den gespeicherten `PASSWORD`-Wert ueber eine parametrisierte, ausschliesslich lesende SQL-Abfrage mit `@ObjectKey`.
9. Backend transformiert das eingegebene Passwort mit der am 03.09.2026 rekonstruierten SYNCOS-Legacy-Logik und vergleicht die Bytes zeitkonstant mit dem gespeicherten Wert.
10. Bei Erfolg wird eine serverseitige Session angelegt. Im Browser liegt nur die HTTP-Session-Cookie-Referenz; das Passwort wird verworfen.
11. `POST /api/mix` akzeptiert neue Buchungen nur, wenn die Session vorhanden ist und Personalnummer sowie Name exakt mit dem Buchungsvorgang uebereinstimmen.
12. Direkt vor der Materialbuchung bleibt zusaetzlich die bereits bestehende erneute Oxaion-Pruefung von `PEPENU` und `PEPENA` aktiv.

Damit bestehen zwei voneinander getrennte Sicherheitspruefungen:

- Anmeldung: Personalnummer + Passwort
- fachliche Buchungspruefung: Mitarbeiter unmittelbar vor dem ersten schreibenden Oxaion-Aufruf erneut eindeutig in Oxaion bestaetigen

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

## Credential-Datenbank

Die PWA darf keine Datenbankverbindung besitzen. Der Zugriff erfolgt ausschliesslich im Backend und ausschliesslich lesend.

Der konkrete SYNCOS-Tabellen-/Schemaname ist im Repository noch nicht bestaetigt und wird deshalb nicht hart codiert oder erfunden. Die Implementierung erwartet zur Laufzeit:

```text
PersonnelAuthentication__ConnectionString
PersonnelAuthentication__PasswordLookupSql
```

Die SQL-Abfrage muss:

- mit `SELECT` beginnen;
- den Parameter `@ObjectKey` verwenden;
- genau den gespeicherten `PASSWORD`-Wert als erste Spalte liefern;
- fuer einen Benutzer hoechstens eine Zeile liefern;
- produktiv nur aktive Benutzer zulassen, sobald Tabelle und Aktivkennzeichen technisch bestaetigt sind.

Beispiel als Schablone, **nicht** als bestaetigter Tabellenname:

```sql
SELECT PASSWORD
FROM <BESTAETIGTES_SCHEMA>.<BESTAETIGTE_TABELLE>
WHERE OBJECTKEY = @ObjectKey
  AND ISENABLED = -1
```

Die Connection-String-Zugangsdaten sind Secrets und duerfen nicht in `appsettings.json` im Repository eingetragen werden. Sie werden ueber die sichere IIS-/Laufzeitkonfiguration bereitgestellt.

## OBJECTKEY-Zuordnung

Im realen Referenzdatensatz wurde fuer Personalnummer `446` der Benutzer mit

```text
OBJECTKEY = 0000000446
NAME      = ANSA
PASSWORD  = 7ECF2EE5714D
```

beobachtet. Die aktuelle STAGING-Implementierung bildet deshalb die Oxaion-Personalnummer wie folgt ab:

```text
PEPENU 446 -> OBJECTKEY 0000000446
```

Vor Produktivfreigabe ist diese Zuordnung noch an mehreren realen Mitarbeitern zu bestaetigen. Ein Tabellenname oder eine alternative Benutzerzuordnung wird nicht angenommen.

## Session und Fehlverhalten

- Session-Idle-Timeout im aktuellen STAGING-Stand: 480 Minuten, konfigurierbar.
- Login-Endpunkt ist aktuell pro Client-IP auf 10 Versuche pro Minute begrenzt.
- Falsche Personalnummer und falsches Passwort liefern dieselbe Bedienermeldung.
- Ein fehlender oder nicht eindeutiger Credential-Datensatz fuehrt nicht zu einer Freigabe.
- Ist die Credential-Datenbank nicht erreichbar oder nicht konfiguriert, wird keine Anmeldung bestaetigt.
- Stimmt der angemeldete Mitarbeiter beim Buchungsaufruf nicht exakt mit `PersonnelNo` und `PersonnelName` des Vorgangs ueberein, wird vor jeder Materialbuchung mit `AUTH_CONFLICT` gestoppt.
- Nach App-/Server-Neustart kann eine erneute Anmeldung erforderlich sein. Das ist sicherer als eine lokal gespeicherte Passwort- oder Authentifizierungsumgehung.

## Sicherheitsbewertung

Die rekonstruierte SYNCOS-Transformation ist eine Legacy-Obfuskation und kein moderner Passwort-Hash. Die PWA verwendet sie nur, um einen bereits vorhandenen SYNCOS-Passwortwert kompatibel zu pruefen.

Daraus folgen verbindlich:

- Transformation nur serverseitig;
- kein Legacy-Schluessel im JavaScript;
- keine Ausgabe des gespeicherten Passwortwerts an das Frontend;
- keine Passwortprotokollierung;
- HTTPS fuer die PWA und den Login;
- zeitkonstanter Vergleich der transformierten Bytes;
- keine Verwendung dieser Transformation fuer neue eigene Passwortspeicher der WebApp.

Wenn spaeter eine eigene WebApp-Benutzerverwaltung entsteht, muss sie moderne Passwort-Hashverfahren verwenden und darf diese Legacy-Logik nicht uebernehmen.
