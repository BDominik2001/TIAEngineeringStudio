//! C ABI (see include/tes_validator.h). Panics never cross this boundary.

use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;

use crate::rules::{too_large_report, validate_bytes, MAX_INPUT_BYTES};

pub const ABI_VERSION: u32 = 1;

pub const TES_OK: i32 = 0;
pub const TES_ERR_NULL_POINTER: i32 = -1;
pub const TES_ERR_PANIC: i32 = -99;

/// Returns the FFI contract version. Callers must refuse to use the library on mismatch.
#[no_mangle]
pub extern "C" fn tes_abi_version() -> u32 {
    ABI_VERSION
}

/// Validates `input_len` bytes of UTF-8 JSON at `input` and returns the report.
///
/// On `TES_OK` (0) `*out_json` points to a UTF-8 JSON report of `*out_len` bytes (no NUL
/// terminator) that must be released with [`tes_free`]. A report with `"ok": false` is still
/// `TES_OK`. Returns `TES_ERR_NULL_POINTER` (-1) if any pointer is null and `TES_ERR_PANIC`
/// (-99) on an internal error; in both cases nothing is allocated.
///
/// # Safety
/// `input` must be valid for reads of `input_len` bytes for the duration of the call;
/// `out_json` and `out_len` must be valid for writes.
#[no_mangle]
pub unsafe extern "C" fn tes_validate(
    input: *const u8,
    input_len: usize,
    out_json: *mut *mut u8,
    out_len: *mut usize,
) -> i32 {
    if input.is_null() || out_json.is_null() || out_len.is_null() {
        return TES_ERR_NULL_POINTER;
    }
    *out_json = ptr::null_mut();
    *out_len = 0;
    let result = catch_unwind(AssertUnwindSafe(|| {
        let report = if input_len > MAX_INPUT_BYTES {
            // Do not touch an oversized buffer at all.
            too_large_report(input_len)
        } else {
            validate_bytes(std::slice::from_raw_parts(input, input_len))
        };
        report.to_json().into_bytes().into_boxed_slice()
    }));
    match result {
        Ok(buffer) => {
            *out_len = buffer.len();
            *out_json = Box::into_raw(buffer) as *mut u8;
            TES_OK
        }
        Err(_) => TES_ERR_PANIC,
    }
}

/// Releases a buffer returned by [`tes_validate`]. Null is ignored.
///
/// # Safety
/// `ptr`/`len` must be exactly a pair returned by `tes_validate`, freed at most once.
#[no_mangle]
pub unsafe extern "C" fn tes_free(ptr: *mut u8, len: usize) {
    if ptr.is_null() {
        return;
    }
    drop(Box::from_raw(ptr::slice_from_raw_parts_mut(ptr, len)));
}

#[cfg(test)]
mod tests {
    use super::*;

    unsafe fn call(input: &[u8]) -> (i32, String) {
        let mut out: *mut u8 = ptr::null_mut();
        let mut len = 0usize;
        let rc = tes_validate(input.as_ptr(), input.len(), &mut out, &mut len);
        let text = if out.is_null() {
            String::new()
        } else {
            String::from_utf8(std::slice::from_raw_parts(out, len).to_vec()).unwrap()
        };
        tes_free(out, len);
        (rc, text)
    }

    #[test]
    fn abi_version_is_one() {
        assert_eq!(tes_abi_version(), 1);
    }

    #[test]
    fn roundtrip_returns_report() {
        let (rc, json) = unsafe { call(b"[]") };
        assert_eq!(rc, TES_OK);
        let v: serde_json::Value = serde_json::from_str(&json).unwrap();
        assert_eq!(v["ok"], false);
        assert_eq!(v["issues"][0]["code"], "INVALID_JSON");
    }

    #[test]
    fn null_pointers_are_rejected() {
        let mut out: *mut u8 = ptr::null_mut();
        let mut len = 0usize;
        unsafe {
            assert_eq!(tes_validate(ptr::null(), 0, &mut out, &mut len), TES_ERR_NULL_POINTER);
            let input = b"{}";
            assert_eq!(
                tes_validate(input.as_ptr(), 2, ptr::null_mut(), &mut len),
                TES_ERR_NULL_POINTER
            );
            assert_eq!(
                tes_validate(input.as_ptr(), 2, &mut out, ptr::null_mut()),
                TES_ERR_NULL_POINTER
            );
            tes_free(ptr::null_mut(), 0);
        }
        assert!(out.is_null());
    }

    #[test]
    fn oversized_length_is_not_read() {
        let input = *b"{";
        let mut out: *mut u8 = ptr::null_mut();
        let mut len = 0usize;
        unsafe {
            // The length lies; the buffer must not be dereferenced.
            let rc = tes_validate(input.as_ptr(), MAX_INPUT_BYTES + 1, &mut out, &mut len);
            assert_eq!(rc, TES_OK);
            let json = std::str::from_utf8(std::slice::from_raw_parts(out, len))
                .unwrap()
                .to_string();
            tes_free(out, len);
            assert!(json.contains("INPUT_TOO_LARGE"));
        }
    }
}
