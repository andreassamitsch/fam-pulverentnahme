# FAM Pulverentnahme: Logo und App-Icons

## Verbindliche Gestaltung (21.09.2026)

Das freigegebene Standardlogo zeigt einen leicht gerundeten, oben offenen weissen Pulverbehaelter mit dem unverzerrten FAM-Firmenzeichen an der Front. Weisse Pulverpartikel steigen **oben** aus der Behaelteroeffnung auf; unter dem Behaelter sind keine Partikel. Der Hintergrund ist einheitlich blau-tuerkis (`#007fa9`).

## Quelle und abgeleitete Formate

Die hochaufloesende, verlustfrei skalierbare Vektor-Masterdatei ist `src/Fam.Pulverentnahme.Web/wwwroot/icons/fam-pulver-master.svg` (ViewBox 1254 x 1254). Sie ist die einzige Vorlage fuer alle App-Icons. `icons/favicon.svg` ist eine identische Kopie und wird in `index.html` eingebunden; bestehende SVG-Icon-URLs bleiben zur Kompatibilitaet auf demselben Logo. Die PNGs werden aus dem Master generiert und liegen unter `wwwroot/icons/`:

- `icon-192.png`: PWA / Android 192 x 192
- `icon-512.png`: PWA / Android 512 x 512
- `icon-maskable-512.png`: maskierbares Android-Icon 512 x 512; das Motiv ist mit Sicherheitsrand verkleinert, damit adaptive Masken keinen wichtigen Inhalt abschneiden.
- `favicon-32.png`: Browser-Fallback 32 x 32; `favicon.svg`: verlustfrei skalierbares Favicon.
- `apple-touch-icon.png`: iOS-Home-Screen-Icon 180 x 180; wird auch unter `wwwroot/apple-touch-icon.png` fuer Browser-Autodiscovery gespiegelt.

Das PWA-Manifest referenziert die passenden PNG-Icons und markiert die maskierbare Variante mit `purpose: maskable`. `index.html` verlinkt die Favicons und das Apple-Touch-Icon. Der Service Worker cached die aktiven Icons und verwendet bei Logo-Aenderungen eine neue Cache-Version. Die Icon-Umstellung aendert weder Login noch fachliche Buchungs- und Offline-Logik.

## Regeneration und Pruefung

Im Repository-Stammverzeichnis:

```bash
python -m pip install cairosvg pillow
python scripts/generate-brand-icons.py
python scripts/validate-brand-icons.py
```

Fuer einen neuen Logo-Stand den Master anpassen, alle abgeleiteten Dateien neu generieren und den Service-Worker-Cachenamen anheben. Bereits installierte PWAs koennen bis zu einem sicheren App-Update beziehungsweise einer Neuinstallation noch ein zwischengespeichertes altes Icon zeigen. Produktive PWA-Installation auf Smartphones erfordert einen vertrauenswuerdigen HTTPS-Endpunkt; die bestehenden sicheren Update-/Outbox-Regeln aus `docs/OFFLINE_PWA.md` bleiben unveraendert.
