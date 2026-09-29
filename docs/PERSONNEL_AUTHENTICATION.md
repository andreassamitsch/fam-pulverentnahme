# Mitarbeiter-Anmeldung: NFC und Passwort-Fallback

Stand: 04.09.2026

## Ziel

Vor einer produktiven Materialbuchung muss der handelnde Mitarbeiter eindeutig identifiziert und in einer serverseitigen Session gebunden sein.

Fuer die produktionsnahe Bedienung gilt ab diesem Stand:

1. **NFC ist der bevorzugte Loginweg.**
2. **Personalnummer + SYNCOS-Passwort ist der Fallback**, wenn NFC nicht zur Verfuegung steht.
3. Beide Wege erzeugen dieselbe serverseitige Personal-Session.
4. `/api/mix` akzeptiert einen neuen Buchungsaufruf nur, wenn diese Session exakt zu `PersonnelNo` und `PersonnelName` des Vorgangs passt.
5. Unmittelbar vor dem ersten schreibenden Oxaion-Aufruf bleibt die bereits bestaetigte exakte Oxaion-Personalpruefung zusaetzlich aktiv.

Damit ist die Session eine zusaetzliche Zugriffssicherung und kein Ersatz fuer die fachliche Oxaion-Revalidierung.

## NFC-Login

Ablauf:

1. Browser liest den NFC-Chip ueber Web NFC.
2. Reader-Trennzeichen werden entsprechend `docs/NFC_PERSONNEL_LOOKUP.md` entfernt; die RFID bleibt ein alphanumerischer String.
3. Backend sucht die RFID rein lesend in `syncos_stg_102.ITSDEV.ITSUSER` mit:
   - `ClassID = 47`
   - `IsEnabled = -1`
   - `IsVisible = -1`
4. `ObjectKey` liefert die zugeordnete Personalnummer.
5. Backend bestaetigt diese Personalnummer erneut ueber den dokumentierten exakten Oxaion-`IPENU`-Ablauf.
6. Nur bei eindeutiger Syncos-Zuordnung und erfolgreicher Oxaion-Bestaetigung wird die Backend-Session auf diese Person gesetzt.

Der NFC-Weg fragt kein zusaetzliches Passwort ab. Er ist eine bewusste neue Prozessentscheidung fuer den eingesetzten Personalchip als bevorzugtes Anmeldemedium.

Ein fehlgeschlagener NFC-Versuch darf keine Person raten oder eine Session fuer eine nicht eindeutig bestaetigte RFID setzen.

## Manueller Fallback: Personalnummer + Passwort

Wenn Web NFC nicht verfuegbar ist oder der Personalchip nicht gelesen werden kann:

1. Bediener gibt die Personalnummer ohne fuehrende Nullen ein.
2. Die vorhandene Oxaion-AJAX-Suche liefert ausschliesslich passende `PEPENU - PEPENA`-Treffer.
3. Bediener waehlt den Mitarbeiter bewusst aus.
4. Bediener gibt das vorhandene SYNCOS-Passwort ein.
5. Das Klartextpasswort wird nur fuer diesen Login-Request an das Backend uebertragen und danach im Browser verworfen.
6. Backend prueft Identitaet erneut in Oxaion und vergleicht den transformierten Passwortwert zeitkonstant mit dem vorhandenen SYNCOS-`PASSWORD`-Wert.
7. Bei Erfolg wird dieselbe Personal-Session gesetzt wie beim NFC-Login.

### Browser-Passwortspeicherung

Fuer die Produktions-PWA soll das vorhandene SYNCOS-Passwort nicht als speicherbares Browser-Passwort angeboten werden. Im aktuellen Android-/Chrome-orientierten Stand wird das Eingabefeld daher bewusst nicht als klassisches Browser-Passwortfeld ausgezeichnet:

- kein `type=password`;
- kein `autocomplete=current-password`;
- `autocomplete=off` und gaengige Passwortmanager-Ignore-Hinweise;
- visuelle Maskierung ueber `-webkit-text-security: disc`;
- initial `readonly`, Freigabe erst bei bewusstem Fokus;
- nach jedem erfolgreichen oder fehlgeschlagenen Login wird der Feldinhalt wieder geloescht.

