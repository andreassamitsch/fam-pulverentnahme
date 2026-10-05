# Audit fuer abgelehnte Chargenscans

Stand: 04.09.2026

## Zweck

Ein physischer Fehlscan im gefuehrten Nachfuellprozess soll nachvollziehbar sein, ohne ihn mit einer Oxaion-Buchung oder einem Oxaion-Status `REJECTED` zu verwechseln.

Ein abgelehnter Chargenscan findet **vor** dem Aufbau einer gueltigen Nachfuellquelle und vor jeder schreibenden Oxaion-Materialbuchung statt. Er erzeugt daher keine Materialbewegung, keine Oxaion-Belegposition und keinen automatischen Retry.

## Protokollierte Gruende

Der aktuelle STAGING-Stand kennt fuer diesen Audit genau folgende strukturierte Gruende:

- `WRONG_ARTICLE`: Artikel aus dem Chargen-QR stimmt nicht mit dem aus dem Maschinentank abgeleiteten Artikel ueberein.
- `CHARGE_NOT_FOUND`: Artikel stimmt, fuer die Charge wurde aber keine zulaessige positive Oxaion-Bestandsposition gefunden.
- `SOURCE_ALREADY_USED`: Fuer die erneut gescannte Charge wurden zwar Oxaion-Bestandspositionen gefunden, aber alle sind in diesem Vorgang bereits als exakte Quelle gewaehlt.
- `INVALID_QR_FORMAT`: Der QR-Code entspricht nicht dem erwarteten Format `Artikel+++Charge`.

Ein technischer Oxaion-/HTTP-/Backend-Ausfall bei der Bestandsabfrage ist **kein** Bediener-Fehlscan und wird nicht unter einem dieser Gruende als solcher protokolliert.

## Gespeicherte Daten

Das Backend uebernimmt den Mitarbeiter aus der bereits authentifizierten Personal-Session. Der Browser darf Personalnummer oder Name fuer diesen Audit nicht frei vorgeben.

Pro Ereignis werden gespeichert:

- eindeutige `eventId`
- UTC-Zeitpunkt
- Ablehnungsgrund
- Personalnummer
- von der Session bestaetigter Mitarbeitername
- gescannter Maschinentank/Oxaion-Lagerort
- erwarteter Tankartikel
- gescannter Artikel, soweit aus einem gueltig strukturierten QR ableitbar
- gescannte Charge, soweit aus einem gueltig strukturierten QR ableitbar

Bewusst **nicht** gespeichert werden:

- Passwort
- RFID/NFC-UID
- Syncos- oder Oxaion-Zugangsdaten
- Connection Strings
- beliebiger ungepruefter roher QR-Inhalt

Bei `INVALID_QR_FORMAT` wird deshalb nur der strukturierte Ablehnungsgrund samt vorhandenem Prozesskontext gespeichert, nicht der beliebige Rohinhalt des Codes.

## STAGING-Persistenz

Der aktuelle STAGING-Prototyp speichert jedes Ereignis als eigene JSON-Datei unter:

```text
App_Data/scan-events
```

Diese Dateien sind WebApp-eigene Auditdaten und keine Oxaion-Daten. Die produktive Persistenztechnik, Aufbewahrungsdauer, Zugriffsrechte und Auswertung sind noch offen und werden in `docs/OPEN_POINTS.md` gefuehrt.

## API

Der aktuelle Endpunkt lautet:

```text
POST /api/scan-events/rejected-charge
```

Er akzeptiert nur eine bereits bestehende Personal-Session. Ohne authentifizierten Mitarbeiter wird der Audit-Request mit `401 AUTH_REQUIRED` abgewiesen.

Die erlaubten Eingabefelder sind auf kurze strukturierte Werte begrenzt und Steuerzeichen werden abgelehnt. Unbekannte Ablehnungsgruende werden nicht gespeichert.

## Bedienerverhalten

Bei einem fachlich abgelehnten Chargenscan:

1. Es wird **keine** Nachfuellkarte angelegt.
2. Die PWA zeigt eine seitendeckende, blockierende Meldung.
3. Sollartikel/Sollfarbe und der erkannte Scan werden soweit moeglich gegenuebergestellt.
4. Bei falschem oder nicht verfuegbarem Pulver wird klar angezeigt, dass die Charge nicht eingefuellt werden darf.
5. Das Audit wird serverseitig angestossen.
6. Der Mitarbeiter muss die Meldung mit `Verstanden` bestaetigen.
7. Danach kann eine andere beziehungsweise korrekte Charge gescannt werden.

Schlaegt ausschliesslich die Audit-Persistenz fehl, bleibt die falsche Charge trotzdem sicher abgelehnt. Die PWA weist darauf hin, dass die Protokollierung fehlgeschlagen ist und die Produktionsleitung informiert werden soll. Ein Auditfehler darf niemals dazu fuehren, dass ein fachlich falscher Scan freigegeben wird.

## Abgrenzung zu Buchungsfehlern

Dieser Audit ist strikt getrennt von den Transaktionszustaenden aus `docs/ERROR_HANDLING.md`:

- kein `clientOperationId`-Retry entsteht aus einem Fehlscan;
- kein Oxaion-`REJECTED` wird vorgetaeuscht;
- kein `UNCERTAIN` entsteht, weil noch keine Materialbuchung begonnen wurde;
- die normale serverseitige Revalidierung jeder spaeter gueltig ausgewaehlten Quelle bleibt unveraendert bestehen.
