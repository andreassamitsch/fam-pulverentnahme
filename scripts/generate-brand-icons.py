#!/usr/bin/env python3
"""Regenerate PWA/browser icons from the approved vector master.

Install once: python -m pip install cairosvg pillow
Run from the repository root: python scripts/generate-brand-icons.py
"""
from io import BytesIO
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

import cairosvg
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ICONS = ROOT / 'src' / 'Fam.Pulverentnahme.Web' / 'wwwroot' / 'icons'
MASTER = ICONS / 'fam-pulver-master.svg'
NS = '{http://www.w3.org/2000/svg}'


def render(size: int, *, maskable: bool = False) -> Image.Image:
    svg = MASTER.read_bytes()
    if maskable:
        ET.register_namespace('', 'http://www.w3.org/2000/svg')
        root = ET.fromstring(svg)
        image_path = root.find(f'{NS}path')
        if image_path is None:
            raise ValueError('Master SVG does not contain the icon path')
        root.remove(image_path)
        group = ET.SubElement(root, f'{NS}g', {'transform': 'translate(87.78 87.78) scale(0.86)'})
        group.append(image_path)
        svg = ET.tostring(root, encoding='utf-8')
    png = cairosvg.svg2png(bytestring=svg, output_width=size, output_height=size)
    return Image.open(BytesIO(png)).convert('RGB')


def main() -> None:
    for name, size, maskable in (
        ('icon-192.png', 192, False),
        ('icon-512.png', 512, False),
        ('icon-maskable-512.png', 512, True),
        ('favicon-32.png', 32, False),
        ('apple-touch-icon.png', 180, False),
    ):
        image = render(size, maskable=maskable)
        # Indexed PNG keeps the committed files small, retaining smooth white edges.
        image = image.quantize(colors=16, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
        if maskable:
            image = image.convert('RGB').quantize(
                colors=4, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE
            )
        target = ICONS / name
        image.save(target, optimize=True, compress_level=9)
        if name == 'apple-touch-icon.png':
            shutil.copyfile(target, ICONS.parent / name)
        print(f'{target.relative_to(ROOT)}: {size}x{size}')


if __name__ == '__main__':
    main()
