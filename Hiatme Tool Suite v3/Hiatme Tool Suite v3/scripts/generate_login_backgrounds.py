#!/usr/bin/env python3
"""Generate login background PNGs for every Supey theme (4 classics + 240 level themes)."""

from __future__ import annotations

import math
import os
import random
import struct
import zlib
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:
    raise SystemExit("Install Pillow: pip install pillow")

W, H = 1600, 900
THEMES_PER_LEVEL = 8
MAX_LEVEL = 30

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Resources" / "login_backgrounds"


def hsl_to_rgb(h: float, s: float, l: float) -> tuple[int, int, int]:
    h = h % 360.0
    s = max(0.0, min(1.0, s))
    l = max(0.0, min(1.0, l))
    c = (1 - abs(2 * l - 1)) * s
    x = c * (1 - abs((h / 60) % 2 - 1))
    m = l - c / 2
    if h < 60:
        r, g, b = c, x, 0
    elif h < 120:
        r, g, b = x, c, 0
    elif h < 180:
        r, g, b = 0, c, x
    elif h < 240:
        r, g, b = 0, x, c
    elif h < 300:
        r, g, b = x, 0, c
    else:
        r, g, b = c, 0, x
    return (
        int((r + m) * 255),
        int((g + m) * 255),
        int((b + m) * 255),
    )


