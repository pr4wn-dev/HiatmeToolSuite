"""Generate 64px PNG alert icons from Material Design Icons (Apache 2.0)."""
from __future__ import annotations

import re
from pathlib import Path

import fitz
import requests
from PIL import Image, ImageDraw

BASE = "https://raw.githubusercontent.com/Templarian/MaterialDesign-SVG/master/svg"
OUT = Path(__file__).resolve().parents[1] / "Hiatme Tool Suite v3" / "Resources" / "trip-alerts"
SIZE = 128
FILL = "#FFFFFF"

ICONS = {
    "date": "calendar",
    "hidden": "eye-off",
    "cancelled": "close-circle",
    "dupe": "file-document-multiple",
    "time": "clock",
    "address": "map-marker",
    "mwc": "wheelchair-accessibility",
    "wc-not-in-reserves": "wheelchair-accessibility",
    "child": "baby-face-outline",
    "escort": "account-group",
    "lbs": "scale-balance",
    "service-dog": "dog",
    "scooter": "scooter",
    "mass-transit": "bus",
    "rerouted": "arrow-u-left-top-bold",
}


def fetch_svg(name: str) -> str:
    url = f"{BASE}/{name}.svg"
    r = requests.get(url, timeout=30)
    r.raise_for_status()
    text = r.text
    text = re.sub(r'fill="[^"]*"', f'fill="{FILL}"', text)
    if "<path" in text and 'fill="' not in text:
        text = text.replace("<path ", f'<path fill="{FILL}" ')
    return text


def svg_to_png(svg_text: str) -> Image.Image:
    doc = fitz.open(stream=svg_text.encode("utf-8"), filetype="svg")
    page = doc[0]
    scale = SIZE / max(page.rect.width, page.rect.height)
    matrix = fitz.Matrix(scale, scale)
    pix = page.get_pixmap(matrix=matrix, alpha=True)
    doc.close()
    return Image.open(__import__("io").BytesIO(pix.tobytes("png"))).convert("RGBA")


def add_badge_x(img: Image.Image) -> Image.Image:
    out = img.copy()
    draw = ImageDraw.Draw(out)
    r = 14
    cx, cy = SIZE - r // 2 - 2, r // 2 + 2
    draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(220, 53, 69, 255))
    pad = 5
    draw.line((cx - pad, cy - pad, cx + pad, cy + pad), fill=(255, 255, 255, 255), width=3)
    draw.line((cx + pad, cy - pad, cx - pad, cy + pad), fill=(255, 255, 255, 255), width=3)
    return out


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for file_name, mdi_name in ICONS.items():
        svg = fetch_svg(mdi_name)
        img = svg_to_png(svg)
        if file_name == "wc-not-in-reserves":
            img = add_badge_x(img)
        path = OUT / f"{file_name}.png"
        img.save(path, "PNG")
        print(f"wrote {path.name}")


if __name__ == "__main__":
    main()
