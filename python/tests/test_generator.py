from __future__ import annotations

import hashlib
import io
import json
import os
import shutil
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest import mock

from generator import main as cli
from generator import render
from generator.manifest import is_safe_relative_path, verify_package
from generator.models import MachineProject
from generator.validation import MAX_DEVICES, validate_document
from tests._support import EXAMPLES, GOLDEN_DIR, device, load_example

VERSIONS = ("V18", "V19", "V20")


def render_example(version: str, doc: dict | None = None) -> list[render.SourceFile]:
    doc = doc or load_example(version)
    assert validate_document(doc).ok
    return render.Renderer(MachineProject.from_validated(doc), render.load_template_set()).render()


def run_cli(*argv: str) -> tuple[int, dict]:
    out, err = io.StringIO(), io.StringIO()
    with redirect_stdout(out), redirect_stderr(err):
        code = cli.main(list(argv))
    lines = out.getvalue().splitlines()
    assert len(lines) == 1, f"stdout must contain exactly one JSON line, got {lines!r}"
    return code, json.loads(lines[0])


class RenderTest(unittest.TestCase):
    def test_golden_sources(self) -> None:
        """Regenerálás: UPDATE_GOLDEN=1 python -m unittest tests.test_generator"""
        for version in VERSIONS:
            for source in render_example(version):
                golden = GOLDEN_DIR / version / Path(source.relative_path).name
                with self.subTest(version=version, file=golden.name):
                    if os.environ.get("UPDATE_GOLDEN") == "1":
                        golden.parent.mkdir(parents=True, exist_ok=True)
                        golden.write_bytes(source.content)
                    self.assertEqual(golden.read_bytes(), source.content)

    def test_deterministic_bytes(self) -> None:
        first = [(f.relative_path, f.content) for f in render_example("V18")]
        second = [(f.relative_path, f.content) for f in render_example("V18")]
        self.assertEqual(first, second)

    def test_ascii_crlf_and_target_version_in_header(self) -> None:
        for version in VERSIONS:
            for source in render_example(version):
                text = source.content.decode("ascii")
                self.assertNotIn("\n", text.replace("\r\n", ""))
                self.assertIn(f"TIA Portal {version}", text)
                self.assertTrue(text.endswith("\r\n"))

    def test_import_order_and_block_names(self) -> None:
        files = render_example("V18")
        self.assertEqual(
            ["sources/FB_Cylinder.scl", "sources/DB_Cylinder_CYL_01.db", "sources/DB_Cylinder_CYL_02.db", "sources/FC_Cylinders.scl"],
            [f.relative_path for f in files],
        )
        self.assertEqual(
            ["FB_Cylinder", "DB_Cylinder_CYL_01", "DB_Cylinder_CYL_02", "FC_Cylinders"],
            [f.block_name for f in files],
        )
        self.assertEqual(["FB", "DB", "DB", "FC"], [f.block_type for f in files])

    def test_single_parameterised_fb_for_many_cylinders(self) -> None:
        doc = load_example("V18")
        doc["devices"] = [device(i) for i in range(MAX_DEVICES)]
        files = render_example("V18", doc)
        self.assertEqual(1, sum(1 for f in files if f.block_type == "FB"))
        self.assertEqual(MAX_DEVICES, sum(1 for f in files if f.block_type == "DB"))
        fc = files[-1].content.decode("ascii")
        self.assertEqual(MAX_DEVICES, fc.count("Reset := #Reset);"))

    def test_generated_code_writes_no_outputs(self) -> None:
        for source in render_example("V18"):
            text = source.content.decode("ascii")
            for forbidden in ("%Q", "%M", "%DB", "Q0.", "PEEK", "POKE"):
                self.assertNotIn(forbidden, text)

    def test_iec_time(self) -> None:
        self.assertEqual("T#2S", render.format_iec_time(2000))
        self.assertEqual("T#2S_500MS", render.format_iec_time(2500))
        self.assertEqual("T#100MS", render.format_iec_time(100))
        self.assertEqual("T#60S", render.format_iec_time(60000))

    def test_template_integrity_is_enforced(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            copy = Path(tmp) / "templates"
            shutil.copytree(render.TEMPLATE_DIR, copy)
            (copy / "FB_Cylinder.scl.tmpl").write_bytes(b"tampered\n")
            with self.assertRaises(render.TemplateIntegrityError):
                render.load_template_set(copy)

    def test_template_set_hash_is_recorded(self) -> None:
        self.assertEqual(64, len(render.load_template_set().sha256))


class ManifestTest(unittest.TestCase):
    def test_safe_relative_paths(self) -> None:
        self.assertTrue(is_safe_relative_path("sources/FB_Cylinder.scl"))
        for bad in ("", "../x.scl", "sources/../../x", "/abs.scl", "C:/x.scl", "sources\\x.scl", "./x", "a//b"):
            with self.subTest(path=bad):
                self.assertFalse(is_safe_relative_path(bad))


class CliTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, ignore_errors=True)

    def test_generate_writes_package_and_manifest(self) -> None:
        out = self.tmp / "run-001"
        code, result = run_cli("--input", str(EXAMPLES / "machine-v19.json"), "--output", str(out), "--run-id", "run-001")
        self.assertEqual(0, code)
        self.assertEqual("Success", result["status"])
        manifest = json.loads((out / "manifest.json").read_text(encoding="ascii"))
        self.assertEqual("V19", manifest["target_tia_version"])
        self.assertIs(False, manifest["download_allowed"])
        self.assertEqual("non-safety", manifest["safety_scope"])
        self.assertEqual("run-001", manifest["run_id"])
        expected_config_hash = hashlib.sha256((EXAMPLES / "machine-v19.json").read_bytes()).hexdigest()
        self.assertEqual(expected_config_hash, manifest["source_config_sha256"])
        self.assertEqual(hashlib.sha256((out / "manifest.json").read_bytes()).hexdigest(), result["manifest_sha256"])
        self.assertEqual([], verify_package(out))
        self.assertFalse(any(p.name.startswith(".") for p in self.tmp.iterdir()), "staging directory left behind")

    def test_existing_output_is_never_overwritten(self) -> None:
        out = self.tmp / "run"
        out.mkdir()
        (out / "keep.txt").write_text("x")
        code, result = run_cli("--input", str(EXAMPLES / "machine-v18.json"), "--output", str(out))
        self.assertEqual(3, code)
        self.assertEqual("OUTPUT_EXISTS", result["code"])
        self.assertEqual(["keep.txt"], [p.name for p in out.iterdir()])

    def test_validation_failure_writes_nothing(self) -> None:
        doc = load_example("V18")
        doc["target"]["tia_version"] = "V21"
        src = self.tmp / "bad.json"
        src.write_text(json.dumps(doc))
        out = self.tmp / "out"
        code, result = run_cli("--input", str(src), "--output", str(out))
        self.assertEqual(2, code)
        self.assertEqual("ValidationFailed", result["status"])
        self.assertEqual("UNSUPPORTED_TIA_VERSION", result["code"])
        self.assertFalse(out.exists())

    def test_validate_only(self) -> None:
        code, result = run_cli("--input", str(EXAMPLES / "machine-v20.json"), "--validate-only")
        self.assertEqual(0, code)
        self.assertEqual("ValidationOnly", result["status"])

    def test_manifest_detects_tampering(self) -> None:
        out = self.tmp / "pkg"
        self.assertEqual(0, run_cli("--input", str(EXAMPLES / "machine-v18.json"), "--output", str(out))[0])
        target = out / "sources" / "FC_Cylinders.scl"
        target.write_bytes(target.read_bytes().replace(b"%I0.0", b"%I0.7"))
        self.assertEqual(["HASH_MISMATCH: sources/FC_Cylinders.scl"], verify_package(out))

    def test_manifest_rejects_escaping_paths(self) -> None:
        out = self.tmp / "pkg"
        self.assertEqual(0, run_cli("--input", str(EXAMPLES / "machine-v18.json"), "--output", str(out))[0])
        manifest = json.loads((out / "manifest.json").read_text())
        manifest["files"][0]["relative_path"] = "../outside.scl"
        (out / "manifest.json").write_text(json.dumps(manifest))
        self.assertEqual(["PATH_ESCAPE: '../outside.scl'"], verify_package(out))

    def test_same_input_same_source_hashes_across_runs(self) -> None:
        hashes = []
        for name in ("a", "b"):
            code, result = run_cli("--input", str(EXAMPLES / "machine-v18.json"), "--output", str(self.tmp / name))
            self.assertEqual(0, code)
            hashes.append([f["sha256"] for f in result["files"]])
        self.assertEqual(hashes[0], hashes[1])

    def test_generated_at_honours_source_date_epoch(self) -> None:
        with mock.patch.dict(os.environ, {"SOURCE_DATE_EPOCH": "0"}):
            self.assertEqual("1970-01-01T00:00:00Z", cli._generated_at(None))

    def test_internal_error_is_reported_as_json(self) -> None:
        with mock.patch.object(cli, "run", side_effect=RuntimeError("boom")):
            code, result = run_cli("--input", "x", "--validate-only")
        self.assertEqual(1, code)
        self.assertEqual("INTERNAL_ERROR", result["code"])


if __name__ == "__main__":
    unittest.main()
