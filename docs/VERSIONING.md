# Verziózás

Az alkalmazás verziója **nem azonos** a TIA Portal verziójával. A TIA V18/V19/V20 támogatását külön
adapterek és a kompatibilitási adatok ([COMPATIBILITY.md](COMPATIBILITY.md)) biztosítják, nem három
tartós Git-branch.

## Alkalmazásverzió (SemVer)

Formátum: `MAJOR.MINOR.PATCH`, előzetes kiadásnál `-alpha.N`, `-beta.N`, `-rc.N` utótaggal.

| Rész | Mikor nő |
|---|---|
| MAJOR | visszafelé nem kompatibilis nyilvános interfész vagy adatmodell (gépmodell-séma, IPC-protokoll, könyvtárformátum) |
| MINOR | új, visszafelé kompatibilis funkció |
| PATCH | visszafelé kompatibilis hibajavítás |

A `0.x` kiadások fejlesztési státuszúak, a kompatibilitás még változhat.

Egyetlen forrás: `Directory.Build.props` `<Version>`. Ugyanezt az értéket kell hordoznia a Python-generátornak
(`src/python/generator/__init__.py` `__version__`) és a Rust-validátornak (`src/rust/validator/Cargo.toml`);
ezt a `python scripts/tools/build_info.py --check` ellenőrzi a CI-ban. A .NET SDK a git-commitot az
`AssemblyInformationalVersion`-be teszi (`0.1.0+<sha>`), a felület a kezdőlapon mutatja.

Tervezett mérföldkövek (nem tényleges kiadások): `v0.1.0` első fejlesztői prototípus · `v0.2.0`
projektkezelés és validáció · `v0.3.0` PLC-forrásgenerálás · `v0.4.0` V18/V19/V20 integrációs csomag ·
`v1.0.0` első stabil, teljesen tesztelt kiadás.

## Külön nyilvántartott verziók

| Verzió | Jelenlegi | Hol van | Mikor nő |
|---|---|---|---|
| Alkalmazás | 0.1.0 | `Directory.Build.props` | lásd fent |
| TIA-kompatibilitás | V18, V19, V20 | `TiaVersions`, [COMPATIBILITY.md](COMPATIBILITY.md) | új adapter vagy tesztállapot |
| Gépmodell JSON-séma | 1.1 | `contracts/machine-project.schema.json` (`schema_version`), `MachineProject.CurrentSchemaVersion` | mezőváltozás; törő változásnál migrációs útmutató + MAJOR |
| Validációs szabálykészlet | 1.1 | `NamingRules.RulesVersion`, `tests/contracts/validation-cases.json` | szabályváltozás (mindhárom nyelven egyszerre) |
| PLC-sablonkészlet | `tes-scl-core@1.0.0` | `templates/scl/template-set.json` | bármely sablonbájt változása; a golden SCL-fájlokat tudatosan újra kell generálni |
| IPC-protokoll | 1.1 (1.0 elfogadott) | `ProtocolVersions.Current`, `contracts/tia-command.schema.json` | új művelet vagy mező; régi verzió elfogadása dokumentálva |
| Generálási manifest | 1.0 / könyvtár 1.1 | `GenerationManifest` | mezőváltozás |
| Blokk-könyvtár formátum | 1.0 | `BlockLibraryIndex.CurrentFormatVersion` | mezőváltozás |
| Rust C ABI | 1 | `TES_ABI_VERSION` (`tes_validator.h`) | bármely ABI-változás; a hívó eltérésnél megtagadja a futást |
| Openness assembly | 18.0.0.0 / 19.0.0.0 / 20.0.0.0 | adapter csproj `TiaAssemblyVersion` | új TIA Update, ha az assembly-verzió változik |

A séma-, protokoll- vagy sablonverzió a felhasználói kiadástól függetlenül is nőhet; a változást a
[CHANGELOG.md](../CHANGELOG.md) adott kiadásának bejegyzése jelöli.

## Buildazonosító (`build-info.json`)

A `scripts/build-desktop.ps1` és a `scripts/package-release.ps1` minden kiadási mappába és workerenként
`build-info.json`-t ír (`scripts/tools/build_info.py`):

```json
{
  "appVersion": "0.1.0",
  "gitCommitSha": "…",
  "gitDirty": false,
  "buildTimestampUtc": "2026-10-28T20:35:21Z",
  "targetTiaVersion": "V19",
  "adapterVersion": { "workerVersion": "0.1.0", "opennessAssemblyVersion": "19.0.0.0" },
  "schemaVersion": "1.1",
  "protocolVersion": "1.1",
  "templateCatalogVersion": "tes-scl-core@1.0.0",
  "templateCatalogSha256": "…",
  "dependencyLockfileHash": "…",
  "tiaIntegrationTested": false
}
```

A `tiaIntegrationTested` a buildben mindig `false`: TIA-kompatibilitást csak a TIA-integrációs teszt
eredménye igazol (a kiadási manifestben csomagonként szerepel).

## Törő változás

Séma- vagy API-törésnél: MAJOR (1.0 után) vagy MINOR (0.x alatt) verzióemelés, migrációs útmutató a
CHANGELOG-ban és a PR-ban, a régi formátum elutasítása egyértelmű hibakóddal (pl. `UNSUPPORTED_SCHEMA_VERSION`).
