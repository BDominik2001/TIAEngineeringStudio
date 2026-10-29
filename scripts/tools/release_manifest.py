"""Kiadási tag-ellenőrzés és metaadatok (docs/RELEASE_PROCESS.md).

    python scripts/tools/release_manifest.py --check-tag v0.2.0-beta.1
    python scripts/tools/release_manifest.py --dir <csomagok> --tag v0.2.0 [--run-url URL]

Tag-formátum: v<MAJOR>.<MINOR>.<PATCH> (stabil) vagy v<MAJOR>.<MINOR>.<PATCH>-alpha|beta|rc.<N>
(előzetes kiadás). Az alapverzió (az utótag nélkül) egyezzen a Directory.Build.props <Version>
értékével; a csomagnevek, a manifest és az alkalmazás InformationalVersion-je a teljes tag-verziót
hordozza (pl. 0.2.0-beta.1).

A --dir mappa tartalma a csomagoló jobokból: TIAStudio_<tag-verzió>_TIA<NN>_win-x64.zip és mellette
tia-status-V<NN>.json ({"tia": "V18", "integration": "passed|failed|not-run", "run_url": "...",
"environment": {...}}).

Kiadási kapu (szabályzat 4. pont):
  stabil kiadás:   V18, V19 és V20 csomag kötelező, mindegyiknek "passed" TIA-integrációs teszttel;
                   hiányzó vagy ki nem futott teszt = a kiadás megáll
  előzetes kiadás: "not-run" megengedett, a csomag állapota "Untested development build" –
                   nem nevezhető Openness-kompatibilisnek
  "failed":        a kiadás mindig megáll
A sikeres fordítás önmagában soha nem "tested".
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))

import build_info  # noqa: E402

TIA_VERSIONS = ("V18", "V19", "V20")
TESTED = "Supported & tested"
UNTESTED = "Untested development build"
TAG_RE = re.compile(
    r"^v(?P<base>(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*))"  # MAJOR.MINOR.PATCH
    r"(?:-(?P<channel>alpha|beta|rc)\.(?P<number>[1-9]\d*))?$"  # opcionális előzetes-kiadás utótag
)
PACKAGE_RE = re.compile(r"^TIAStudio_(?P<version>[^_]+)_TIA(?P<major>\d\d)_win-x64\.zip$")


@dataclass(frozen=True)
class ReleaseTag:
    tag: str
    version: str  # teljes verzió, pl. 0.2.0-beta.1
    base: str  # alapverzió, pl. 0.2.0 (= Directory.Build.props)
    channel: str  # alpha | beta | rc | stable

    @property
    def prerelease(self) -> bool:
        return self.channel != "stable"


def parse_tag(tag: str) -> ReleaseTag:
    match = TAG_RE.match(tag)
    if not match:
        raise SystemExit(f"invalid release tag {tag!r}: expected vX.Y.Z or vX.Y.Z-alpha|beta|rc.N")
    return ReleaseTag(tag, tag[1:], match.group("base"), match.group("channel") or "stable")


def check_tag(tag: str, app_version: str) -> ReleaseTag:
    release = parse_tag(tag)
    if release.base != app_version:
        raise SystemExit(f"tag {tag}: base version {release.base} != {app_version} (Directory.Build.props)")
    return release


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def changelog_section(release: ReleaseTag, text: str | None = None) -> str:
    """Stabil kiadás: [X.Y.Z]; előzetes kiadás: [X.Y.Z-ch.N], ennek hiányában [X.Y.Z] vagy [Unreleased]."""
    text = text if text is not None else (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
    candidates = [release.version] if not release.prerelease else [release.version, release.base, "Unreleased"]
    for version in candidates:
        match = re.search(rf"(?ms)^## \[{re.escape(version)}\][^\n]*\n(.*?)(?=^## \[|\Z)", text)
        if match:
            return match.group(1).strip()
    raise SystemExit(f"CHANGELOG.md has no section for [{candidates[0]}]")


def collect(directory: Path, release: ReleaseTag) -> list[dict]:
    packages = []
    for archive in sorted(directory.glob("*.zip")):
        found = PACKAGE_RE.match(archive.name)
        if not found:
            raise SystemExit(f"unexpected file: {archive.name}")
        if found.group("version") != release.version:
            raise SystemExit(f"{archive.name}: package version != tag version {release.version}")
        tia = "V" + found.group("major")
        status_file = directory / f"tia-status-{tia}.json"
        status = json.loads(status_file.read_text(encoding="utf-8-sig")) if status_file.is_file() else {"integration": "not-run"}
        result = status.get("integration")
        if result == "failed":
            raise SystemExit(f"{tia}: TIA integration test failed - no release")
        if result not in ("passed", "not-run"):
            raise SystemExit(f"{tia}: unknown integration result {result!r}")
        if result == "not-run" and not release.prerelease:
            raise SystemExit(f"{tia}: stable release {release.tag} requires a passed TIA integration test")
        packages.append(
            {
                "file": archive.name,
                "sha256": sha256(archive),
                "size": archive.stat().st_size,
                "targetTiaVersion": tia,
                "compatibilityStatus": TESTED if result == "passed" else UNTESTED,
                "integrationTest": result,
                "integrationRunUrl": status.get("run_url"),
                "testEnvironment": status.get("environment"),
            }
        )
    if not packages:
        raise SystemExit("no packages found")
    if not release.prerelease:
        missing = sorted(set(TIA_VERSIONS) - {p["targetTiaVersion"] for p in packages})
        if missing:
            raise SystemExit(f"stable release {release.tag} is missing packages for {', '.join(missing)}")
    return packages


def notes(release: ReleaseTag, info: dict, packages: list[dict], section: str) -> str:
    tested = [p["targetTiaVersion"] for p in packages if p["integrationTest"] == "passed"]
    rows = "\n".join(
        f"| {p['targetTiaVersion']} | `{p['file']}` | {p['compatibilityStatus']} | "
        + (f"[run]({p['integrationRunUrl']})" if p["integrationRunUrl"] and p["integrationTest"] == "passed" else "nem futott")
        + " |"
        for p in packages
    )
    environments = "\n".join(
        f"- {p['targetTiaVersion']}: " + ", ".join(f"{k}: `{v}`" for k, v in sorted(p["testEnvironment"].items()))
        for p in packages
        if p["integrationTest"] == "passed" and p.get("testEnvironment")
    )
    warning = ""
    if len(tested) < len(packages):
        warning = (
            "> **Előzetes fejlesztői kiadás, valódi TIA-integrációs teszt nélkül** a nem tesztelt csomagoknál.\n"
            "> Ezek nem tekinthetők Openness-kompatibilisnek; éles projekthez nem használhatók.\n\n"
        )
    environment_block = "\n### Tesztkörnyezet\n\n" + environments + "\n" if environments else ""
    sums = "\n".join(f"{p['sha256']}  {p['file']}" for p in packages)
    return f"""# TIA Engineering Studio {release.tag}

