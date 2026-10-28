# tes_validator (Rust)

A TIA Engineering Studio determinisztikus, statikus gépmodell-validátora (`schema_version` 1.1).
A normatív szabályok: [`docs/VALIDATION_RULES.md`](../../docs/VALIDATION_RULES.md); a hibakódok:
[`contracts/error-codes.json`](../../contracts/error-codes.json). A könyvtár **csak adatot kap**,
fájlrendszer- vagy TIA-hozzáférése nincs.

## Build és teszt

```powershell
cd src\rust\validator
cargo test
cargo clippy --all-targets -- -D warnings
cargo build --release --target x86_64-pc-windows-msvc
```

A kimenet: `target\x86_64-pc-windows-msvc\release\tes_validator.dll`. A DLL-t a WPF-alkalmazás
mellé kell másolni. **Csak x64**: a DLL és a WPF-folyamat architektúrájának egyeznie kell; a
kiadási csomagnak tartalmaznia/ellenőriznie kell a VC++ futtatókörnyezetet.

A `tests/contract_cases.rs` a közös, nyelvfüggetlen eseteket futtatja
(`tests/contracts/validation-cases.json`) — ugyanezeket a C# és a Python tesztek is.

## FFI-szerződés (ABI 1) — `include/tes_validator.h`

| Függvény | Leírás |
|---|---|
| `uint32_t tes_abi_version()` | ABI-verzió (1). Eltérés esetén a hívó nem használhatja a DLL-t (`VALIDATOR_UNAVAILABLE`). |
| `int32_t tes_validate(const uint8_t* in, size_t len, uint8_t** out, size_t* out_len)` | UTF-8 JSON (opcionális BOM) validálása. `0` = riport elkészült (lehet `"ok": false` is), `-1` = null pointer, `-99` = belső hiba (pánik, elkapva). |
| `void tes_free(uint8_t* ptr, size_t len)` | A `tes_validate` által adott puffer felszabadítása (null-biztos, pontosan egyszer). |

- A kimeneti puffer UTF-8 JSON, **nincs** NUL-lezárva; hossza `*out_len`.
- Rust-pánik nem lép át az FFI-határon (`catch_unwind`, ezért `panic = "unwind"` a release profilban).
- 1 MiB-nál nagyobb `len` esetén a bemenetet nem olvassuk, `INPUT_TOO_LARGE` riport jön.

C# P/Invoke minta:

```csharp
[DllImport("tes_validator.dll", CallingConvention = CallingConvention.Cdecl)]
static extern int tes_validate(byte[] input, UIntPtr inputLen, out IntPtr outJson, out UIntPtr outLen);
[DllImport("tes_validator.dll", CallingConvention = CallingConvention.Cdecl)]
static extern void tes_free(IntPtr ptr, UIntPtr len);
```
