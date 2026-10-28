"""Kiadási metaadatok: checksums.sha256, release-manifest.json, release-notes.md (docs/RELEASE_PROCESS.md).

    python scripts/tools/release_manifest.py --dir <csomagok> --tag v0.2.0 [--run-url URL]

A --dir mappa tartalma a csomagoló jobokból: TIAStudio_<ver>_TIA<NN>_win-x64.zip és mellette
tia-status-V<NN>.json ({"tia": "V18", "integration": "passed|failed|not-run", "run_url": "..."}).

Kompatibilitási állapot (szabályzat 11. pont) csomagonként:
  passed  -> "Supported & tested"
  not-run -> "Supported, integration test pending"
  failed  -> a kiadás megáll (sikertelen TIA-teszttel nem készül kiadás)
A sikeres fordítás önmagában soha nem "tested".
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))

import build_info  # noqa: E402

STATUS = {"passed": "Supported & tested", "not-run": "Supported, integration test pending"}
PACKAGE_RE = re.compile(r"^TIAStudio_(?P<version>[^_]+)_TIA(?P<major>\d\d)_win-x64\.zip$")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def changelog_section(version: str) -> str:
    text = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
    match = re.search(rf"(?ms)^## \[{re.escape(version)}\][^\n]*\n(.*?)(?=^## \[|\Z)", text)
    if not match:
        raise SystemExit(f"CHANGELOG.md has no section for [{version}]")
    return match.group(1).strip()


def collect(directory: Path, version: str) -> list[dict]:
    packages = []
    for archive in sorted(directory.glob("*.zip")):
        found = PACKAGE_RE.match(archive.name)
        if not found:
            raise SystemExit(f"unexpected file: {archive.name}")
        if found.group("version") != version:
            raise SystemExit(f"{archive.name}: package version != tag version {version}")
        tia = "V" + found.group("major")
        status_file = directory / f"tia-status-{tia}.json"
        status = json.loads(status_file.read_text(encoding="utf-8")) if status_file.is_file() else {"integration": "not-run"}
        if status["integration"] == "failed":
            raise SystemExit(f"{tia}: TIA integration test failed - no release")
        if status["integration"] not in STATUS:
            raise SystemExit(f"{tia}: unknown integration result {status['integration']!r}")
        packages.append(
            {
                "file": archive.name,
                "sha256": sha256(archive),
                "size": archive.stat().st_size,
                "targetTiaVersion": tia,
                "compatibilityStatus": STATUS[status["integration"]],
                "integrationTest": status["integration"],
                "integrationRunUrl": status.get("run_url"),
            }
        )
    if not packages:
        raise SystemExit("no packages found")
    return packages


def notes(tag: str, info: dict, packages: list[dict], section: str) -> str:
    tested = [p["targetTiaVersion"] for p in packages if p["integrationTest"] == "passed"]
    rows = "\n".join(
        f"| {p['targetTiaVersion']} | `{p['file']}` | {p['compatibilityStatus']} | "
        + (f"[run]({p['integrationRunUrl']})" if p["integrationRunUrl"] else "-")
        + " |"
        for p in packages
    )
    sums = "\n".join(f"{p['sha256']}  {p['file']}" for p in packages)
    return f"""# TIA Engineering Studio {tag}

{section}

## Kompatibilitás

| TIA | Csomag | Állapot | TIA-integrációs teszt |
|---|---|---|---|
{rows}

Ténylegesen tesztelt TIA-verziók: **{", ".join(tested) if tested else "nincs"}**. A sikeres build önmagában
nem jelent TIA-kompatibilitást; a részletes mátrix: `docs/COMPATIBILITY.md`.

## Verziók

- Alkalmazás: {info["appVersion"]} · Git commit: `{info["gitCommitSha"]}`
- Gépmodell-séma: {info["schemaVersion"]} · IPC-protokoll: {info["protocolVersion"]}
- Sablonkészlet: {info["templateCatalogVersion"]} (`{info["templateCatalogSha256"]}`)

## Előfeltételek

Windows 10/11 x64, .NET Framework 4.8, Python 3.9+ a PATH-on, a célverziójú TIA Portal + Openness
telepítve, a felhasználó a „Siemens TIA Openness” csoport tagja. A Siemens DLL-ek nincsenek a csomagban.

## Ellenőrzőösszegek (SHA-256)

```text
{sums}
```
"""


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dir", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--run-url")
    args = parser.parse_args(argv)
    directory = Path(args.dir)
    info = build_info.build_info(None)
    version = info["appVersion"]
    if args.tag != f"v{version}":
        raise SystemExit(f"tag {args.tag} != v{version} (Directory.Build.props)")
    packages = collect(directory, version)
    manifest = {
        "tag": args.tag,
        "appVersion": version,
        "prerelease": "-" in version,
        "gitCommitSha": info["gitCommitSha"],
        "buildTimestampUtc": info["buildTimestampUtc"],
        "schemaVersion": info["schemaVersion"],
        "protocolVersion": info["protocolVersion"],
        "templateCatalogVersion": info["templateCatalogVersion"],
        "dependencyLockfileHash": info["dependencyLockfileHash"],
        "workflowRunUrl": args.run_url,
        "packages": packages,
    }
    (directory / "release-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    (directory / "checksums.sha256").write_text("".join(f"{p['sha256']}  {p['file']}\n" for p in packages), encoding="utf-8")
    (directory / "release-notes.md").write_text(notes(args.tag, info, packages, changelog_section(version)), encoding="utf-8")
    print(f"release metadata written for {len(packages)} package(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
