"""Reprodukálható buildazonosító (build-info.json) a verziókövetési szabályzat 4. pontja szerint.

    python scripts/tools/build_info.py [--tia V18|V19|V20] [--output build-info.json] [--check]

A külön nyilvántartott verziókat a forrásukból olvassa (nincs kézzel másolt érték):

  appVersion             Directory.Build.props <Version> (SemVer)
  releaseVersion         --release-version (kiadásnál a tag teljes verziója, pl. 0.2.0-beta.1), különben = appVersion
  gitCommitSha           GITHUB_SHA vagy `git rev-parse HEAD` (+ gitDirty)
  buildTimestampUtc      SOURCE_DATE_EPOCH, ha meg van adva; különben a jelenlegi idő
  targetTiaVersion       --tia (worker-csomag); a közös Desktop-buildnél null
  adapterVersion         a worker verziója (= appVersion) és a hivatkozott Openness API assembly verziója
  schemaVersion          contracts/machine-project.schema.json (schema_version const)
  protocolVersion        src/csharp/Ipc/WorkerProtocol.cs (ProtocolVersions.Current)
  templateCatalogVersion templates/scl/template-set.json (id@version + a készlet SHA-256-ja)
  dependencyLockfileHash a függőségzár-fájlok (Cargo.lock) együttes SHA-256-ja

--check: csak ellenőrzi, hogy az appVersion a Python-generátorban és a Rust crate-ben is azonos.
"""

from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TIA_VERSIONS = ("V18", "V19", "V20")
LOCKFILES = ("src/rust/validator/Cargo.lock",)


def _read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def _match(pattern: str, text: str, what: str) -> str:
    found = re.search(pattern, text)
    if not found:
        raise SystemExit(f"build_info: {what} not found")
    return found.group(1)


def app_version() -> str:
    return _match(r"<Version>([^<]+)</Version>", _read("Directory.Build.props"), "Directory.Build.props <Version>")


def component_versions() -> dict[str, str]:
    return {
        "Directory.Build.props": app_version(),
        "src/python/generator/__init__.py": _match(
            r'__version__ = "([^"]+)"', _read("src/python/generator/__init__.py"), "__version__"
        ),
        "src/rust/validator/Cargo.toml": _match(
            r'(?m)^version = "([^"]+)"', _read("src/rust/validator/Cargo.toml"), "crate version"
        ),
    }


def git_state() -> tuple[str | None, bool | None]:
    sha = os.environ.get("GITHUB_SHA")
    try:
        if not sha:
            sha = subprocess.run(
                ["git", "rev-parse", "HEAD"], cwd=ROOT, check=True, capture_output=True, text=True
            ).stdout.strip()
        dirty = bool(
            subprocess.run(
                ["git", "status", "--porcelain", "--untracked-files=no"], cwd=ROOT, check=True, capture_output=True, text=True
            ).stdout.strip()
        )
        return sha, dirty
    except (OSError, subprocess.CalledProcessError):
        return sha, None


def timestamp() -> str:
    epoch = os.environ.get("SOURCE_DATE_EPOCH")
    moment = (
        datetime.datetime.fromtimestamp(int(epoch), tz=datetime.timezone.utc)
        if epoch
        else datetime.datetime.now(tz=datetime.timezone.utc)
    )
    return moment.replace(microsecond=0).isoformat().replace("+00:00", "Z")


def template_catalog() -> dict[str, str]:
    descriptor = json.loads(_read("templates/scl/template-set.json"))
    combined = hashlib.sha256()
    for name in sorted(descriptor["files"]):
        combined.update(f"{name}\0{descriptor['files'][name]}\n".encode("ascii"))
    return {"id": descriptor["id"], "version": descriptor["version"], "sha256": combined.hexdigest()}


def lockfile_hash() -> str:
    combined = hashlib.sha256()
    for relative in LOCKFILES:
        combined.update(
            relative.encode("ascii") + b"\0" + hashlib.sha256((ROOT / relative).read_bytes()).hexdigest().encode("ascii") + b"\n"
        )
    return combined.hexdigest()


def build_info(tia: str | None, release_version: str | None = None) -> dict:
    """release_version: a kiadási tag teljes verziója (pl. 0.2.0-beta.1); alapverziója az appVersion."""
    release_version = release_version or app_version()
    if release_version != app_version() and not release_version.startswith(app_version() + "-"):
        raise SystemExit(f"build_info: release version {release_version} does not extend app version {app_version()}")
    sha, dirty = git_state()
    adapter = None
    if tia:
        csproj = _read(f"src/tia-adapters/{tia.lower()}/TiaStudio.TiaAdapter.{tia}.csproj")
        adapter = {
            "workerVersion": release_version,
            "opennessAssemblyVersion": _match(r"<TiaAssemblyVersion>([^<]+)</TiaAssemblyVersion>", csproj, "TiaAssemblyVersion"),
        }
    template = template_catalog()
    return {
        "appVersion": app_version(),
        "releaseVersion": release_version,
        "gitCommitSha": sha,
        "gitDirty": dirty,
        "buildTimestampUtc": timestamp(),
        "targetTiaVersion": tia,
        "adapterVersion": adapter,
        "schemaVersion": _match(
            r'"schema_version"\s*:\s*\{\s*"const"\s*:\s*"([^"]+)"',
            _read("contracts/machine-project.schema.json"),
            "schema_version const",
        ),
        "protocolVersion": _match(
            r'public const string Current = "([^"]+)";', _read("src/csharp/Ipc/WorkerProtocol.cs"), "ProtocolVersions.Current"
        ),
        "templateCatalogVersion": f"{template['id']}@{template['version']}",
        "templateCatalogSha256": template["sha256"],
        "dependencyLockfileHash": lockfile_hash(),
        # A sikeres build NEM jelent TIA-kompatibilitást (szabályzat 5. pont); ezt csak a
        # tia-integration workflow eredménye és a docs/COMPATIBILITY.md rögzítheti.
        "tiaIntegrationTested": False,
    }


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--tia", choices=TIA_VERSIONS)
    parser.add_argument("--output")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--release-version", help="a kiadási tag teljes verziója (pl. 0.2.0-beta.1)")
    args = parser.parse_args(argv)
    if args.check:
        versions = component_versions()
        if len(set(versions.values())) != 1:
            print("version mismatch: " + ", ".join(f"{k}={v}" for k, v in versions.items()), file=sys.stderr)
            return 1
        print("app version consistent: " + app_version())
        return 0
    text = json.dumps(build_info(args.tia, args.release_version), indent=2) + "\n"
    if args.output:
        Path(args.output).write_text(text, encoding="utf-8")
    else:
        sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
