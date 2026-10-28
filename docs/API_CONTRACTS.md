# API-szerződések

Minden szerződés UTF-8 JSON, a sémák a `contracts/` mappában. Nyilvános interfész változásakor
protokoll-/sémaverzió-növelés és migrációs teszt kötelező (spec 10.2).

| Szerződés | Fájl | Verzió |
|---|---|---|
| Gépmodell | `contracts/machine-project.schema.json` + `docs/VALIDATION_RULES.md` | `schema_version` 1.1 |
| Worker-parancs | `contracts/tia-command.schema.json` | `protocol_version` 1.0 |
| Worker-eredmény | `contracts/tia-result.schema.json` | `protocol_version` 1.0 |
| Generálási manifest | `contracts/generation-manifest.schema.json` | `manifest_version` 1.0 |
| Hibakódok | `contracts/error-codes.json` | `error_codes_version` 1.0 |
| Rust FFI | `rust/tes_validator/include/tes_validator.h` | ABI 1 |
| Sandbox-marker | `.tes-sandbox.json` (`TiaStudio.Contracts.SandboxMarker`) | `marker_version` 1.0 |

## Worker hívása

```powershell
TiaStudio.TiaAdapter.V19.exe --command D:\jobs\<job_id>.command.json --result D:\jobs\<job_id>.result.json
```

Kilépési kódok: `0` Success/ValidationOnly, `1` használati hiba, `2` Blocked, `3` Failed,
`4` Unsupported, `5` Cancelled. A főalkalmazás az eredményfájlt tekinti mérvadónak; ha az hiányzik
vagy a `job_id`/verzió nem egyezik, `WORKER_CRASHED` / `WORKER_VERSION_MISMATCH`, és importnál a
sandbox állapota `Unknown`.

Műveletek: `GetCapabilities`, `ValidateJob` (mindig `dry_run=true`), `ImportAndCompile`,
`UpgradeProject` (MVP-ben mindig `Unsupported`). Letöltés/online/force művelet a protokollban **nem létezik**;
ismeretlen művelet `OPERATION_NOT_SUPPORTED`.

### `sandbox_state`

| Érték | Jelentés |
|---|---|
| `NotTouched` | a projekt meg sem nyílt |
| `Unchanged` | megnyílt, nem módosult (dry run, ütközés, offline/PLC-hiba) |
| `ModifiedNotSaved` | import történt, mentés nem (import- vagy fordítási hiba) – a sandboxot újra kell előállítani |
| `Saved` | teljes siker, mentve |
| `Unknown` | időtúllépés, összeomlás, megszakítás vagy mentési hiba – kézi ellenőrzés szükséges |

## ApprovalDigest v1

SHA-256 (kisbetűs hex) a következő UTF-8 szöveg fölött (`\n` sorvég, minden sor `kulcs=érték`):

```text
tes-approval-v1
job_id=<job_id>
operation=ImportAndCompile
tia_version=V19
project_path=<Path.GetFullPath, Windows alatt nagybetűsítve, záró elválasztó nélkül>
plc_name=PLC_1
overwrite_policy=Deny
manifest_sha256=<a manifest.json bájtjainak hash-e>
file=<import_order>:<relative_path>:<sha256>      (import_order szerint, fájlonként egy sor)
```

Sortörést tartalmazó mező elutasítva. A digest a jóváhagyáskor új `job_id`-hoz készül, így egy
jóváhagyás egyetlen futtatási kérelemre érvényes. A worker a hash-eket maga méri újra.

## Manifest-útvonalszabály

`relative_path`: előre-perjeles relatív út; tiltott az abszolút út, `..`, `.`, üres elem, backslash,
kettőspont (meghajtó), NUL; a feloldott útnak a csomaggyökér alatt kell maradnia, és egyetlen eleme
sem lehet symlink/junction. Import előtt minden SHA-256 újraszámolandó (C#: `PackageVerifier`,
Python: `generator.manifest.verify_package`).

## Generátor CLI

`python -m generator.main --input <json> --output <új mappa> [--run-id ID] [--generated-at ISO] [--validate-only]`

Stdout: pontosan egy JSON-sor (`status`: `Success` | `ValidationOnly` | `ValidationFailed` | `Failed`,
`code`, `validation`, `manifest_sha256`, `files`…); napló a stderr-en. Kilépési kód: 0 siker,
2 validációs hiba, 3 generálási/kimeneti hiba (pl. `OUTPUT_EXISTS`), 1 belső hiba.
A kimenet staging mappába készül és átnevezéssel publikálódik; meglévő mappát soha nem ír felül.