def blend(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    t = max(0.0, min(1.0, t))
    u = 1.0 - t
    return (
        int(a[0] * u + b[0] * t),
        int(a[1] * u + b[1] * t),
        int(a[2] * u + b[2] * t),
    )


def theme_hues(level: int, index: int) -> tuple[float, float, float, float]:
    chaos = min(1.0, (level - 1) / max(1, MAX_LEVEL - 1)) if level > 0 else 0.0
    hue = (level * 41.7 + index * 53.3) % 360.0
    accent_hue = hue
    alt_hue = (hue + 140 + index * 11 + level * 4) % 360.0
    return hue, accent_hue, alt_hue, chaos


def radial_gradient(size: tuple[int, int], c1, c2, c3=None, center=(0.5, 0.45)) -> Image.Image:
    w, h = size
    img = Image.new("RGB", size)
    px = img.load()
    cx, cy = center[0] * w, center[1] * h
    max_d = math.hypot(max(cx, w - cx), max(cy, h - cy))
    for y in range(h):
        for x in range(w):
            d = math.hypot(x - cx, y - cy) / max_d
            if c3 is None:
                px[x, y] = blend(c1, c2, d)
            elif d < 0.55:
                px[x, y] = blend(c1, c2, d / 0.55)
            else:
                px[x, y] = blend(c2, c3, (d - 0.55) / 0.45)
    return img


def add_bokeh(img: Image.Image, rng: random.Random, hue: float, chaos: float, count: int) -> None:
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    for _ in range(count):
        x = rng.randint(0, img.width)
        y = rng.randint(0, img.height)
        r = rng.randint(40, 180 + int(chaos * 120))
        sat = 0.35 + chaos * 0.4
        lit = 0.35 + rng.random() * 0.25
        col = hsl_to_rgb(hue + rng.uniform(-25, 25), sat, lit)
        alpha = int(35 + chaos * 55 + rng.random() * 40)
        draw.ellipse((x - r, y - r, x + r, y + r), fill=(*col, alpha))
    img.paste(Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB"))


def add_city_silhouette(img: Image.Image, rng: random.Random, base_rgb, accent_hue: float, chaos: float) -> None:
    draw = ImageDraw.Draw(img)
    ground = int(H * 0.62)
    x = 0
    while x < W:
        bw = rng.randint(28, 90 + int(chaos * 40))
        bh = rng.randint(60, int(H * 0.45) + int(chaos * 80))
        tone = blend(base_rgb, (0, 0, 0), 0.35 + rng.random() * 0.25)
        draw.rectangle((x, ground - bh, x + bw, ground + 4), fill=tone)
        if chaos > 0.35:
            for wy in range(ground - bh + 12, ground - 8, 18):
                for wx in range(x + 8, x + bw - 8, 14):
                    if rng.random() < 0.25 + chaos * 0.35:
                        win = hsl_to_rgb(accent_hue + rng.uniform(-15, 15), 0.7, 0.55 + rng.random() * 0.2)
                        draw.rectangle((wx, wy, wx + 6, wy + 10), fill=win)
        x += bw + rng.randint(2, 12)


def add_neon_grid(img: Image.Image, hue: float, chaos: float) -> None:
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    accent = hsl_to_rgb(hue, 0.75, 0.55)
    alpha = int(40 + chaos * 80)
    spacing = max(28, 80 - int(chaos * 35))
    for x in range(0, W, spacing):
        draw.line((x, int(H * 0.35), x, H), fill=(*accent, alpha), width=2)
    for y in range(int(H * 0.35), H, spacing):
        draw.line((0, y, W, y), fill=(*accent, alpha), width=2)
    img.paste(Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB"))


def add_laser_beams(img: Image.Image, rng: random.Random, hue: float, alt_hue: float, chaos: float) -> None:
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    for i in range(4 + int(chaos * 10)):
        h = hue if i % 2 == 0 else alt_hue
        col = hsl_to_rgb(h + rng.uniform(-20, 20), 0.85, 0.58)
        x1 = rng.randint(-100, W)
        y1 = rng.randint(0, int(H * 0.4))
        x2 = x1 + rng.randint(200, 700)
        y2 = H + 50
        draw.line((x1, y1, x2, y2), fill=(*col, int(50 + chaos * 90)), width=rng.randint(2, 5))
    img.paste(Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB"))


def add_stars(img: Image.Image, rng: random.Random, count: int) -> None:
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    for _ in range(count):
        x, y = rng.randint(0, W), rng.randint(0, int(H * 0.75))
        s = rng.randint(1, 3)
        a = rng.randint(120, 255)
        draw.ellipse((x, y, x + s, y + s), fill=(255, 255, 255, a))
    img.paste(Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB"))


def add_soft_petals(img: Image.Image, rng: random.Random, hue: float) -> None:
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    for _ in range(28):
        x = rng.randint(0, W)
        y = rng.randint(0, H)
        r = rng.randint(30, 110)
        col = hsl_to_rgb(hue + rng.uniform(-18, 18), 0.45, 0.72 + rng.random() * 0.15)
        draw.ellipse((x - r, y - r, x + r, y + r), fill=(*col, int(30 + rng.random() * 45)))
    img.paste(Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB"))


def add_hero_silhouette(img: Image.Image, kind: str, accent: tuple[int, int, int]) -> None:
    """Abstract cape/hero shapes — inspired vibes, not branded characters."""
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    cx, base = int(W * 0.78), int(H * 0.88)
    body = (20, 20, 28)
    if kind == "hero":
        # Cape + skyline glow
        draw.polygon(
            [(cx, int(H * 0.18)), (cx - 120, base), (cx + 120, base)],
            fill=(*accent, 180),
        )
        draw.ellipse((cx - 35, int(H * 0.22), cx + 35, int(H * 0.42)), fill=(*body, 220))
        draw.rectangle((cx - 22, int(H * 0.38), cx + 22, int(H * 0.72)), fill=(*body, 220))
    elif kind == "night":
        # Moon + tower blocks
        draw.ellipse((cx - 70, int(H * 0.08), cx + 70, int(H * 0.22)), fill=(240, 240, 255, 90))
        draw.rectangle((cx - 18, int(H * 0.28), cx + 18, int(H * 0.78)), fill=(*body, 200))
        draw.polygon([(cx - 90, int(H * 0.55)), (cx, int(H * 0.12)), (cx + 90, int(H * 0.55))], fill=(*accent, 120))
    elif kind == "warm":
        draw.polygon(
            [(cx - 80, base), (cx - 40, int(H * 0.35)), (cx + 60, int(H * 0.25)), (cx + 100, base)],
            fill=(*accent, 150),
        )
    elif kind == "aurora":
        for i in range(5):
            y = int(H * (0.15 + i * 0.08))
            draw.arc((cx - 200, y, cx + 200, y + 180), 200, 340, fill=(*accent, 80 - i * 10), width=8)
    img.paste(Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB"))


def generate_level(level: int, index: int) -> Image.Image:
    hue, accent_hue, alt_hue, chaos = theme_hues(level, index)
    rng = random.Random(level * 1000 + index * 137)

    surface_sat = 0.12 + chaos * 0.35
    surface_lit = 0.05 + (index % 3) * 0.012
    accent_sat = 0.42 + chaos * 0.52
    accent_lit = 0.42 + (1 - chaos) * 0.12 - (index % 2) * 0.04

    c1 = hsl_to_rgb(hue, surface_sat, surface_lit)
    c2 = hsl_to_rgb(hue, surface_sat + 0.08, surface_lit + 0.12)
    c3 = hsl_to_rgb(accent_hue, accent_sat * 0.5, accent_lit * 0.35)
    img = radial_gradient((W, H), c1, c2, c3, center=(0.42 + (index % 5) * 0.04, 0.38))

    # Softer pastel / botanical lane for even indices at low chaos
    if chaos < 0.45 and index % 2 == 0:
        add_soft_petals(img, rng, hue + 30)

    add_bokeh(img, rng, accent_hue, chaos, count=8 + int(chaos * 14))

    if chaos >= 0.15:
        add_city_silhouette(img, rng, c2, accent_hue, chaos)

    if chaos >= 0.35:
        add_neon_grid(img, accent_hue, chaos)

    if chaos >= 0.55:
        add_laser_beams(img, rng, accent_hue, alt_hue, chaos)
        add_stars(img, rng, 40 + int(chaos * 120))

    if chaos >= 0.75:
        overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
        draw = ImageDraw.Draw(overlay)
        for _ in range(12 + index):
            gx = rng.randint(0, W - 80)
            gy = rng.randint(0, H - 40)
            gw, gh = rng.randint(30, 140), rng.randint(8, 40)
            col = hsl_to_rgb((hue + rng.uniform(0, 360)) % 360, 0.9, 0.55)
            draw.rectangle((gx, gy, gx + gw, gy + gh), fill=(*col, rng.randint(40, 100)))
        img.paste(Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB"))

    if chaos > 0.5:
        img = img.filter(ImageFilter.GaussianBlur(radius=0.6))

    return img


def generate_classic(key: str) -> Image.Image:
    if key == "classic-black-lime":
        img = radial_gradient(
            (W, H),
            (12, 14, 10),
            (24, 28, 18),
            (40, 55, 22),
            center=(0.35, 0.5),
        )
        add_neon_grid(img, 95, 0.55)
        add_hero_silhouette(img, "hero", hsl_to_rgb(95, 0.65, 0.48))
        add_city_silhouette(img, random.Random(1), (18, 20, 14), 95, 0.5)
    elif key == "classic-midnight":
        img = radial_gradient((W, H), (8, 10, 22), (18, 24, 42), (30, 40, 68))
        add_stars(img, random.Random(2), 180)
        add_hero_silhouette(img, "night", hsl_to_rgb(220, 0.55, 0.55))
        add_city_silhouette(img, random.Random(3), (10, 12, 24), 220, 0.45)
    elif key == "classic-graphite":
        img = radial_gradient((W, H), (18, 18, 20), (32, 30, 28), (48, 38, 24))
        add_bokeh(img, random.Random(4), 35, 0.35, 16)
        add_hero_silhouette(img, "warm", hsl_to_rgb(35, 0.7, 0.5))
    elif key == "classic-slate":
        img = radial_gradient((W, H), (14, 20, 22), (24, 36, 40), (36, 58, 62))
        add_soft_petals(img, random.Random(5), 175)
        add_hero_silhouette(img, "aurora", hsl_to_rgb(170, 0.55, 0.55))
    else:
        img = radial_gradient((W, H), (20, 20, 24), (40, 40, 48), (60, 60, 72))
    return img


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    written = 0

    for key in ("classic-black-lime", "classic-midnight", "classic-graphite", "classic-slate"):
        path = OUT / f"{key}.png"
        generate_classic(key).save(path, "PNG", optimize=True)
        written += 1
        print(f"  {path.name}")

    for level in range(1, MAX_LEVEL + 1):
        for index in range(THEMES_PER_LEVEL):
            name = f"L{level:02d}-{index:02d}.png"
            path = OUT / name
            generate_level(level, index).save(path, "PNG", optimize=True)
            written += 1
        print(f"  level {level:02d} ({THEMES_PER_LEVEL} images)")

    print(f"\nDone — {written} backgrounds -> {OUT}")


if __name__ == "__main__":
    main()
