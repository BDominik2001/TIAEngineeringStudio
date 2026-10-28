"""Determinisztikus SCL-renderelés a verziókezelt sablonkészletből.

Azonos gépmodell + sablonkészlet -> azonos forrásbájtok (spec 7.1/1).
A kimenet ASCII, CRLF sorvégű SCL (a V18 külső forrás követelménye szerint).
"""

from __future__ import annotations

import hashlib
import json
import os
from dataclasses import dataclass
from pathlib import Path
from string import Template

from .models import Cylinder, MachineProject


def _find_template_dir() -> Path:
    """A verziózott SCL-sablonkészlet mappája.

    Sorrend: TES_TEMPLATE_DIR környezeti változó, majd a generator csomag fölötti mappák
    templates/scl almappája (repó: <gyökér>/templates/scl, kiadás: <telepítés>/templates/scl).
    """
    override = os.environ.get("TES_TEMPLATE_DIR")
    if override:
        return Path(override).resolve()
    here = Path(__file__).resolve().parent
    for parent in here.parents:
        candidate = parent / "templates" / "scl"
        if (candidate / "template-set.json").is_file():
            return candidate
    raise FileNotFoundError("templates/scl/template-set.json not found above " + str(here))


TEMPLATE_DIR = _find_template_dir()
TEMPLATE_SET_FILE = TEMPLATE_DIR / "template-set.json"
LINE_ENDING = "\r\n"
SOURCE_EXTENSIONS = {"FB": ".scl", "FC": ".scl", "DB": ".db", "UDT": ".udt"}


class TemplateIntegrityError(Exception):
    """A sablonfájlok hash-e eltér a template-set.json-ban rögzítettől."""


class NonAsciiSourceError(Exception):
    """A renderelt forrás nem ASCII (nem szabadna előfordulnia validált bemenetnél)."""


@dataclass(frozen=True)
class SourceFile:
    relative_path: str
    block_name: str
    block_type: str
    content: bytes

    @property
    def sha256(self) -> str:
        return hashlib.sha256(self.content).hexdigest()


@dataclass(frozen=True)
class TemplateSet:
    set_id: str
    version: str
    sha256: str
    templates: dict[str, str]


def load_template_set(directory: Path = TEMPLATE_DIR) -> TemplateSet:
    """Betölti és hash-ellenőrzi a sablonkészletet."""
    descriptor = json.loads((directory / "template-set.json").read_text(encoding="utf-8"))
    templates: dict[str, str] = {}
    combined = hashlib.sha256()
    for name in sorted(descriptor["files"]):
        expected = descriptor["files"][name]
        data = (directory / name).read_bytes()
        actual = hashlib.sha256(data).hexdigest()
        if actual != expected:
            raise TemplateIntegrityError(f"template {name} hash {actual} != recorded {expected}")
        combined.update(f"{name}\0{actual}\n".encode("ascii"))
        templates[name] = data.decode("ascii").replace("\r\n", "\n")
    return TemplateSet(descriptor["id"], descriptor["version"], combined.hexdigest(), templates)


def format_iec_time(ms: int) -> str:
    """Ezredmásodperc -> IEC TIME literál (pl. 2500 -> T#2S_500MS)."""
    seconds, millis = divmod(ms, 1000)
    if seconds and millis:
        return f"T#{seconds}S_{millis}MS"
    if seconds:
        return f"T#{seconds}S"
    return f"T#{millis}MS"


def _to_source(text: str) -> bytes:
    normalized = text.replace("\r\n", "\n")
    if not normalized.endswith("\n"):
        normalized += "\n"
    try:
        return normalized.replace("\n", LINE_ENDING).encode("ascii")
    except UnicodeEncodeError as exc:
        raise NonAsciiSourceError(str(exc)) from exc


class Renderer:
    def __init__(self, project: MachineProject, template_set: TemplateSet) -> None:
        self.project = project
        self.templates = template_set

    def _template(self, name: str) -> Template:
        return Template(self.templates.templates[name])

    def _header(self, block_name: str) -> str:
        target = self.project.target
        return self._template("_header.scl.tmpl").substitute(
            template_set_id=self.templates.set_id,
            template_set_version=self.templates.version,
            tia_version=target.tia_version,
            cpu_family=target.cpu_family,
            plc_name=target.plc_name,
            project_name=self.project.project_name,
            block_name=block_name,
        )

    def _file(self, block_name: str, block_type: str, body: str) -> SourceFile:
        content = _to_source(self._header(block_name) + body)
        # TIA Portal Openness V18 kézikönyv 5.11.3.17/5.11.3.18: külső forrás kiterjesztése
        # típusfüggő (*.scl kódblokk, *.db adatblokk, *.udt adattípus).
        extension = SOURCE_EXTENSIONS[block_type]
        return SourceFile(f"sources/{block_name}{extension}", block_name, block_type, content)

    def render(self) -> list[SourceFile]:
        """Forrásfájlok importálási sorrendben: FB -> instance DB-k -> hívó FC."""
        files = [self._file("FB_Cylinder", "FB", self._template("FB_Cylinder.scl.tmpl").substitute())]
        for cylinder in self.project.cylinders:
            files.append(
                self._file(
                    cylinder.instance_db_name,
                    "DB",
                    self._template("DB_Cylinder.scl.tmpl").substitute(
                        block_name=cylinder.instance_db_name,
                        device_id=cylinder.id,
                        device_description=_description(cylinder),
                    ),
                )
            )
        calls = "".join(self._call(c) for c in self.project.cylinders)
        files.append(
            self._file(
                "FC_Cylinders",
                "FC",
                self._template("FC_Cylinders.scl.tmpl").substitute(cylinder_count=len(self.project.cylinders), calls=calls),
            )
        )
        return files

    def _call(self, cylinder: Cylinder) -> str:
        return self._template("FC_Cylinders.call.scl.tmpl").substitute(
            device_id=cylinder.id,
            device_description=_description(cylinder),
            block_name=cylinder.instance_db_name,
            call_indent=" " * (len(cylinder.instance_db_name) + 3),
            retracted_input=cylinder.retracted_input,
            extended_input=cylinder.extended_input,
            monitor_time=format_iec_time(cylinder.monitor_time_ms),
        )


def _description(cylinder: Cylinder) -> str:
    return cylinder.description or "(no description)"
