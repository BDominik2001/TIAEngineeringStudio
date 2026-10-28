"""Kiadási metaadatok (scripts/tools/release_manifest.py, docs/RELEASE_PROCESS.md)."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

from _support import REPO_ROOT

sys.path.insert(0, str(REPO_ROOT / "scripts" / "tools"))
_spec = importlib.util.spec_from_file_location("release_manifest", REPO_ROOT / "scripts" / "tools" / "release_manifest.py")
release_manifest = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(release_manifest)


class ReleaseManifestTest(unittest.TestCase):
    def _package(self, directory: Path, tia: str, integration: str | None) -> Path:
        archive = directory / f"TIAStudio_0.9.0_TIA{tia[1:]}_win-x64.zip"
        archive.write_bytes(b"zip-" + tia.encode("ascii"))
        if integration is not None:
            status = {"tia": tia, "integration": integration, "run_url": "https://example.invalid/run/1"}
            (directory / f"tia-status-{tia}.json").write_text(json.dumps(status), encoding="utf-8")
        return archive

    def test_status_is_tested_only_after_passed_integration(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            archive = self._package(directory, "V18", "passed")
            self._package(directory, "V19", "not-run")
            self._package(directory, "V20", None)
            packages = {p["targetTiaVersion"]: p for p in release_manifest.collect(directory, "0.9.0")}
        self.assertEqual("Supported & tested", packages["V18"]["compatibilityStatus"])
        self.assertEqual("Supported, integration test pending", packages["V19"]["compatibilityStatus"])
        self.assertEqual("Supported, integration test pending", packages["V20"]["compatibilityStatus"])
        self.assertEqual(hashlib.sha256(b"zip-V18").hexdigest(), packages["V18"]["sha256"])
        self.assertEqual(archive.name, packages["V18"]["file"])

    def test_failed_integration_stops_the_release(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            self._package(directory, "V20", "failed")
            with self.assertRaises(SystemExit):
                release_manifest.collect(directory, "0.9.0")

    def test_package_version_must_match_tag(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            self._package(directory, "V18", "passed")
            with self.assertRaises(SystemExit):
                release_manifest.collect(directory, "1.0.0")

    def test_changelog_has_unreleased_section(self) -> None:
        self.assertIn("### Hozzáadva", release_manifest.changelog_section("Unreleased"))


if __name__ == "__main__":
    unittest.main()
