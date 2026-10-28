"""Parancssori belépési pont.

    python -m generator.main --input ../examples/machine-v18.json --output ../generated/v18-demo

A standard outputra pontosan egy JSON-eredmény kerül (géppel feldolgozható);
minden napló a standard errorra megy. Kilépési kódok:
    0 = siker, 2 = validációs hiba, 3 = generálási/kimeneti hiba, 1 = belső hiba.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import json
import logging
import os
import shutil
import sys
import uuid
from pathlib import Path
from typing import Any

from . import __version__
from .manifest import build_manifest, serialize_manifest
from .models import MachineProject
from .render import NonAsciiSourceError, Renderer, TemplateIntegrityError, load_template_set
from .validation import validate_bytes

EXIT_OK = 0
EXIT_INTERNAL = 1
EXIT_VALIDATION = 2
EXIT_GENERATION = 3

log = logging.getLogger("tes_generator")


class GenerationError(Exception):
    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


def _generated_at(explicit: str | None) -> str:
    if explicit:
        return explicit
    epoch = os.environ.get("SOURCE_DATE_EPOCH")
    moment = _dt.datetime.fromtimestamp(int(epoch), _dt.timezone.utc) if epoch else _dt.datetime.now(_dt.timezone.utc)
    return moment.replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _publish(output: Path, files: dict[str, bytes], run_id: str) -> None:
    """Atomikus publikálás: staging mappa -> átnevezés. Meglévő célt soha nem ír felül."""
    if output.exists():
        raise GenerationError("OUTPUT_EXISTS", f"Output directory already exists: {output}")
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = output.parent / f".{output.name}.staging-{run_id}"
    staging.mkdir()
    try:
        for rel, data in files.items():
            target = staging / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            with open(target, "xb") as handle:
                handle.write(data)
                handle.flush()
                os.fsync(handle.fileno())
        if output.exists():
            raise GenerationError("OUTPUT_EXISTS", f"Output directory appeared during generation: {output}")
        os.rename(staging, output)
    except BaseException:
        shutil.rmtree(staging, ignore_errors=True)
        raise


def run(args: argparse.Namespace) -> tuple[int, dict[str, Any]]:
    input_path = Path(args.input)
    try:
        raw = input_path.read_bytes()
    except OSError as exc:
        return EXIT_GENERATION, {"status": "Failed", "code": "INVALID_JSON", "message": f"Cannot read input: {exc}"}

    doc, report = validate_bytes(raw)
    result: dict[str, Any] = {
        "generator_version": __version__,
        "source_config_sha256": hashlib.sha256(raw).hexdigest(),
        "validation": report.to_dict(),
    }
    if not report.ok or doc is None:
        log.error("validation failed with %d error(s)", report.error_count)
        return EXIT_VALIDATION, {"status": "ValidationFailed", "code": report.issues[0].code, **result}
    if args.validate_only:
        return EXIT_OK, {"status": "ValidationOnly", **result}

    try:
        template_set = load_template_set()
    except TemplateIntegrityError as exc:
        return EXIT_GENERATION, {"status": "Failed", "code": "TEMPLATE_INTEGRITY", "message": str(exc), **result}

    project = MachineProject.from_validated(doc)
    try:
        sources = Renderer(project, template_set).render()
    except NonAsciiSourceError as exc:
        return EXIT_GENERATION, {"status": "Failed", "code": "NON_ASCII_SOURCE", "message": str(exc), **result}

    run_id = args.run_id or str(uuid.uuid4())
    manifest = build_manifest(
        project=project,
        template_set=template_set,
        files=sources,
        report=report,
        source_config_sha256=result["source_config_sha256"],
        run_id=run_id,
        generated_at_utc=_generated_at(args.generated_at),
    )
    manifest_bytes = serialize_manifest(manifest)
    payload = {f.relative_path: f.content for f in sources}
    payload["manifest.json"] = manifest_bytes
    payload["validation-report.json"] = (json.dumps(report.to_dict(), indent=2) + "\n").encode("ascii")

    output = Path(os.path.abspath(args.output))
    try:
        _publish(output, payload, run_id)
    except GenerationError as exc:
        return EXIT_GENERATION, {"status": "Failed", "code": exc.code, "message": str(exc), **result}

    log.info("generated %d source file(s) into %s", len(sources), output)
    return EXIT_OK, {
        "status": "Success",
        **result,
        "run_id": run_id,
        "target_tia_version": project.target.tia_version,
        "output_directory": str(output),
        "manifest_path": str(output / "manifest.json"),
        "manifest_sha256": hashlib.sha256(manifest_bytes).hexdigest(),
        "files": manifest["files"],
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="generator", description="TIA Engineering Studio SCL generator")
    parser.add_argument("--input", required=True, help="machine project JSON (schema 1.1)")
    parser.add_argument("--output", help="new output directory (must not exist)")
    parser.add_argument("--run-id", help="run identifier (default: random UUID)")
    parser.add_argument("--generated-at", help="UTC timestamp for the manifest (default: now or SOURCE_DATE_EPOCH)")
    parser.add_argument("--validate-only", action="store_true", help="validate the input, write nothing")
    parser.add_argument("--version", action="version", version=f"%(prog)s {__version__}")
    return parser


def main(argv: list[str] | None = None) -> int:
    logging.basicConfig(stream=sys.stderr, level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")
    args = build_parser().parse_args(argv)
    if not args.validate_only and not args.output:
        build_parser().error("--output is required unless --validate-only is given")
    try:
        code, result = run(args)
    except Exception as exc:  # noqa: BLE001 - géppel olvasható hibát adunk vissza
        log.exception("internal error")
        code, result = EXIT_INTERNAL, {"status": "Failed", "code": "INTERNAL_ERROR", "message": str(exc)}
    sys.stdout.write(json.dumps(result, ensure_ascii=True) + "\n")
    sys.stdout.flush()
    return code


if __name__ == "__main__":
    sys.exit(main())
