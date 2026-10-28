# Validációs szabályok (gépmodell `schema_version` 1.1)

Ez a dokumentum a **normatív** szabálykészlet. A `contracts/machine-project.schema.json`
a szerkezetet írja le; az alábbi szemantikai szabályokat **mindhárom** implementáció
ellenőrzi, azonos hibakódokkal (`contracts/error-codes.json`):

| Réteg | Implementáció | Mikor fut |
|---|---|---|
| C# | `TiaStudio.Infrastructure.Json.MachineProjectJsonValidator` (primitívek: `TiaStudio.Domain.Rules`) | DRAFT → VALIDATED (azonnali GUI-visszajelzés) |
| Rust | `rust/tes_validator` (`tes_validate`) | VALIDATED → CHECKED (determinisztikus statikus ellenőrzés) |
| Python | `python/generator/validation.py` | generálás előtt, védelmi mélységként |

A hibák sorrendje determinisztikus: a dokumentumbejárás sorrendjében (mezők a lenti
sorrendben, eszközök indexsorrendben) keletkeznek. Az útvonal (`path`) formátuma:
`target.plc_name`, `devices[3].inputs.extended`.

## 1. Bemenet

| Szabály | Kód |
|---|---|
| Bemenet legfeljebb 1 048 576 bájt | `INPUT_TOO_LARGE` |
| Érvényes UTF-8 JSON, gyökér objektum; a kezdő UTF-8 BOM megengedett és figyelmen kívül marad; `NaN`/`Infinity` nem megengedett; ismétlődő kulcsnál az utolsó érték érvényes | `INVALID_JSON` |

## 2. Struktúra (minden objektumra)

| Szabály | Kód |
|---|---|
| Kötelező mező hiányzik | `MISSING_FIELD` |
| Mező értéke `null` | `NULL_VALUE` |
| Nem dokumentált mező | `UNKNOWN_FIELD` |
| Hibás JSON-típus (pl. szám string helyett, tört szám integer helyett, `true` szám helyett) | `INVALID_TYPE` |

Mezősorrend az ellenőrzéskor: gyökér `schema_version, project_name, description, target, devices`;
target `tia_version, plc_name, cpu_family`; eszköz `id, kind, description, inputs, parameters`;
inputs `retracted, extended`. Az ismeretlen mezőket ábécérendben jelentjük az adott objektum
ismert mezői után.

## 3. Gyökér és cél

| Mező | Szabály | Kód |
|---|---|---|
| `schema_version` | pontosan `"1.1"` (az 1.0 tudatosan nem támogatott, lásd ROADMAP) | `UNSUPPORTED_SCHEMA_VERSION` |
| `project_name` | ASCII; `^[A-Za-z][A-Za-z0-9_]{0,63}$` | `NON_ASCII` / `INVALID_IDENTIFIER` |
| `description` (opcionális) | nyomtatható ASCII (0x20–0x7E), legfeljebb 200 karakter | `NON_ASCII` / `INVALID_VALUE` |
| `target.tia_version` | `V18` \| `V19` \| `V20` | `UNSUPPORTED_TIA_VERSION` |
| `target.plc_name` | ASCII; `^[A-Za-z][A-Za-z0-9_\-]{0,63}$` | `NON_ASCII` / `INVALID_IDENTIFIER` |
| `target.cpu_family` | `S7-1200` \| `S7-1500` | `UNSUPPORTED_CPU_FAMILY` |
| `devices` | tömb, 1..256 elem | `NO_DEVICES` / `TOO_MANY_DEVICES` |

A `NON_ASCII` megelőzi a mintaellenőrzést: nem-ASCII azonosítóra csak `NON_ASCII` jön.

## 4. Eszköz

