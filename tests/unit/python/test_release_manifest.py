"""Kiadási tag-ellenőrzés és metaadatok (scripts/tools/release_manifest.py, docs/RELEASE_PROCESS.md)."""

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
sys.modules["release_manifest"] = release_manifest  # a dataclass a modult a sys.modules-ban keresi
_spec.loader.exec_module(release_manifest)

STABLE = release_manifest.parse_tag("v0.9.0")
BETA = release_manifest.parse_tag("v0.9.0-beta.1")


class TagTest(unittest.TestCase):
    def test_prerelease_tag_keeps_base_version_separate(self) -> None:
        release = release_manifest.check_tag("v0.2.0-beta.1", "0.2.0")
        self.assertEqual(
            ("0.2.0-beta.1", "0.2.0", "beta", True), (release.version, release.base, release.channel, release.prerelease)
        )
        stable = release_manifest.check_tag("v0.2.0", "0.2.0")
        self.assertEqual(("0.2.0", "stable", False), (stable.version, stable.channel, stable.prerelease))
        for tag in ("v0.2.0-alpha.1", "v0.2.0-rc.12", "v1.0.0"):
            release_manifest.parse_tag(tag)

    def test_base_version_must_match_the_application(self) -> None:
        with self.assertRaises(SystemExit):
            release_manifest.check_tag("v0.3.0-rc.1", "0.2.0")

    def test_malformed_tags_are_rejected(self) -> None:
        for tag in ("0.2.0", "v0.2", "v0.2.0-beta", "v0.2.0-preview.1", "v0.2.0-beta.0", "v01.2.0", "v0.2.0+build"):
            with self.subTest(tag=tag), self.assertRaises(SystemExit):
                release_manifest.parse_tag(tag)

    def test_changelog_section_for_prerelease_falls_back_to_base_or_unreleased(self) -> None:
        text = "# Változástörténet\n\n## [Unreleased]\n\n- u\n\n## [0.9.0] - 2026-11-01\n\n- stable notes\n"
        self.assertEqual("- stable notes", release_manifest.changelog_section(BETA, text))
        self.assertEqual("- stable notes", release_manifest.changelog_section(STABLE, text))
        self.assertEqual("- u", release_manifest.changelog_section(release_manifest.parse_tag("v1.0.0-alpha.1"), text))
        with self.assertRaises(SystemExit):
            release_manifest.changelog_section(release_manifest.parse_tag("v1.0.0"), text)


class ReleaseGateTest(unittest.TestCase):
    def _package(self, directory: Path, release, tia: str, integration: str | None) -> Path:
        archive = directory / f"TIAStudio_{release.version}_TIA{tia[1:]}_win-x64.zip"
        archive.write_bytes(b"zip-" + tia.encode("ascii"))
        if integration is not None:
            status = {
                "tia": tia,
                "integration": integration,
                "run_url": "https://example.invalid/run/1",
                "environment": {"windows": "Windows 11 Pro 10.0.22631", "opennessFileVersion": "18.0.0.5"},
            }
            (directory / f"tia-status-{tia}.json").write_text(json.dumps(status), encoding="utf-8")
        return archive

    def test_stable_release_requires_all_three_versions_tested(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            for tia in ("V18", "V19", "V20"):
                self._package(directory, STABLE, tia, "passed")
            packages = {p["targetTiaVersion"]: p for p in release_manifest.collect(directory, STABLE)}
        self.assertEqual({"Supported & tested"}, {p["compatibilityStatus"] for p in packages.values()})
        self.assertEqual(hashlib.sha256(b"zip-V18").hexdigest(), packages["V18"]["sha256"])
        self.assertEqual("18.0.0.5", packages["V18"]["testEnvironment"]["opennessFileVersion"])

    def test_stable_release_stops_without_integration_test_or_package(self) -> None:
        for statuses in ({"V18": "passed", "V19": "passed", "V20": "not-run"}, {"V18": "passed", "V19": "passed"}):
            with self.subTest(statuses=statuses), tempfile.TemporaryDirectory() as tmp:
                directory = Path(tmp)
                for tia, result in statuses.items():
                    self._package(directory, STABLE, tia, result)
                with self.assertRaises(SystemExit):
                    release_manifest.collect(directory, STABLE)

    def test_prerelease_may_ship_untested_packages_clearly_marked(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            self._package(directory, BETA, "V18", "passed")
            self._package(directory, BETA, "V19", "not-run")
            self._package(directory, BETA, "V20", None)
            packages = {p["targetTiaVersion"]: p for p in release_manifest.collect(directory, BETA)}
        self.assertEqual("Supported & tested", packages["V18"]["compatibilityStatus"])
        self.assertEqual("Untested development build", packages["V19"]["compatibilityStatus"])
        self.assertEqual("Untested development build", packages["V20"]["compatibilityStatus"])

    def test_failed_integration_stops_any_release(self) -> None:
        for release in (STABLE, BETA):
            with self.subTest(release=release.tag), tempfile.TemporaryDirectory() as tmp:
                directory = Path(tmp)
                self._package(directory, release, "V20", "failed")
                with self.assertRaises(SystemExit):
                    release_manifest.collect(directory, release)

    def test_package_version_must_match_the_full_tag_version(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            self._package(directory, STABLE, "V18", "passed")  # 0.9.0 csomag egy 0.9.0-beta.1 kiadásban
            with self.assertRaises(SystemExit):
                release_manifest.collect(directory, BETA)

    def test_untested_prerelease_notes_carry_a_warning(self) -> None:
        info = {
            "gitCommitSha": "abc",
            "schemaVersion": "1.1",
            "protocolVersion": "1.1",
            "templateCatalogVersion": "t@1",
            "templateCatalogSha256": "x",
        }
        packages = [
            {
                "targetTiaVersion": "V18",
                "file": "a.zip",
                "sha256": "1",
                "compatibilityStatus": "Untested development build",
                "integrationTest": "not-run",
                "integrationRunUrl": None,
            }
        ]
        text = release_manifest.notes(BETA, info, packages, "- notes")
        self.assertIn("valódi TIA-integrációs teszt nélkül", text)
        self.assertIn("Ténylegesen tesztelt TIA-verziók: **nincs**", text)


if __name__ == "__main__":
    unittest.main()
