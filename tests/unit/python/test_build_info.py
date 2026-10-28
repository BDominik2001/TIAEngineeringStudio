"""A build-info.json előállítása (scripts/tools/build_info.py, docs/VERSIONING.md)."""

from __future__ import annotations

import importlib.util
import json
import re
import unittest

from _support import REPO_ROOT

_spec = importlib.util.spec_from_file_location("build_info", REPO_ROOT / "scripts" / "tools" / "build_info.py")
build_info = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(build_info)


class BuildInfoTest(unittest.TestCase):
    def test_component_versions_match_app_version(self) -> None:
        versions = build_info.component_versions()
        self.assertEqual({build_info.app_version()}, set(versions.values()), versions)
        self.assertRegex(build_info.app_version(), r"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$")

    def test_worker_build_info_records_all_versions(self) -> None:
        info = build_info.build_info("V19")
        self.assertEqual("V19", info["targetTiaVersion"])
        self.assertEqual("19.0.0.0", info["adapterVersion"]["opennessAssemblyVersion"])
        schema = json.loads((REPO_ROOT / "contracts" / "machine-project.schema.json").read_text(encoding="utf-8"))
        self.assertEqual(schema["properties"]["schema_version"]["const"], info["schemaVersion"])
        template = json.loads((REPO_ROOT / "templates" / "scl" / "template-set.json").read_text(encoding="utf-8"))
        self.assertEqual(f"{template['id']}@{template['version']}", info["templateCatalogVersion"])
        self.assertTrue(re.fullmatch(r"[0-9a-f]{64}", info["dependencyLockfileHash"]))
        self.assertFalse(info["tiaIntegrationTested"], "a build önmagában nem jelent TIA-kompatibilitást")

    def test_template_catalog_hash_matches_generator(self) -> None:
        from generator import render

        self.assertEqual(render.load_template_set().sha256, build_info.template_catalog()["sha256"])

    def test_desktop_build_has_no_target_version(self) -> None:
        info = build_info.build_info(None)
        self.assertIsNone(info["targetTiaVersion"])
        self.assertIsNone(info["adapterVersion"])


if __name__ == "__main__":
    unittest.main()