| Mező | Szabály | Kód |
|---|---|---|
| `id` | ASCII; `^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$`, legfeljebb 32 karakter | `NON_ASCII` / `INVALID_IDENTIFIER` |
| `id` | egyedi, **kis- és nagybetűtől függetlenül** (a második előfordulásnál jelezzük) | `DUPLICATE_DEVICE_ID` |
| `kind` | jelenleg csak `cylinder` (ismeretlen fajtánál a további mezőket nem vizsgáljuk) | `UNSUPPORTED_DEVICE_KIND` |
| `description` (opcionális) | nyomtatható ASCII, legfeljebb 120 karakter | `NON_ASCII` / `INVALID_VALUE` |
| `inputs` | objektum, `retracted` és `extended` kötelező | `MISSING_FIELD` stb. |
| `parameters` (opcionális) | objektum, `monitor_time_ms` egész, 100..60000 (alapérték 2000) | `INVALID_TYPE` / `INVALID_VALUE` |

## 5. I/O-címek

| Szabály | Kód |
|---|---|
| Szintaxis: `^I(0\|[1-9][0-9]{0,4})\.[0-7]$` (prefix `%` nélkül, vezető nulla nélkül) | `INVALID_IO_ADDRESS` |
| Bájtcím a CPU-család bemeneti folyamatkép-tartományán belül: S7-1200 → 0..1023, S7-1500 → 0..32767 | `IO_ADDRESS_OUT_OF_RANGE` |
| Ugyanaz a cím nem fordulhat elő kétszer (eszközön belül és eszközök között sem; a második előfordulásnál jelezzük, az üzenet megnevezi az elsőt) | `DUPLICATE_IO_ADDRESS` |

Alias (szándékos többszörös felhasználás) **nem** támogatott; ha szükséges lesz, külön,
explicit engedélyezett szabályként kell modellezni.

## 6. Generált nevek

| Szabály | Kód |
|---|---|
| `DB_Cylinder_<id>` legfeljebb 125 karakter (az `id` korlátja miatt jelenleg mindig teljesül; a szabály a későbbi eszközosztályok miatt marad) | `GENERATED_NAME_TOO_LONG` |
| Generált név nem egyezhet a sablonkészlet közös blokkjainak nevével: `FB_Cylinder`, `FC_Cylinders` (kis/nagybetűtől függetlenül) | `GENERATED_NAME_COLLISION` |

## 6/b. Pontosítások (mindhárom implementációban így működik)

1. Nem-ASCII karaktert tartalmazó I/O-cím `INVALID_IO_ADDRESS` (nem `NON_ASCII`; a `NON_ASCII`-elsőbbség csak azonosítókra és leírásokra vonatkozik).
2. A generált névszabályok (`GENERATED_NAME_TOO_LONG`, `GENERATED_NAME_COLLISION`) csak érvényes `id` és támogatott `kind` mellett futnak, a `kind` ellenőrzése után, `devices[i].id` útvonallal.
3. Opcionális mező `null` értékkel `NULL_VALUE`; ismeretlen mező `null` értékkel csak `UNKNOWN_FIELD`.
4. `monitor_time_ms`: bármely tört szám (a `2000.0` is) `INVALID_TYPE`; negatív vagy 64 biten nem ábrázolható egész `INVALID_VALUE`.
5. Címek sorrendje: szintaxis → tartomány (érvénytelen CPU-családnál kimarad) → duplikáció; csak a tartományon belüli címek kerülnek a duplikáció-nyilvántartásba.
6. Ismeretlen eszközfajtánál (vagy hiányzó/hibás `kind`-nál) az eszköz további mezőit, az ismeretlen mezőit is beleértve, nem vizsgáljuk; az `id` ellenőrzése ekkor is lefut.

## 7. Figyelmeztetés

Ha nincs hiba, egyetlen `IO_HW_NOT_VERIFIED` figyelmeztetés (`path` = `devices`) jelzi, hogy a
címek létezését a TIA hardverkonfigurációban kézzel kell igazolni. A figyelmeztetés nem
blokkol, de a riportban és a manifestben megjelenik.

## 8. Riport formátuma (Rust FFI és Python közös)

```json
{
  "validator": "tes_validator",
  "validator_version": "0.1.0",
  "rules_version": "1.1",
  "ok": false,
  "error_count": 1,
  "warning_count": 0,
  "issues": [
    { "severity": "error", "code": "DUPLICATE_IO_ADDRESS", "path": "devices[1].inputs.retracted",
      "message": "I/O address I0.0 is already used by devices[0].inputs.retracted" }
  ]
}
```
