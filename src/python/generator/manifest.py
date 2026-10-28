"""Generálási manifest (contracts/generation-manifest.schema.json) és útvonal-ellenőrzés."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path, PurePosixPath
from typing import Any, Sequence

from . import __version__
from .models import MachineProject
from .render import LINE_ENDING, SourceFile, TemplateSet
from .validation import ValidationReport

MANIFEST_VERSION = "1.0"


def build_manifest(
    *,
    project: MachineProject,
    template_set: TemplateSet,
    files: Sequence[SourceFile],
    report: ValidationReport,
    source_config_sha256: str,
    run_id: str,
    generated_at_utc: str,
) -> dict[str, Any]:
    return {
        "manifest_version": MANIFEST_VERSION,
        "generator": "tes_generator",
        "generator_version": __version__,
        "schema_version": project.schema_version,
        "project_name": project.project_name,
        "target_tia_version": project.target.tia_version,
        "plc_name": project.target.plc_name,
        "cpu_family": project.target.cpu_family,
        "template_set_id": template_set.set_id,
        "template_set_version": template_set.version,
        "template_set_sha256": template_set.sha256,
        "run_id": run_id,
        "generated_at_utc": generated_at_utc,
        "source_config_sha256": source_config_sha256,
        "encoding": "ascii",
        "line_ending": "CRLF" if LINE_ENDING == "\r\n" else "LF",
        "files": [
            {
                "relative_path": f.relative_path,
                "sha256": f.sha256,
                "byte_length": len(f.content),
                "block_name": f.block_name,
                "block_type": f.block_type,
                "import_order": index + 1,
            }
            for index, f in enumerate(files)
        ],
        "validation_result": report.to_dict(),
        "safety_scope": "non-safety",
        "download_allowed": False,
    }


def serialize_manifest(manifest: dict[str, Any]) -> bytes:
    return (json.dumps(manifest, indent=2, ensure_ascii=True) + "\n").encode("ascii")


def is_safe_relative_path(relative_path: str) -> bool:
    """Csak a csomaggyökéren belüli, előre-perjeles, '..' nélküli relatív útvonal fogadható el."""
    if not relative_path or "\\" in relative_path or ":" in relative_path or "\0" in relative_path:
        return False
    path = PurePosixPath(relative_path)
    if path.is_absolute():
        return False
    return all(part not in ("", ".", "..") for part in relative_path.split("/"))


def verify_package(root: Path) -> list[str]:
    """Újraszámolja a manifestben szereplő hasheket. Üres lista = rendben.

    Ugyanazt a szabályt a C# ManifestVerifier és a worker is alkalmazza import előtt.
    """
    problems: list[str] = []
    try:
        manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        return [f"MANIFEST_INVALID: {exc}"]
    if manifest.get("manifest_version") != MANIFEST_VERSION or manifest.get("download_allowed") is not False:
        return ["MANIFEST_INVALID: unsupported manifest_version or download_allowed is not false"]
    resolved_root = root.resolve()
    for entry in manifest.get("files", []):
        rel = entry.get("relative_path", "")
        if not is_safe_relative_path(rel):
            problems.append(f"PATH_ESCAPE: {rel!r}")
            continue
        path = root / rel
        if path.is_symlink() or resolved_root not in path.resolve().parents:
            problems.append(f"PATH_ESCAPE: {rel!r}")
            continue
        try:
            data = path.read_bytes()
        except OSError as exc:
            problems.append(f"HASH_MISMATCH: {rel}: {exc}")
            continue
        if hashlib.sha256(data).hexdigest() != entry.get("sha256") or len(data) != entry.get("byte_length"):
            problems.append(f"HASH_MISMATCH: {rel}")
    return problems
