#!/usr/bin/env python3
"""Check checked-in icon formats and their live PWA references (no dependencies)."""
import json
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / 'src' / 'Fam.Pulverentnahme.Web' / 'wwwroot'
ICONS = WEB / 'icons'


def png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()
    if data[:8] != b'\x89PNG\r\n\x1a\n' or data[12:16] != b'IHDR':
        raise AssertionError(f'Invalid PNG: {path}')
    return struct.unpack('>II', data[16:24])


def main() -> None:
    master = ET.parse(ICONS / 'fam-pulver-master.svg').getroot()
    assert master.attrib.get('viewBox') == '0 0 1254 1254'
    assert (ICONS / 'favicon.svg').read_bytes() == (ICONS / 'fam-pulver-master.svg').read_bytes()
    for name, size in (
        ('icon-192.png', 192),
        ('icon-512.png', 512),
        ('icon-maskable-512.png', 512),
        ('favicon-32.png', 32),
        ('apple-touch-icon.png', 180),
    ):
        assert png_size(ICONS / name) == (size, size), name
    assert png_size(WEB / 'apple-touch-icon.png') == (180, 180)
    manifest = json.loads((WEB / 'manifest.webmanifest').read_text(encoding='utf-8'))
    assert {(icon['src'], icon['sizes'], icon['type'], icon['purpose']) for icon in manifest['icons']} == {
        ('/icons/icon-192.png', '192x192', 'image/png', 'any'),
        ('/icons/icon-512.png', '512x512', 'image/png', 'any'),
        ('/icons/icon-maskable-512.png', '512x512', 'image/png', 'maskable'),
    }
    html = (WEB / 'index.html').read_text(encoding='utf-8')
    sw = (WEB / 'sw.js').read_text(encoding='utf-8')
    for asset in ('/icons/favicon.svg', '/icons/favicon-32.png', '/apple-touch-icon.png'):
        assert asset in html and asset in sw, asset
    for icon in manifest['icons']:
        assert icon['src'] in sw and (WEB / icon['src'].lstrip('/')).is_file(), icon['src']
    print('PASS: SVG master, PNG dimensions, favicon, Apple icon, manifest, HTML and offline cache')


if __name__ == '__main__':
    main()
