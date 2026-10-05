# Entwicklungs-, Test- und Release-Workflow

Stand: 05.10.2026

## Ziel

Diese Richtlinie legt verbindlich fest, wie Aenderungen entwickelt, getestet, versioniert und in den stabilen Projektstand uebernommen werden.

Sie soll insbesondere verhindern, dass:

- ein installierbarer Stand nur auf einem Feature-Branch verbleibt;
- unterschiedliche Programmstaende unter derselben MSI-Version verteilt werden;
- ein PWA-Frontend trotz Serverupdate aus einem alten Service-Worker-Cache weiterlaeuft;
- ein nicht vollstaendig getesteter Stand stillschweigend als stabiler Hauptstand gilt.

## Branch-Regel

`main` ist der stabile Integrations- und Freigabestand des Projekts.

Verbindlich gilt:

1. Neue Entwicklung beginnt auf einem Feature-, Fix- oder Release-Branch.
2. Code, Tests und relevante Dokumentation werden gemeinsam aktualisiert.
3. Vor der Uebernahme nach `main` muessen Build und automatisierte Tests in GitHub Actions erfolgreich sein.
4. Aenderungen, die einen realen Oxaion-, APP-01-, Android-, Scanner-, PWA- oder Produktionsumgebungstest benoetigen, bleiben bis zu diesem Praxistest Release Candidate.
5. Sobald der erforderliche Praxistest erfolgreich bestaetigt ist, wird der freigegebene Stand zeitnah nach `main` uebernommen.
6. Ein fertig getesteter und freigegebener Stand darf nicht dauerhaft nur auf einem Feature-Branch liegen.
7. Neue Entwicklungszweige sollen anschliessend wieder vom aktuellen `main` ausgehen.

Damit ist `main` die verlaessliche Antwort auf die Frage: "Welcher Stand ist aktuell getestet und freigegeben?"

## Pull-Request- und Test-Gate

Die normale Uebernahme nach `main` erfolgt ueber einen Pull Request.

Vor dem Merge muessen mindestens erfolgreich sein:

- Restore
- Frontend-JavaScript-Syntaxpruefung
- PowerShell-Syntaxpruefung
- .NET-Build
- automatisierte Tests
- MSI-Erzeugung, wenn der Stand als Serverversion ausgeliefert werden soll

Ein roter CI-Lauf wird nicht nach `main` gemergt.

Ein gruenes CI allein ersetzt keinen erforderlichen realen APP-01-/Oxaion-/Android-Test. Wenn die Aenderung nur durch einen solchen Test fachlich bestaetigt werden kann, bleibt der PR bis zur Bestaetigung offen.

## Versionsregel fuer MSI

Jeder installierbare, inhaltlich geaenderte Stand erhaelt eine neue Versionsnummer.

Verbindlich:

- Dateiname: `FAM-Pulverentnahme-Setup-<Version>-x64.msi`
- Die Versionsnummer wird im WiX-Projekt einmal zentral als `ProductVersion` gepflegt.
- Der MSI-Dateiname und der GitHub-Actions-Artefaktname werden daraus abgeleitet.
- Eine bereits zum Test oder Einsatz bereitgestellte MSI-Version ist unveraenderlich.
- Wird nach Bereitstellung einer MSI noch Code, Frontend, Service Worker, Konfiguration, Installerlogik oder fachliches Verhalten geaendert, ist die naechste MSI-Version zu verwenden.
- Ein fehlgeschlagener CI-Lauf, der noch keine MSI erzeugt hat, erzwingt fuer reine Build-/Testkorrekturen keinen weiteren Versionssprung.
- Wurde eine MSI bereits erzeugt und extern getestet oder installiert, wird derselbe Versionsname fuer einen geaenderten Build nicht erneut verwendet.

Beispiel:

- `0.1.2` wurde installiert.
- Der Tank-Farbfix veraendert Frontend/PWA-Verhalten.
- Naechster installierbarer Stand ist `0.1.3`.
- Wenn `0.1.3` auf APP-01 getestet wurde und danach noch eine Codekorrektur notwendig wird, lautet der naechste installierbare Stand `0.1.4`.

## PWA- und Cache-Regel

Bei Aenderungen an ausgelieferten Frontend-Dateien muss geprueft werden, ob die installierte PWA eine alte Datei aus dem Service-Worker-Cache weiterverwenden koennte.

Wenn erforderlich:

1. Asset-URL beziehungsweise Versionsparameter anpassen.
2. Service-Worker-Cachekennung anheben.
3. zugehoerige Regressionstests aktualisieren.
4. sicherstellen, dass alte `fam-pulver-*`-Caches beim Aktivieren der neuen Version entfernt werden.

Eine neue MSI-Version ohne wirksame PWA-Cache-Aktualisierung gilt bei Frontend-Aenderungen nicht als vollstaendig.

## Dokumentationspflicht

Mit einer Release-relevanten Aenderung sind mindestens zu pruefen:

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`, wenn fachliche Entscheidungen betroffen sind
- das jeweilige spezialisierte Dokument
- `docs/OPEN_POINTS.md`
- `docs/SYSTEM_DOCUMENTATION.md`
- `docs/ADMIN_GUIDE.md`
- `docs/OPERATOR_GUIDE.md`, wenn Bedienung betroffen ist
- `docs/SERVICE_DEPLOYMENT.md`, wenn Installer, Dienst, IIS oder Releaseweg betroffen sind

Dauerhaft relevante Entscheidungen duerfen nicht nur im Chat verbleiben.

## Release-Ablauf in Kurzform

```text
aktuelles main
  -> Feature/Fix/Release-Branch
  -> Code + Tests + Dokumentation
  -> neue MSI-Version, falls installierbarer Stand geaendert
  -> Pull Request nach main
  -> CI gruen
  -> erforderlicher APP-01/Oxaion/Android-Praxistest
  -> Freigabe
  -> Merge nach main
  -> main ist neuer stabiler Referenzstand
```
