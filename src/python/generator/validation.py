"""Gépmodell-validáció (docs/VALIDATION_RULES.md, rules_version 1.1).

A Rust validátorral és a C# Domain-validátorral azonos hibakódokat, útvonalakat
és sorrendet állít elő; ezt a tests/contracts/validation-cases.json közös
tesztkészlet ellenőrzi.
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from typing import Any

RULES_VERSION = "1.1"
VALIDATOR_NAME = "tes_generator.validation"

MAX_INPUT_BYTES = 1_048_576
MAX_DEVICES = 256
SUPPORTED_SCHEMA_VERSION = "1.1"
SUPPORTED_TIA_VERSIONS = ("V18", "V19", "V20")
# Bemeneti folyamatkép bájttartománya CPU-családonként (spec 6.2).
INPUT_BYTE_LIMITS = {"S7-1200": 1023, "S7-1500": 32767}
SUPPORTED_KINDS = ("cylinder",)
RESERVED_BLOCK_NAMES = ("FB_Cylinder", "FC_Cylinders")
MAX_BLOCK_NAME_LENGTH = 125
MONITOR_TIME_MIN_MS = 100
MONITOR_TIME_MAX_MS = 60000
DEFAULT_MONITOR_TIME_MS = 2000

_PROJECT_NAME_RE = re.compile(r"[A-Za-z][A-Za-z0-9_]{0,63}")
_PLC_NAME_RE = re.compile(r"[A-Za-z][A-Za-z0-9_\-]{0,63}")
_DEVICE_ID_RE = re.compile(r"[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)*")
_INPUT_ADDRESS_RE = re.compile(r"I(0|[1-9][0-9]{0,4})\.([0-7])")
_PRINTABLE_ASCII_RE = re.compile(r"[\x20-\x7E]*")

_ROOT_FIELDS = ("schema_version", "project_name", "description", "target", "devices")
_TARGET_FIELDS = ("tia_version", "plc_name", "cpu_family")
_CYLINDER_FIELDS = ("id", "kind", "description", "inputs", "parameters")
_CYLINDER_INPUT_FIELDS = ("retracted", "extended")
_CYLINDER_PARAMETER_FIELDS = ("monitor_time_ms",)


@dataclass(frozen=True)
class Issue:
    severity: str
    code: str
    path: str
    message: str

    def to_dict(self) -> dict[str, str]:
        return {"severity": self.severity, "code": self.code, "path": self.path, "message": self.message}


@dataclass
class ValidationReport:
    issues: list[Issue] = field(default_factory=list)

    @property
    def error_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == "error")

    @property
    def warning_count(self) -> int:
        return sum(1 for i in self.issues if i.severity == "warning")

    @property
    def ok(self) -> bool:
        return self.error_count == 0

    def to_dict(self) -> dict[str, Any]:
        return {
            "validator": VALIDATOR_NAME,
            "rules_version": RULES_VERSION,
            "ok": self.ok,
            "error_count": self.error_count,
            "warning_count": self.warning_count,
            "issues": [i.to_dict() for i in self.issues],
        }


class _Validator:
    def __init__(self) -> None:
        self.issues: list[Issue] = []
        self.input_byte_limit: int | None = None
        self.seen_ids: dict[str, str] = {}
        self.seen_addresses: dict[str, str] = {}

    # -- helpers ---------------------------------------------------------
    def error(self, code: str, path: str, message: str) -> None:
        self.issues.append(Issue("error", code, path, message))

    def _present(self, obj: dict[str, Any], key: str, path: str, required: bool) -> bool:
        """True, ha a mező létezik és nem null; különben jelzi a hibát."""
        if key not in obj:
            if required:
                self.error("MISSING_FIELD", path, f"Required field '{key}' is missing")
            return False
        if obj[key] is None:
            self.error("NULL_VALUE", path, f"Field '{key}' must not be null")
            return False
        return True

    def _string(self, value: Any, path: str) -> bool:
        if not isinstance(value, str):
            self.error("INVALID_TYPE", path, f"Expected string, got {_json_type(value)}")
            return False
        return True

    def _ascii_identifier(self, value: Any, path: str, pattern: re.Pattern[str], rule: str) -> bool:
        if not self._string(value, path):
            return False
        if not value.isascii():
            self.error("NON_ASCII", path, f"Identifier '{value}' contains non-ASCII characters")
            return False
        if not pattern.fullmatch(value):
            self.error("INVALID_IDENTIFIER", path, f"Identifier '{value}' does not match {rule}")
            return False
        return True

    def _description(self, value: Any, path: str, max_length: int) -> None:
        if not self._string(value, path):
            return
        if not value.isascii():
            self.error("NON_ASCII", path, "Description contains non-ASCII characters")
        elif not _PRINTABLE_ASCII_RE.fullmatch(value):
            self.error("INVALID_VALUE", path, "Description may contain printable ASCII characters only")
        elif len(value) > max_length:
            self.error("INVALID_VALUE", path, f"Description is longer than {max_length} characters")

    def _object(self, value: Any, path: str) -> bool:
        if not isinstance(value, dict):
            self.error("INVALID_TYPE", path, f"Expected object, got {_json_type(value)}")
            return False
        return True

    def _unknown_fields(self, obj: dict[str, Any], known: tuple[str, ...], prefix: str) -> None:
        for key in sorted((k for k in obj if k not in known), key=lambda k: k.encode("utf-8")):
            self.error("UNKNOWN_FIELD", _join(prefix, key), f"Unknown field '{key}'")

    # -- rules -----------------------------------------------------------
    def root(self, doc: dict[str, Any]) -> None:
        if self._present(doc, "schema_version", "schema_version", True):
            value = doc["schema_version"]
            if self._string(value, "schema_version") and value != SUPPORTED_SCHEMA_VERSION:
                self.error(
                    "UNSUPPORTED_SCHEMA_VERSION",
                    "schema_version",
                    f"schema_version '{value}' is not supported (expected '{SUPPORTED_SCHEMA_VERSION}')",
                )
        if self._present(doc, "project_name", "project_name", True):
            self._ascii_identifier(doc["project_name"], "project_name", _PROJECT_NAME_RE, "^[A-Za-z][A-Za-z0-9_]{0,63}$")
        if self._present(doc, "description", "description", False):
            self._description(doc["description"], "description", 200)
        if self._present(doc, "target", "target", True) and self._object(doc["target"], "target"):
            self.target(doc["target"])
        if self._present(doc, "devices", "devices", True):
            self.devices(doc["devices"])
        self._unknown_fields(doc, _ROOT_FIELDS, "")

    def target(self, target: dict[str, Any]) -> None:
        if self._present(target, "tia_version", "target.tia_version", True):
            value = target["tia_version"]
            if self._string(value, "target.tia_version") and value not in SUPPORTED_TIA_VERSIONS:
                self.error("UNSUPPORTED_TIA_VERSION", "target.tia_version", f"TIA version '{value}' is not supported (V18, V19, V20)")
        if self._present(target, "plc_name", "target.plc_name", True):
            self._ascii_identifier(target["plc_name"], "target.plc_name", _PLC_NAME_RE, "^[A-Za-z][A-Za-z0-9_-]{0,63}$")
        if self._present(target, "cpu_family", "target.cpu_family", True):
            value = target["cpu_family"]
            if self._string(value, "target.cpu_family"):
                if value in INPUT_BYTE_LIMITS:
                    self.input_byte_limit = INPUT_BYTE_LIMITS[value]
                else:
                    self.error("UNSUPPORTED_CPU_FAMILY", "target.cpu_family", f"CPU family '{value}' is not supported (S7-1200, S7-1500)")
        self._unknown_fields(target, _TARGET_FIELDS, "target")

    def devices(self, devices: Any) -> None:
        if not isinstance(devices, list):
            self.error("INVALID_TYPE", "devices", f"Expected array, got {_json_type(devices)}")
            return
        if len(devices) == 0:
            self.error("NO_DEVICES", "devices", "At least one device is required")
            return
        if len(devices) > MAX_DEVICES:
            self.error("TOO_MANY_DEVICES", "devices", f"{len(devices)} devices exceed the limit of {MAX_DEVICES}")
            return
        for index, device in enumerate(devices):
            self.device(device, f"devices[{index}]")

    def device(self, device: Any, path: str) -> None:
        if not self._object(device, path):
            return
        valid_id: str | None = None
        if self._present(device, "id", f"{path}.id", True):
            device_id = device["id"]
            if self._ascii_identifier(device_id, f"{path}.id", _DEVICE_ID_RE, "^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$"):
                if len(device_id) > 32:
                    self.error("INVALID_IDENTIFIER", f"{path}.id", f"Identifier '{device_id}' is longer than 32 characters")
                else:
                    valid_id = device_id
                    self._device_id_unique(device_id, f"{path}.id")
        if not self._present(device, "kind", f"{path}.kind", True):
            return
        kind = device["kind"]
        if not self._string(kind, f"{path}.kind"):
            return
        if kind not in SUPPORTED_KINDS:
            self.error("UNSUPPORTED_DEVICE_KIND", f"{path}.kind", f"Device kind '{kind}' is not supported")
            return
        if valid_id is not None:
            self._generated_name(f"DB_Cylinder_{valid_id}", f"{path}.id")
        self.cylinder(device, path)

    def _device_id_unique(self, device_id: str, path: str) -> None:
        key = device_id.upper()
        if key in self.seen_ids:
            self.error("DUPLICATE_DEVICE_ID", path, f"Device id '{device_id}' is already used by {self.seen_ids[key]}")
        else:
            self.seen_ids[key] = path

    def _generated_name(self, block_name: str, path: str) -> None:
        if len(block_name) > MAX_BLOCK_NAME_LENGTH:
            self.error("GENERATED_NAME_TOO_LONG", path, f"Generated block name '{block_name}' exceeds {MAX_BLOCK_NAME_LENGTH} characters")
        if block_name.upper() in (n.upper() for n in RESERVED_BLOCK_NAMES):
            self.error("GENERATED_NAME_COLLISION", path, f"Generated block name '{block_name}' is reserved")

    def cylinder(self, device: dict[str, Any], path: str) -> None:
        if self._present(device, "description", f"{path}.description", False):
            self._description(device["description"], f"{path}.description", 120)
        if self._present(device, "inputs", f"{path}.inputs", True) and self._object(device["inputs"], f"{path}.inputs"):
            inputs = device["inputs"]
            for name in _CYLINDER_INPUT_FIELDS:
                if self._present(inputs, name, f"{path}.inputs.{name}", True):
                    self.input_address(inputs[name], f"{path}.inputs.{name}")
            self._unknown_fields(inputs, _CYLINDER_INPUT_FIELDS, f"{path}.inputs")
        if self._present(device, "parameters", f"{path}.parameters", False) and self._object(device["parameters"], f"{path}.parameters"):
            parameters = device["parameters"]
            p = f"{path}.parameters.monitor_time_ms"
            if self._present(parameters, "monitor_time_ms", p, False):
                value = parameters["monitor_time_ms"]
                if not _is_integer(value):
                    self.error("INVALID_TYPE", p, f"Expected integer, got {_json_type(value)}")
                elif not MONITOR_TIME_MIN_MS <= value <= MONITOR_TIME_MAX_MS:
                    self.error("INVALID_VALUE", p, f"monitor_time_ms {value} is outside {MONITOR_TIME_MIN_MS}..{MONITOR_TIME_MAX_MS}")
            self._unknown_fields(parameters, _CYLINDER_PARAMETER_FIELDS, f"{path}.parameters")
        self._unknown_fields(device, _CYLINDER_FIELDS, path)

    def input_address(self, value: Any, path: str) -> None:
        if not self._string(value, path):
            return
        match = _INPUT_ADDRESS_RE.fullmatch(value) if value.isascii() else None
        if match is None:
            self.error("INVALID_IO_ADDRESS", path, f"'{value}' is not a valid input address (e.g. I0.0)")
            return
        byte = int(match.group(1))
        if self.input_byte_limit is not None and byte > self.input_byte_limit:
            self.error("IO_ADDRESS_OUT_OF_RANGE", path, f"Input byte {byte} exceeds the CPU family limit {self.input_byte_limit}")
            return
        if value in self.seen_addresses:
            self.error("DUPLICATE_IO_ADDRESS", path, f"I/O address {value} is already used by {self.seen_addresses[value]}")
        else:
            self.seen_addresses[value] = path


def _join(prefix: str, key: str) -> str:
    return f"{prefix}.{key}" if prefix else key


def _is_integer(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _json_type(value: Any) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, (int, float)):
        return "number"
    if isinstance(value, str):
        return "string"
    if isinstance(value, list):
        return "array"
    return "object"


def _reject_constant(name: str) -> Any:
    raise ValueError(f"non-standard JSON constant {name}")


def parse_json_bytes(data: bytes) -> tuple[Any, ValidationReport | None]:
    """UTF-8 JSON-bájtok szigorú beolvasása. Hibánál (None, riport)."""
    if len(data) > MAX_INPUT_BYTES:
        return None, ValidationReport([Issue("error", "INPUT_TOO_LARGE", "$", f"Input is {len(data)} bytes; limit is {MAX_INPUT_BYTES}")])
    try:
        text = data.decode("utf-8")
        if text.startswith("﻿"):
            text = text[1:]
        doc = json.loads(text, parse_constant=_reject_constant)
    except (UnicodeDecodeError, ValueError) as exc:
        return None, ValidationReport([Issue("error", "INVALID_JSON", "$", f"Input is not valid UTF-8 JSON: {exc}")])
    if not isinstance(doc, dict):
        return None, ValidationReport([Issue("error", "INVALID_JSON", "$", "JSON root must be an object")])
    return doc, None


def validate_document(doc: dict[str, Any]) -> ValidationReport:
    validator = _Validator()
    validator.root(doc)
    report = ValidationReport(validator.issues)
    if report.ok:
        report.issues.append(
            Issue("warning", "IO_HW_NOT_VERIFIED", "devices", "I/O addresses were not checked against the TIA hardware configuration")
        )
    return report


def validate_bytes(data: bytes) -> tuple[dict[str, Any] | None, ValidationReport]:
    doc, failure = parse_json_bytes(data)
    if failure is not None:
        return None, failure
    assert doc is not None
    return doc, validate_document(doc)