Diese Massnahmen verhindern die Passwort-speichern-Abfrage im vorgesehenen Android-/Browser-Testaufbau soweit die Browser-Heuristik dies respektiert. Eine Website kann UI-Entscheidungen eines Browsers oder eines separat installierten Passwortmanagers nicht absolut erzwingen. Sollte der eingesetzte verwaltete Browser trotz dieser Kennzeichnung weiterhin eine Speicherung anbieten, muss dies zusaetzlich ueber Browser-/MDM-Policy unterbunden werden.

Diese UI-Haertung aendert nichts an der serverseitigen Passwortpruefung. Das Passwort darf weiterhin niemals in IndexedDB, Transaktionsdaten, Auditdateien oder Logs gespeichert werden.

## Bestaetigter SYNCOS-Credential-Lookup

Der Passwort-Fallback verwendet ausschliesslich den bereits bestaetigten rein lesenden Lookup:

```sql
SELECT t0.PASSWORD
  FROM syncos_stg_102.ITSDEV.ITSUSER t0
 WHERE t0.ClassID = 47
   AND t0.IsEnabled = -1
   AND t0.IsVisible = -1
   AND t0.OBJECTKEY LIKE '%' + @PersonnelNo
```

Die Abfrage ist parametriert. Mehrere aktive/sichtbare Treffer fuer dieselbe Personalnummer werden abgelehnt.

Die direkte SQL-Nutzung ist auf diese vorhandenen Syncos-Personal-/Credential-Lesewege begrenzt. Sie ist keine Freigabe fuer Oxaion-Buchungen oder sonstige ERP-Manipulationen per SQL.

## Nachgewiesene Legacy-Passworttransformation

Die bestehende Implementierung `SyncosLegacyPasswordCodec` reproduziert die am 03.09.2026 mit kontrollierten Testbenutzern nachgewiesene SYNCOS-Legacy-Transformation.

Aktuell freigegebener Bereich:

- ASCII-Ziffern
- ASCII-Gross-/Kleinbuchstaben
- maximal 18 Zeichen

Die Transformation bleibt ausschliesslich serverseitig. Sie ist kein moderner Passwort-Hash und wird nicht als neue Passwortspeicherung verwendet; sie dient nur dazu, den bereits vorhandenen SYNCOS-Wert zu pruefen.

Bestaetigte Regressionstestvektoren sind in `tests/Fam.Pulverentnahme.Web.Tests/PersonnelAuthenticationTests.cs` hinterlegt.

Sonderzeichen oder laengere Passwoerter bleiben bis zu kontrollierten Testvektoren offen.

## Session und Buchungsfreigabe

Die ASP.NET-Core-Session speichert nur die fuer die Zuordnung erforderliche Mitarbeiteridentitaet:

- Personalnummer
- vollstaendiger Oxaion-Name

Passwort, transformierter Passwortwert, RFID oder Connection String werden nicht als Authentifizierungsersatz in `IndexedDB` gespeichert.

Die aktuelle STAGING-Session hat einen Idle-Timeout von 480 Minuten.

Vor einer neuen Materialbuchung verlangt `PersonnelBookingAuthorizationFilter`:

- gueltige Session;
- Session-Personalnummer = `request.PersonnelNo`;
- Session-Name = `request.PersonnelName`.

Danach erfolgt innerhalb des Buchungsablaufs weiterhin die exakte Oxaion-Personalrevalidierung.

## HTTPS und STAGING

- Web NFC benoetigt auf den eingesetzten Browsern einen sicheren HTTPS-Kontext.
- Der manuelle Passwort-Fallback kann fuer den internen STAGING-Test gemaess der bereits getroffenen Testentscheidung auch ueber HTTP genutzt werden.
- Produktiv bleibt HTTPS fuer die gesamte PWA verbindlich.

## Secrets

Fuer STAGING werden SQL-Connection-Strings weiterhin nur serverseitig verwendet. Nach einmaliger verdeckter Eingabe duerfen sie lokal per Windows-DPAPI fuer denselben Windows-Benutzer und Rechner verschluesselt gespeichert und bei spaeteren Starts wiederverwendet werden.

Der Self-contained-Starter verwendet denselben eingegebenen Syncos-STAGING-Connection-String fuer:

- `Syncos__ConnectionString` - RFID-Zuordnung
- `PersonnelAuthentication__ConnectionString` - Passwort-Fallback

Der Connection String steht nicht im Frontend und nicht im Repository. Die lokale STAGING-Speicherung liegt unter `%LOCALAPPDATA%\FAM-Pulverentnahme\staging-sql-secrets.clixml`; mit `-ResetStoredSqlConnections` kann sie geloescht werden.
