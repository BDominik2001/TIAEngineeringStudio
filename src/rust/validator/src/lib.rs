//! TIA Engineering Studio – deterministic static validator of the version-independent
//! machine model (`schema_version` 1.1). Normative rules: docs/VALIDATION_RULES.md.
//!
//! The library only receives data to validate; it has no file-system or TIA access.

mod ffi;
mod report;
mod rules;

pub use ffi::{tes_abi_version, tes_free, tes_validate, ABI_VERSION};
pub use report::{Issue, Report, Severity};
pub use rules::{validate_bytes, MAX_DEVICES, MAX_INPUT_BYTES};

pub const VALIDATOR_NAME: &str = "tes_validator";
pub const VALIDATOR_VERSION: &str = env!("CARGO_PKG_VERSION");
pub const RULES_VERSION: &str = "1.1";
