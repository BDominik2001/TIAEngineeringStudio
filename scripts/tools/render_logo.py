"""A logó (docs/assets/logo.svg) raszterizálása PNG-be és ICO-ba Pillow-val.

    python scripts/tools/render_logo.py

A geometria megegyezik a logo.svg-vel és a Desktop Themes/Logo.xaml vektorával (64x64 egység).
Kimenet: docs/assets/logo-256.png, src/TiaStudio.Desktop/Assets/app.ico
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SUPERSAMPLE = 8


def _hex(color: str, alpha: float = 1.0) -> tuple[int, int, int, int]:
    color = color.lstrip("#")
    return int(color[0:2], 16), int(color[2:4], 16), int(color[4:6], 16), round(255 * alpha)


def render(size: int) -> Image.Image:
    s = size * SUPERSAMPLE / 64.0
    big = Image.new("RGBA", (size * SUPERSAMPLE, size * SUPERSAMPLE), (0, 0, 0, 0))

    # Jelvény függőleges színátmenettel (maszkolt kitöltés)
    mask = Image.new("L", big.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([2 * s, 2 * s, 62 * s, 62 * s], radius=13 * s, fill=255)
    top, bottom = _hex("#46525E"), _hex("#232B33")
    gradient = Image.new("RGBA", big.size)
    gd = ImageDraw.Draw(gradient)
    for y in range(big.size[1]):
        t = y / max(1, big.size[1] - 1)
        gd.line([(0, y), (big.size[0], y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    big.paste(gradient, (0, 0), mask)

    d = ImageDraw.Draw(big)
    stroke = round(3 * s)
    light = _hex("#E9EDF0")
    for y in (24, 40):
        d.line([(10 * s, y * s), (20 * s, y * s)], fill=light, width=stroke)
        d.ellipse([(10 - 1.5) * s, (y - 1.5) * s, (10 + 1.5) * s, (y + 1.5) * s], fill=light)
    d.rounded_rectangle([18.5 * s, 12.5 * s, 45.5 * s, 51.5 * s], radius=4 * s, fill=light)
    orange = _hex("#F39C12")
    d.line([(44 * s, 32 * s), (52 * s, 32 * s)], fill=orange, width=stroke)
    d.ellipse([51 * s, 28.5 * s, 58 * s, 35.5 * s], fill=orange)
    for y, alpha in ((21, 1.0), (30, 0.75), (39, 0.5)):
        d.rounded_rectangle([26 * s, y * s, 38 * s, (y + 4) * s], radius=2 * s, fill=_hex("#3E9AB5", alpha))
    return big.resize((size, size), Image.LANCZOS)


def main() -> None:
    assets = ROOT / "docs" / "assets"
    assets.mkdir(parents=True, exist_ok=True)
    render(256).save(assets / "logo-256.png")
    icon_dir = ROOT / "src" / "TiaStudio.Desktop" / "Assets"
    icon_dir.mkdir(parents=True, exist_ok=True)
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [render(n) for n in sizes]
    images[-1].save(icon_dir / "app.ico", format="ICO", sizes=[(n, n) for n in sizes], append_images=images[:-1])
    render(512).save(icon_dir / "logo-512.png")
    print("logo rendered")


if __name__ == "__main__":
    main()
