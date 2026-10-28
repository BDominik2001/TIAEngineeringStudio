"""Márka-eszközök előállítása a logóból (Pillow szükséges).

    python scripts/tools/build_brand_assets.py [--import ikon-keretes ikon-korvonalas ikon logo-teljes logo-app]

--import: az eredeti (1254 px-es) logóképekből előállítja a docs/assets/brand/ mesterképeket
(a halvány, 8 alatti alfájú háttérszemcsék törlése, levágás a tartalomra, négyzetes kitöltés).
Ezután a mesterképekből mindig újraépülnek a származtatott fájlok:

  src/csharp/Desktop/Assets/app.ico         alkalmazásikon (keretes ikon, 16-256 px)
  src/csharp/Desktop/Assets/logo-mark.png   ikon keret nélkül (fejléc, kezdőlap, nyitókép 1. fázis)
  src/csharp/Desktop/Assets/logo-full.png   teljes logó felirattal (nyitókép 2. fázis)
"""

import argparse
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BRAND = os.path.join(ROOT, "docs", "assets", "brand")
ASSETS = os.path.join(ROOT, "src", "csharp", "Desktop", "Assets")
MASTERS = ["icon-frame.png", "icon-outline.png", "icon.png", "logo-full.png", "logo-app.png"]
MASTER_SIZE = 768


def clean_square(path, pad_ratio=0.02):
    image = Image.open(path).convert("RGBA")
    if image.getextrema()[3][0] < 255:
        alpha = image.getchannel("A").point(lambda a: 0 if a < 8 else a)
        image.putalpha(alpha)
        image = image.crop(alpha.getbbox())
    side = max(image.size)
    side += int(side * pad_ratio * 2)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(image, ((side - image.width) // 2, (side - image.height) // 2), image)
    return canvas.resize((MASTER_SIZE, MASTER_SIZE), Image.LANCZOS)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--import", dest="originals", nargs=5, metavar="PNG")
    args = parser.parse_args()
    os.makedirs(BRAND, exist_ok=True)
    if args.originals:
        for source, name in zip(args.originals, MASTERS):
            clean_square(source).save(os.path.join(BRAND, name), optimize=True)

    masters = {name: Image.open(os.path.join(BRAND, name)).convert("RGBA") for name in MASTERS}
    os.makedirs(ASSETS, exist_ok=True)
    masters["icon-frame.png"].save(
        os.path.join(ASSETS, "app.ico"), sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
    )
    masters["icon.png"].resize((512, 512), Image.LANCZOS).save(os.path.join(ASSETS, "logo-mark.png"), optimize=True)
    masters["logo-full.png"].save(os.path.join(ASSETS, "logo-full.png"), optimize=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
