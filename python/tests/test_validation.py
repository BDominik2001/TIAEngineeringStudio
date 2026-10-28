from __future__ import annotations

import copy
import json
import unittest

from generator import validation
from tests._support import device, load_example


def codes(data: bytes) -> list[str]:
    return [i.code for i in validation.validate_bytes(data)[1].issues]


class ValidationLimitsTest(unittest.TestCase):
    def test_examples_are_valid_for_all_versions(self) -> None:
        for version in ("V18", "V19", "V20"):
            with self.subTest(version=version):
                doc = load_example(version)
                self.assertEqual(version, doc["target"]["tia_version"])
                self.assertEqual(["IO_HW_NOT_VERIFIED"], codes(json.dumps(doc).encode()))

    def test_worst_case_device_count_is_accepted(self) -> None:
        doc = load_example("V18")
        doc["devices"] = [device(i) for i in range(validation.MAX_DEVICES)]
        self.assertEqual(["IO_HW_NOT_VERIFIED"], codes(json.dumps(doc).encode()))

    def test_too_many_devices(self) -> None:
        doc = load_example("V18")
        doc["devices"] = [device(i) for i in range(validation.MAX_DEVICES + 1)]
        self.assertEqual(["TOO_MANY_DEVICES"], codes(json.dumps(doc).encode()))

    def test_input_too_large(self) -> None:
        data = b" " * (validation.MAX_INPUT_BYTES + 1)
        self.assertEqual(["INPUT_TOO_LARGE"], codes(data))

    def test_invalid_utf8(self) -> None:
        self.assertEqual(["INVALID_JSON"], codes(b'{"project_name": "\xff"}'))

    def test_nan_rejected(self) -> None:
        self.assertEqual(["INVALID_JSON"], codes(b'{"x": NaN}'))

    def test_bom_is_accepted(self) -> None:
        data = b"\xef\xbb\xbf" + json.dumps(load_example("V18")).encode()
        self.assertEqual(["IO_HW_NOT_VERIFIED"], codes(data))

    def test_non_ascii_address_is_invalid_address(self) -> None:
        doc = load_example("V18")
        doc["devices"][0]["inputs"]["retracted"] = "Ié.0"
        self.assertEqual(["INVALID_IO_ADDRESS"], codes(json.dumps(doc).encode()))

    def test_huge_monitor_time_is_invalid_value(self) -> None:
        doc = load_example("V18")
        doc["devices"][0]["parameters"] = {"monitor_time_ms": 10**30}
        self.assertEqual(["INVALID_VALUE"], codes(json.dumps(doc).encode()))

    def test_validation_does_not_mutate_input(self) -> None:
        doc = load_example("V18")
        before = copy.deepcopy(doc)
        validation.validate_document(doc)
        self.assertEqual(before, doc)


if __name__ == "__main__":
    unittest.main()
