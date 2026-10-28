//! Semantic rules of the machine model, schema_version 1.1.
//! Normative source: docs/VALIDATION_RULES.md. Issue order is part of the contract.

use std::collections::HashMap;

use serde_json::{Map, Value};

use crate::report::{Issue, Report, Severity};

pub const MAX_INPUT_BYTES: usize = 1_048_576;
pub const MAX_DEVICES: usize = 256;
const UTF8_BOM: &[u8] = b"\xEF\xBB\xBF";

const SCHEMA_VERSION: &str = "1.1";
const TIA_VERSIONS: [&str; 3] = ["V18", "V19", "V20"];
const MAX_BLOCK_NAME_LEN: usize = 125;
const RESERVED_BLOCK_NAMES: [&str; 2] = ["FB_Cylinder", "FC_Cylinders"];
const MONITOR_TIME_MIN: i64 = 100;
const MONITOR_TIME_MAX: i64 = 60_000;

const ROOT_FIELDS: [&str; 5] = ["schema_version", "project_name", "description", "target", "devices"];
const TARGET_FIELDS: [&str; 3] = ["tia_version", "plc_name", "cpu_family"];
const DEVICE_FIELDS: [&str; 5] = ["id", "kind", "description", "inputs", "parameters"];
const INPUT_FIELDS: [&str; 2] = ["retracted", "extended"];
const PARAMETER_FIELDS: [&str; 1] = ["monitor_time_ms"];

/// Highest input byte address of the process image per CPU family.
fn max_input_byte(cpu_family: &str) -> Option<u32> {
    match cpu_family {
        "S7-1200" => Some(1023),
        "S7-1500" => Some(32767),
        _ => None,
    }
}

/// Validates raw UTF-8 JSON bytes and returns the deterministic report.
pub fn validate_bytes(input: &[u8]) -> Report {
    if input.len() > MAX_INPUT_BYTES {
        return too_large_report(input.len());
    }
    // Windows tools often write a UTF-8 BOM; the size limit above counts it.
    let json = input.strip_prefix(UTF8_BOM).unwrap_or(input);
    let mut v = Validator::default();
    match serde_json::from_slice::<Value>(json) {
        Ok(Value::Object(root)) => v.root(&root),
        Ok(_) => v.error("INVALID_JSON", "$", "root element must be a JSON object".into()),
        Err(e) => v.error("INVALID_JSON", "$", format!("input is not valid UTF-8 JSON: {e}")),
    }
    v.finish()
}

pub(crate) fn too_large_report(len: usize) -> Report {
    let mut v = Validator::default();
    v.error(
        "INPUT_TOO_LARGE",
        "$",
        format!("input is {len} bytes; maximum is {MAX_INPUT_BYTES} bytes"),
    );
    v.finish()
}

#[derive(Default)]
struct Validator {
    issues: Vec<Issue>,
    /// Upper-cased device id -> first path.
    seen_ids: HashMap<String, String>,
    /// Canonical address -> first path.
    seen_addresses: HashMap<String, String>,
    max_input_byte: Option<u32>,
}

fn child(parent: &str, key: &str) -> String {
    if parent.is_empty() || parent == "$" {
        key.to_string()
    } else {
        format!("{parent}.{key}")
    }
}

fn type_name(value: &Value) -> &'static str {
    match value {
        Value::Null => "null",
        Value::Bool(_) => "boolean",
        Value::Number(n) if n.is_f64() => "number",
        Value::Number(_) => "integer",
        Value::String(_) => "string",
        Value::Array(_) => "array",
        Value::Object(_) => "object",
    }
}

fn is_printable_ascii(s: &str) -> bool {
    s.bytes().all(|b| (0x20..=0x7E).contains(&b))
}

/// `^[A-Za-z][A-Za-z0-9_]{0,63}$` (extra_hyphen adds `-` to the tail class).
fn is_plc_identifier(s: &str, allow_hyphen: bool) -> bool {
    let b = s.as_bytes();
    if b.is_empty() || b.len() > 64 || !b[0].is_ascii_alphabetic() {
        return false;
    }
    b[1..]
        .iter()
        .all(|&c| c.is_ascii_alphanumeric() || c == b'_' || (allow_hyphen && c == b'-'))
}

