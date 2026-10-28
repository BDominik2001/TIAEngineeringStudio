"""Verziófüggetlen gépmodell típusai (csak validált dokumentumból építhetők)."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .validation import DEFAULT_MONITOR_TIME_MS


@dataclass(frozen=True)
class Target:
    tia_version: str
    plc_name: str
    cpu_family: str


@dataclass(frozen=True)
class Cylinder:
    id: str
    description: str
    retracted_input: str
    extended_input: str
    monitor_time_ms: int

    @property
    def instance_db_name(self) -> str:
        return f"DB_Cylinder_{self.id}"


@dataclass(frozen=True)
class MachineProject:
    schema_version: str
    project_name: str
    description: str
    target: Target
    cylinders: tuple[Cylinder, ...]

    @staticmethod
    def from_validated(doc: dict[str, Any]) -> "MachineProject":
        """A dokumentumot előbb a validation.validate_document()-nek kell elfogadnia."""
        target = doc["target"]
        cylinders = []
        for device in doc["devices"]:
            if device["kind"] != "cylinder":  # pragma: no cover - a validáció kizárja
                raise ValueError(f"unsupported device kind {device['kind']!r}")
            parameters = device.get("parameters") or {}
            cylinders.append(
                Cylinder(
                    id=device["id"],
                    description=device.get("description", ""),
                    retracted_input=device["inputs"]["retracted"],
                    extended_input=device["inputs"]["extended"],
                    monitor_time_ms=parameters.get("monitor_time_ms", DEFAULT_MONITOR_TIME_MS),
                )
            )
        return MachineProject(
            schema_version=doc["schema_version"],
            project_name=doc["project_name"],
            description=doc.get("description", ""),
            target=Target(target["tia_version"], target["plc_name"], target["cpu_family"]),
            cylinders=tuple(cylinders),
        )
