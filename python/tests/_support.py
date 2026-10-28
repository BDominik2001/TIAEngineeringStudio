from __future__ import annotations

import json
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
EXAMPLES = REPO_ROOT / "examples"
CONTRACTS = REPO_ROOT / "contracts"
CASES_FILE = REPO_ROOT / "tests" / "Contracts" / "validation-cases.json"
GOLDEN_DIR = REPO_ROOT / "tests" / "GoldenScl"


def load_example(version: str) -> dict:
    return json.loads((EXAMPLES / f"machine-{version.lower()}.json").read_text(encoding="utf-8"))


def device(index: int) -> dict:
    """Egyedi címkiosztású munkahenger (legrosszabb esetű eszközszám tesztekhez)."""
    return {
        "id": f"CYL_{index:03d}",
        "kind": "cylinder",
        "inputs": {"retracted": f"I{index}.0", "extended": f"I{index}.1"},
    }