/// `^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$`, at most 32 characters.
fn is_device_id(s: &str) -> bool {
    let b = s.as_bytes();
    if b.is_empty() || b.len() > 32 || !b[0].is_ascii_uppercase() {
        return false;
    }
    let mut prev_underscore = false;
    for &c in &b[1..] {
        if c == b'_' {
            if prev_underscore {
                return false;
            }
            prev_underscore = true;
        } else if c.is_ascii_uppercase() || c.is_ascii_digit() {
            prev_underscore = false;
        } else {
            return false;
        }
    }
    !prev_underscore
}

/// `^I(0|[1-9][0-9]{0,4})\.[0-7]$` -> byte address.
fn parse_input_address(s: &str) -> Option<u32> {
    let rest = s.strip_prefix('I')?;
    let (byte_part, bit_part) = rest.split_once('.')?;
    let bb = byte_part.as_bytes();
    if bb.is_empty() || bb.len() > 5 || !bb.iter().all(u8::is_ascii_digit) {
        return None;
    }
    if bb.len() > 1 && bb[0] == b'0' {
        return None;
    }
    let bit = bit_part.as_bytes();
    if bit.len() != 1 || !(b'0'..=b'7').contains(&bit[0]) {
        return None;
    }
    byte_part.parse().ok()
}

impl Validator {
    fn push(&mut self, severity: Severity, code: &'static str, path: &str, message: String) {
        self.issues.push(Issue {
            severity,
            code,
            path: path.to_string(),
            message,
        });
    }

    fn error(&mut self, code: &'static str, path: &str, message: String) {
        self.push(Severity::Error, code, path, message);
    }

    fn finish(mut self) -> Report {
        let has_errors = self.issues.iter().any(|i| i.severity == Severity::Error);
        if !has_errors {
            self.push(
                Severity::Warning,
                "IO_HW_NOT_VERIFIED",
                "devices",
                "I/O addresses were not verified against the TIA hardware configuration; verify them manually".into(),
            );
        }
        Report::new(self.issues)
    }

