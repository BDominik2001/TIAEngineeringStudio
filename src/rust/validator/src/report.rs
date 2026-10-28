//! Validation report and its stable JSON serialization (docs/VALIDATION_RULES.md, section 8).

use crate::{RULES_VERSION, VALIDATOR_NAME, VALIDATOR_VERSION};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Severity {
    Error,
    Warning,
}

impl Severity {
    pub fn as_str(self) -> &'static str {
        match self {
            Severity::Error => "error",
            Severity::Warning => "warning",
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Issue {
    pub severity: Severity,
    /// Code from contracts/error-codes.json.
    pub code: &'static str,
    pub path: String,
    pub message: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Report {
    pub issues: Vec<Issue>,
}

fn json_string(s: &str) -> String {
    serde_json::to_string(s).expect("serializing a str cannot fail")
}

impl Report {
    pub(crate) fn new(issues: Vec<Issue>) -> Self {
        Report { issues }
    }

    pub fn error_count(&self) -> usize {
        self.issues.iter().filter(|i| i.severity == Severity::Error).count()
    }

    pub fn warning_count(&self) -> usize {
        self.issues.iter().filter(|i| i.severity == Severity::Warning).count()
    }

    pub fn ok(&self) -> bool {
        self.error_count() == 0
    }

    /// Compact JSON with a fixed key order; byte-identical for identical input.
    pub fn to_json(&self) -> String {
        let issues: Vec<String> = self
            .issues
            .iter()
            .map(|i| {
                format!(
                    "{{\"severity\":{},\"code\":{},\"path\":{},\"message\":{}}}",
                    json_string(i.severity.as_str()),
                    json_string(i.code),
                    json_string(&i.path),
                    json_string(&i.message)
                )
            })
            .collect();
        format!(
            "{{\"validator\":{},\"validator_version\":{},\"rules_version\":{},\"ok\":{},\"error_count\":{},\"warning_count\":{},\"issues\":[{}]}}",
            json_string(VALIDATOR_NAME),
            json_string(VALIDATOR_VERSION),
            json_string(RULES_VERSION),
            self.ok(),
            self.error_count(),
            self.warning_count(),
            issues.join(",")
        )
    }
}
