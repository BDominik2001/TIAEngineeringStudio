"""Sablonkészlet-hashek karbantartása.

    python -m generator.template_hashes          # ellenőrzés (kilépési kód 1 eltérésnél)
    python -m generator.template_hashes --write  # template-set.json frissítése

Sablonmódosításnál a template-set.json verzióját is növelni kell, és a
golden SCL-fájlokat (tests/regression/golden-scl) tudatosan újra kell generálni.
"""

from __future__ import annotations

import hashlib
import json
import sys

from .render import TEMPLATE_DIR, TEMPLATE_SET_FILE


def current_hashes() -> dict[str, str]:
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(TEMPLATE_DIR.glob("*.tmpl"))}


def main(argv: list[str]) -> int:
    descriptor = json.loads(TEMPLATE_SET_FILE.read_text(encoding="utf-8"))
    hashes = current_hashes()
    if "--write" in argv:
        descriptor["files"] = hashes
        TEMPLATE_SET_FILE.write_text(json.dumps(descriptor, indent=2) + "\n", encoding="utf-8")
        print(f"updated {TEMPLATE_SET_FILE}")
        return 0
    if descriptor["files"] != hashes:
        print("template hashes differ from template-set.json", file=sys.stderr)
        return 1
    print("template hashes OK")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