    /// Returns the field value when present and not null; reports MISSING_FIELD / NULL_VALUE.
    fn field<'a>(&mut self, obj: &'a Map<String, Value>, key: &str, path: &str, required: bool) -> Option<&'a Value> {
        match obj.get(key) {
            None => {
                if required {
                    self.error("MISSING_FIELD", path, format!("required field '{key}' is missing"));
                }
                None
            }
            Some(Value::Null) => {
                self.error("NULL_VALUE", path, format!("field '{key}' must not be null"));
                None
            }
            Some(v) => Some(v),
        }
    }

    fn string_field<'a>(
        &mut self,
        obj: &'a Map<String, Value>,
        key: &str,
        path: &str,
        required: bool,
    ) -> Option<&'a str> {
        match self.field(obj, key, path, required)? {
            Value::String(s) => Some(s.as_str()),
            other => {
                self.error(
                    "INVALID_TYPE",
                    path,
                    format!("expected string, got {}", type_name(other)),
                );
                None
            }
        }
    }

    fn object_field<'a>(
        &mut self,
        obj: &'a Map<String, Value>,
        key: &str,
        path: &str,
        required: bool,
    ) -> Option<&'a Map<String, Value>> {
        match self.field(obj, key, path, required)? {
            Value::Object(o) => Some(o),
            other => {
                self.error(
                    "INVALID_TYPE",
                    path,
                    format!("expected object, got {}", type_name(other)),
                );
                None
            }
        }
    }

    fn unknown_fields(&mut self, obj: &Map<String, Value>, known: &[&str], parent: &str) {
        let mut unknown: Vec<&String> = obj.keys().filter(|k| !known.contains(&k.as_str())).collect();
        unknown.sort();
        for key in unknown {
            self.error("UNKNOWN_FIELD", &child(parent, key), format!("unknown field '{key}'"));
        }
    }

    fn identifier(&mut self, value: &str, path: &str, allow_hyphen: bool) -> bool {
        if !value.is_ascii() {
            self.error("NON_ASCII", path, "value must contain ASCII characters only".into());
            return false;
        }
        if !is_plc_identifier(value, allow_hyphen) {
            self.error(
                "INVALID_IDENTIFIER",
                path,
                format!("'{value}' is not a valid identifier"),
            );
            return false;
        }
        true
    }

    fn description(&mut self, obj: &Map<String, Value>, path: &str, max_len: usize) {
        if let Some(text) = self.string_field(obj, "description", path, false) {
            if !text.is_ascii() {
                self.error(
                    "NON_ASCII",
                    path,
                    "description must contain ASCII characters only".into(),
                );
            } else if !is_printable_ascii(text) {
                self.error(
                    "INVALID_VALUE",
                    path,
                    "description must contain printable ASCII characters only (0x20-0x7E)".into(),
                );
            } else if text.len() > max_len {
                self.error(
                    "INVALID_VALUE",
                    path,
                    format!("description is {} characters; maximum is {max_len}", text.len()),
                );
            }
        }
    }

    fn root(&mut self, root: &Map<String, Value>) {
        if let Some(sv) = self.string_field(root, "schema_version", "schema_version", true) {
            if sv != SCHEMA_VERSION {
                self.error(
                    "UNSUPPORTED_SCHEMA_VERSION",
                    "schema_version",
                    format!("schema_version '{sv}' is not supported; expected '{SCHEMA_VERSION}'"),
                );
            }
        }
        if let Some(name) = self.string_field(root, "project_name", "project_name", true) {
            self.identifier(name, "project_name", false);
        }
        self.description(root, "description", 200);
        if let Some(target) = self.object_field(root, "target", "target", true) {
            self.target(target);
        }
        self.devices(root);
        self.unknown_fields(root, &ROOT_FIELDS, "$");
    }

    fn target(&mut self, target: &Map<String, Value>) {
        if let Some(version) = self.string_field(target, "tia_version", "target.tia_version", true) {
            if !TIA_VERSIONS.contains(&version) {
                self.error(
                    "UNSUPPORTED_TIA_VERSION",
                    "target.tia_version",
                    format!("TIA version '{version}' is not supported; expected V18, V19 or V20"),
                );
            }
        }
        if let Some(plc) = self.string_field(target, "plc_name", "target.plc_name", true) {
            self.identifier(plc, "target.plc_name", true);
        }
        if let Some(cpu) = self.string_field(target, "cpu_family", "target.cpu_family", true) {
            self.max_input_byte = max_input_byte(cpu);
            if self.max_input_byte.is_none() {
                self.error(
                    "UNSUPPORTED_CPU_FAMILY",
                    "target.cpu_family",
                    format!("CPU family '{cpu}' is not supported; expected S7-1200 or S7-1500"),
                );
            }
        }
        self.unknown_fields(target, &TARGET_FIELDS, "target");
    }

    fn devices(&mut self, root: &Map<String, Value>) {
        let devices = match self.field(root, "devices", "devices", true) {
            Some(Value::Array(a)) => a,
            Some(other) => {
                self.error(
                    "INVALID_TYPE",
                    "devices",
                    format!("expected array, got {}", type_name(other)),
                );
                return;
            }
            None => return,
        };
        if devices.is_empty() {
            self.error("NO_DEVICES", "devices", "at least one device is required".into());
            return;
        }
        if devices.len() > MAX_DEVICES {
            self.error(
                "TOO_MANY_DEVICES",
                "devices",
                format!("devices contains {} items; maximum is {MAX_DEVICES}", devices.len()),
            );
            return;
        }
        for (index, device) in devices.iter().enumerate() {
            let path = format!("devices[{index}]");
            match device {
                Value::Object(obj) => self.device(obj, &path),
                other => self.error(
                    "INVALID_TYPE",
                    &path,
                    format!("expected object, got {}", type_name(other)),
                ),
            }
        }
    }

    fn device(&mut self, device: &Map<String, Value>, path: &str) {
        let id_path = child(path, "id");
        let mut valid_id: Option<&str> = None;
        if let Some(id) = self.string_field(device, "id", &id_path, true) {
            if !id.is_ascii() {
                self.error(
                    "NON_ASCII",
                    &id_path,
                    "device id must contain ASCII characters only".into(),
                );
            } else if !is_device_id(id) {
                self.error(
                    "INVALID_IDENTIFIER",
                    &id_path,
                    format!("device id '{id}' must match ^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$ (max 32 characters)"),
                );
            } else {
                let key = id.to_ascii_uppercase();
                if let Some(first) = self.seen_ids.get(&key) {
                    let message = format!("device id '{id}' is already used by {first}");
                    self.error("DUPLICATE_DEVICE_ID", &id_path, message);
                } else {
                    self.seen_ids.insert(key, id_path.clone());
                }
                valid_id = Some(id);
            }
        }

        let kind_path = child(path, "kind");
        let kind = match self.string_field(device, "kind", &kind_path, true) {
            Some("cylinder") => "cylinder",
            Some(other) => {
                self.error(
                    "UNSUPPORTED_DEVICE_KIND",
                    &kind_path,
                    format!("device kind '{other}' is not supported; expected cylinder"),
                );
                return;
            }
            None => return,
        };

        if let Some(id) = valid_id {
            self.generated_name(kind, id, &id_path);
        }
        self.description(device, &child(path, "description"), 120);

        let inputs_path = child(path, "inputs");
        if let Some(inputs) = self.object_field(device, "inputs", &inputs_path, true) {
            for key in INPUT_FIELDS {
                let address_path = child(&inputs_path, key);
                if let Some(address) = self.string_field(inputs, key, &address_path, true) {
                    self.address(address, &address_path);
                }
            }
            self.unknown_fields(inputs, &INPUT_FIELDS, &inputs_path);
        }

        let params_path = child(path, "parameters");
        if let Some(params) = self.object_field(device, "parameters", &params_path, false) {
            let mt_path = child(&params_path, "monitor_time_ms");
            if let Some(value) = self.field(params, "monitor_time_ms", &mt_path, false) {
                self.monitor_time(value, &mt_path);
            }
            self.unknown_fields(params, &PARAMETER_FIELDS, &params_path);
        }

        self.unknown_fields(device, &DEVICE_FIELDS, path);
    }

    fn generated_name(&mut self, kind: &str, id: &str, id_path: &str) {
        // Only the cylinder kind exists today; future kinds add their own prefix here.
        let name = match kind {
            "cylinder" => format!("DB_Cylinder_{id}"),
            _ => return,
        };
        if name.len() > MAX_BLOCK_NAME_LEN {
            self.error(
                "GENERATED_NAME_TOO_LONG",
                id_path,
                format!("generated block name '{name}' exceeds {MAX_BLOCK_NAME_LEN} characters"),
            );
        } else if RESERVED_BLOCK_NAMES.iter().any(|r| r.eq_ignore_ascii_case(&name)) {
            self.error(
                "GENERATED_NAME_COLLISION",
                id_path,
                format!("generated block name '{name}' collides with a template block name"),
            );
        }
    }

    fn address(&mut self, address: &str, path: &str) {
        let Some(byte) = parse_input_address(address) else {
            self.error(
                "INVALID_IO_ADDRESS",
                path,
                format!("'{address}' is not a valid input address (expected e.g. I0.0)"),
            );
            return;
        };
        if let Some(max) = self.max_input_byte {
            if byte > max {
                self.error(
                    "IO_ADDRESS_OUT_OF_RANGE",
                    path,
                    format!("I/O address {address} is outside the input range I0.0..I{max}.7 of the CPU family"),
                );
                return;
            }
        }
        if let Some(first) = self.seen_addresses.get(address) {
            let message = format!("I/O address {address} is already used by {first}");
            self.error("DUPLICATE_IO_ADDRESS", path, message);
        } else {
            self.seen_addresses.insert(address.to_string(), path.to_string());
        }
    }

    fn monitor_time(&mut self, value: &Value, path: &str) {
        let Value::Number(n) = value else {
            self.error(
                "INVALID_TYPE",
                path,
                format!("expected integer, got {}", type_name(value)),
            );
            return;
        };
        let in_range = if let Some(i) = n.as_i64() {
            (MONITOR_TIME_MIN..=MONITOR_TIME_MAX).contains(&i)
        } else if n.as_u64().is_some() {
            false
        } else {
            self.error("INVALID_TYPE", path, format!("expected integer, got {n}"));
            return;
        };
        if !in_range {
            self.error(
                "INVALID_VALUE",
                path,
                format!("monitor_time_ms {n} is outside {MONITOR_TIME_MIN}..{MONITOR_TIME_MAX}"),
            );
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn device_id_matcher() {
        for ok in ["CYL_01", "A", "MTR_01", "AX_X01", "A1_B2_C3"] {
            assert!(is_device_id(ok), "{ok}");
        }
        for bad in ["", "cyl_01", "_A", "A_", "A__B", "1A", "A-B", &"A".repeat(33)] {
            assert!(!is_device_id(bad), "{bad}");
        }
    }

    #[test]
    fn address_parser() {
        assert_eq!(parse_input_address("I0.0"), Some(0));
        assert_eq!(parse_input_address("I32767.7"), Some(32767));
        assert_eq!(parse_input_address("I99999.0"), Some(99999));
        for bad in [
            "I0.8",
            "I01.0",
            "%I0.0",
            "Q0.0",
            "I.0",
            "I0.",
            "I0",
            "I123456.0",
            "I0.0 ",
            "I+1.0",
        ] {
            assert_eq!(parse_input_address(bad), None, "{bad}");
        }
    }
}
