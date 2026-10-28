use tes_validator::{validate_bytes, MAX_INPUT_BYTES};

fn machine(device_count: usize) -> String {
    let devices: Vec<String> = (0..device_count)
        .map(|i| {
            format!(
                r#"{{"id":"CYL_{i:03}","kind":"cylinder","inputs":{{"retracted":"I{b}.0","extended":"I{b}.1"}}}}"#,
                b = i
            )
        })
        .collect();
    format!(
        r#"{{"schema_version":"1.1","project_name":"Cell","target":{{"tia_version":"V18","plc_name":"PLC_1","cpu_family":"S7-1500"}},"devices":[{}]}}"#,
        devices.join(",")
    )
}

fn codes(input: &[u8]) -> Vec<&'static str> {
    validate_bytes(input).issues.iter().map(|i| i.code).collect()
}

#[test]
fn max_devices_is_accepted() {
    let report = validate_bytes(machine(256).as_bytes());
    assert!(report.ok(), "{}", report.to_json());
    assert_eq!(report.warning_count(), 1);
}

#[test]
fn too_many_devices_reports_once_without_item_checks() {
    assert_eq!(codes(machine(257).as_bytes()), vec!["TOO_MANY_DEVICES"]);
}

#[test]
fn input_too_large() {
    let mut big = machine(1).into_bytes();
    big.resize(MAX_INPUT_BYTES + 1, b' ');
    assert_eq!(codes(&big), vec!["INPUT_TOO_LARGE"]);
    let report = validate_bytes(&big);
    assert_eq!(report.issues[0].path, "$");
}

#[test]
fn invalid_utf8_is_invalid_json() {
    let mut bytes = machine(1).into_bytes();
    let pos = bytes.iter().position(|&b| b == b'C').unwrap();
    bytes[pos] = 0xFF;
    assert_eq!(codes(&bytes), vec!["INVALID_JSON"]);
}

#[test]
fn empty_input_is_invalid_json() {
    assert_eq!(codes(b""), vec!["INVALID_JSON"]);
}

#[test]
fn utf8_bom_is_skipped() {
    let mut bytes = b"\xEF\xBB\xBF".to_vec();
    bytes.extend_from_slice(machine(2).as_bytes());
    let report = validate_bytes(&bytes);
    assert!(report.ok(), "{}", report.to_json());
    assert_eq!(codes(&bytes), vec!["IO_HW_NOT_VERIFIED"]);
}

#[test]
fn bom_counts_toward_size_limit() {
    let mut bytes = b"\xEF\xBB\xBF".to_vec();
    bytes.extend_from_slice(machine(1).as_bytes());
    bytes.resize(MAX_INPUT_BYTES + 1, b' ');
    assert_eq!(codes(&bytes), vec!["INPUT_TOO_LARGE"]);
}

#[test]
fn non_standard_constants_are_invalid_json() {
    let input = machine(1).replace(r#""devices""#, r#""x":NaN,"devices""#);
    assert_eq!(codes(input.as_bytes()), vec!["INVALID_JSON"]);
}

#[test]
fn output_is_deterministic_and_ordered() {
    let input = machine(2).replace("I1.0", "I0.0");
    let a = validate_bytes(input.as_bytes()).to_json();
    let b = validate_bytes(input.as_bytes()).to_json();
    assert_eq!(a, b);
    assert!(a.starts_with(
        r#"{"validator":"tes_validator","validator_version":"0.1.0","rules_version":"1.1","ok":false,"error_count":1,"warning_count":0,"issues":[{"severity":"error","code":"DUPLICATE_IO_ADDRESS","path":"devices[1].inputs.retracted","message":"I/O address I0.0 is already used by devices[0].inputs.retracted"}]}"#
    ), "{a}");
}
