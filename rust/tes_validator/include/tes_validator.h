/* TIA Engineering Studio – tes_validator C ABI, version 1.
 * Normative validation rules: docs/VALIDATION_RULES.md. Build: x86_64-pc-windows-msvc (x64 only).
 */
#ifndef TES_VALIDATOR_H
#define TES_VALIDATOR_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define TES_ABI_VERSION 1u

#define TES_OK 0
#define TES_ERR_NULL_POINTER (-1)
#define TES_ERR_PANIC (-99)

/* Returns the ABI version; callers must refuse to run on mismatch. */
uint32_t tes_abi_version(void);

/* Validates input_len bytes of UTF-8 JSON (optional BOM). On TES_OK, *out_json receives a
 * UTF-8 JSON report of *out_len bytes (not NUL-terminated) that must be released with tes_free.
 * A report with "ok": false is still TES_OK. On any other return value nothing is allocated. */
int32_t tes_validate(const uint8_t *input, size_t input_len, uint8_t **out_json, size_t *out_len);

/* Releases a buffer returned by tes_validate. NULL is ignored. */
void tes_free(uint8_t *ptr, size_t len);

#ifdef __cplusplus
}
#endif

#endif /* TES_VALIDATOR_H */
