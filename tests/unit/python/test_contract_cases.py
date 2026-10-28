"""A közös validációs esetek (tests/contracts/validation-cases.json) Pythonban."""

from __future__ import annotations

import json
import re
import unittest

from _support import CASES_FILE, CONTRACTS, REPO_ROOT
from generator import validation


class ContractCasesTest(unittest.TestCase):
    def test_all_shared_cases(self) -> None:
        cases = json.loads(CASES_FILE.read_text(encoding="utf-8"))["cases"]
        self.assertGreater(len(cases), 50)
        for case in cases:
            with self.subTest(case=case["name"]):
                if "input_raw" in case:
                    data = case["input_raw"].encode("utf-8")
                else:
                    data = json.dumps(case["input"]).encode("utf-8")
                _, report = validation.validate_bytes(data)
                actual = [{"severity": i.severity, "code": i.code, "path": i.path} for i in report.issues]
                self.assertEqual(case["expected"], actual)

    def test_emitted_codes_are_in_shared_catalogue(self) -> None:
        catalogue = json.loads((CONTRACTS / "error-codes.json").read_text(encoding="utf-8"))["codes"]
        sources = "".join(p.read_text(encoding="utf-8") for p in (REPO_ROOT / "src" / "python" / "generator").glob("*.py"))
        emitted = set(re.findall(r'"([A-Z][A-Z0-9]+(?:_[A-Z0-9]+)+)"', sources))
        emitted -= {"SOURCE_DATE_EPOCH", "TES_TEMPLATE_DIR"}  # környezeti változók, nem hibakódok
        self.assertTrue(emitted)
        self.assertEqual(set(), emitted - set(catalogue))

    def test_rules_version_matches_cases(self) -> None:
        self.assertEqual(json.loads(CASES_FILE.read_text(encoding="utf-8"))["rules_version"], validation.RULES_VERSION)


if __name__ == "__main__":
    unittest.main()