{warning}{section}

## Kompatibilitás

| TIA | Csomag | Állapot | TIA-integrációs teszt |
|---|---|---|---|
{rows}

Ténylegesen tesztelt TIA-verziók: **{", ".join(tested) if tested else "nincs"}**. A sikeres build önmagában
nem jelent TIA-kompatibilitást; a részletes mátrix: `docs/COMPATIBILITY.md`.
{environment_block}
## Verziók

- Alkalmazás: {release.version} · Git commit: `{info["gitCommitSha"]}`
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
    parser.add_argument("--check-tag", metavar="TAG", help="tag formátum, alapverzió és CHANGELOG ellenőrzése")
    parser.add_argument("--dir")
    parser.add_argument("--tag")
    parser.add_argument("--run-url")
    args = parser.parse_args(argv)
    if args.check_tag:
        release = check_tag(args.check_tag, build_info.app_version())
        changelog_section(release)
        print(f"version={release.version}")
        print(f"prerelease={'true' if release.prerelease else 'false'}")
        return 0
    if not args.dir or not args.tag:
        parser.error("--dir and --tag are required (or use --check-tag)")
    directory = Path(args.dir)
    info = build_info.build_info(None)
    release = check_tag(args.tag, info["appVersion"])
    packages = collect(directory, release)
    manifest = {
        "tag": release.tag,
        "releaseVersion": release.version,
        "appVersion": info["appVersion"],
        "channel": release.channel,
        "prerelease": release.prerelease,
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
    (directory / "release-notes.md").write_text(notes(release, info, packages, changelog_section(release)), encoding="utf-8")
    print(f"release metadata written for {len(packages)} package(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
