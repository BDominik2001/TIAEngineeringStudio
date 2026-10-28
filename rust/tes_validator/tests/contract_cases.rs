//! Runs the shared cross-language cases in tests/Contracts/validation-cases.json.

use std::path::PathBuf;

use serde_json::Value;
use tes_validator::validate_bytes;

#[test]
fn shared_contract_cases() {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../tests/Contracts/validation-cases.json");
    let text = std::fs::read_to_string(&path).unwrap_or_else(|e| panic!("cannot read {}: {e}", path.display()));
    let doc: Value = serde_json::from_str(&text).unwrap();
    let cases = doc["cases"].as_array().expect("cases array");
    assert!(!cases.is_empty());

    let mut failures = Vec::new();
    for case in cases {
        let name = case["name"].as_str().unwrap();
        let input = match case.get("input_raw") {
            Some(Value::String(raw)) => raw.as_bytes().to_vec(),
            _ => serde_json::to_vec(&case["input"]).unwrap(),
        };
        let report = validate_bytes(&input);
        let actual: Vec<(String, String, String)> = report
            .issues
            .iter()
            .map(|i| (i.severity.as_str().to_string(), i.code.to_string(), i.path.clone()))
            .collect();
        let expected: Vec<(String, String, String)> = case["expected"]
            .as_array()
            .unwrap()
            .iter()
            .map(|e| {
                (
                    e["severity"].as_str().unwrap().to_string(),
                    e["code"].as_str().unwrap().to_string(),
                    e["path"].as_str().unwrap().to_string(),
                )
            })
            .collect();
        if actual != expected {
            failures.push(format!("{name}:\n  expected {expected:?}\n  actual   {actual:?}"));
        }
    }
    assert!(
        failures.is_empty(),
        "{} case(s) failed:\n{}",
        failures.len(),
        failures.join("\n")
    );
}

#[test]
fn every_reported_code_is_in_the_shared_catalogue() {
    let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../contracts/error-codes.json");
    let catalogue: Value = serde_json::from_str(&std::fs::read_to_string(path).unwrap()).unwrap();
    let codes = catalogue["codes"].as_object().unwrap();
    let full = include_str!("../src/rules.rs");
    let source = full.split("#[cfg(test)]").next().unwrap();
    // Every "UPPER_CASE" string literal passed as a code must exist in the catalogue.
    for literal in source.split('"').skip(1).step_by(2) {
        let looks_like_code =
            literal.len() > 3 && literal.contains('_') && literal.bytes().all(|b| b.is_ascii_uppercase() || b == b'_');
        if looks_like_code {
            assert!(
                codes.contains_key(literal),
                "code {literal} missing from contracts/error-codes.json"
            );
        }
    }
}
